using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/phone-numbers")]
public class PhoneNumberController(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IOptions<TwilioSettings> twilioOptions,
    ILogger<PhoneNumberController> logger) : DashboardControllerBase(db)
{
    private readonly TwilioSettings _twilio = twilioOptions.Value;

    private const string TwilioRestBase = "https://api.twilio.com";
    /// <summary>Returns all phone numbers assigned to the company.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var numbers = await Db.PhoneNumbers
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.AiPhoneNumber)
            .Select(p => new PhoneNumberDto(
                p.PhoneNumberId,
                p.AiPhoneNumber,
                p.EscalationPhoneNumber,
                p.IsActive,
                p.CreatedAt))
            .ToListAsync();

        return Ok(numbers);
    }

    /// <summary>Adds a new phone number for the company.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePhoneNumberRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var phoneNumber = new PhoneNumber
        {
            CompanyId = companyId,
            AiPhoneNumber = request.AiPhoneNumber,
            EscalationPhoneNumber = request.EscalationPhoneNumber,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Db.PhoneNumbers.Add(phoneNumber);
        await Db.SaveChangesAsync();

        var dto = new PhoneNumberDto(
            phoneNumber.PhoneNumberId,
            phoneNumber.AiPhoneNumber,
            phoneNumber.EscalationPhoneNumber,
            phoneNumber.IsActive,
            phoneNumber.CreatedAt);

        return Created(string.Empty, dto);
    }

    /// <summary>Updates escalation number and active state.</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdatePhoneNumberRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.PhoneNumbers
            .Where(p => p.PhoneNumberId == id && p.CompanyId == companyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.EscalationPhoneNumber, request.EscalationPhoneNumber)
                .SetProperty(p => p.IsActive, request.IsActive));

        if (rows == 0)
            return NotFound();

        return NoContent();
    }

    /// <summary>Deletes a phone number.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.PhoneNumbers
            .Where(p => p.PhoneNumberId == id && p.CompanyId == companyId)
            .ExecuteDeleteAsync();

        if (rows == 0)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Returns Dutch phone numbers available to purchase (Local, Mobile, TollFree).
    /// Owned numbers are excluded — use the manual input if you want to reuse an existing number.
    /// </summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailable()
    {
        var error = EnsureAuthenticated();
        if (error != null) return error;

        var client = CreateTwilioClient();
        var result = new List<AvailablePhoneNumberDto>();

        // Try Local, Mobile and TollFree — Mobile/TollFree have lighter regulatory requirements
        var types = new[] { "Local", "Mobile", "TollFree" };
        foreach (var type in types)
        {
            try
            {
                var url  = $"{TwilioRestBase}/2010-04-01/Accounts/{_twilio.AccountSid}/AvailablePhoneNumbers/NL/{type}.json?Limit=10";
                var resp = await client.GetAsync(url);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("Twilio NL/{Type} returned {Status}: {Body}", type, (int)resp.StatusCode, body);
                    continue;
                }

                var numbers = JsonNode.Parse(body)?["available_phone_numbers"]?.AsArray() ?? [];
                foreach (var n in numbers)
                {
                    var num  = n!["phone_number"]?.GetValue<string>();
                    var name = n["friendly_name"]?.GetValue<string>() ?? num ?? "";
                    if (num != null)
                        result.Add(new AvailablePhoneNumberDto(num, name, IsOwned: false));
                }

                logger.LogInformation("Twilio NL/{Type}: {Count} numbers", type, numbers.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not fetch NL/{Type} numbers", type);
            }
        }

        return Ok(result);
    }

    /// <summary>
    /// Activates a phone number for this company.
    /// If the number is already owned (IsOwned=true), reconfigures its webhooks.
    /// Otherwise purchases it from Twilio.
    /// </summary>
    [HttpPost("purchase")]
    public async Task<IActionResult> Purchase([FromBody] PurchasePhoneNumberRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var voiceUrl       = $"{_twilio.BaseUrl}/api/twilio/answer";
        var statusCallback = $"{_twilio.BaseUrl}/api/twilio/status";
        var client         = CreateTwilioClient();

        string purchasedNumber;

        if (request.IsOwned)
        {
            // Find the SID of the owned number so we can update its config
            var listUrl  = $"{TwilioRestBase}/2010-04-01/Accounts/{_twilio.AccountSid}/IncomingPhoneNumbers.json?PhoneNumber={Uri.EscapeDataString(request.PhoneNumber)}";
            var listResp = await client.GetAsync(listUrl);
            var listBody = await listResp.Content.ReadAsStringAsync();

            if (!listResp.IsSuccessStatusCode)
                return StatusCode((int)listResp.StatusCode, "Twilio lookup failed: " + listBody);

            var sid = JsonNode.Parse(listBody)?["incoming_phone_numbers"]?[0]?["sid"]?.GetValue<string>();
            if (sid == null)
                return BadRequest(new { error = "Nummer niet gevonden in Twilio-account." });

            var updateUrl = $"{TwilioRestBase}/2010-04-01/Accounts/{_twilio.AccountSid}/IncomingPhoneNumbers/{sid}.json";
            var updateResp = await client.PostAsync(updateUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["VoiceUrl"]             = voiceUrl,
                ["VoiceMethod"]          = "POST",
                ["StatusCallback"]       = statusCallback,
                ["StatusCallbackMethod"] = "POST",
            }));

            if (!updateResp.IsSuccessStatusCode)
            {
                var body = await updateResp.Content.ReadAsStringAsync();
                return StatusCode((int)updateResp.StatusCode, "Twilio update failed: " + body);
            }

            purchasedNumber = request.PhoneNumber;
            logger.LogInformation("Reconfigured existing Twilio number {Number} for company {CompanyId}", purchasedNumber, companyId);
        }
        else
        {
            // Purchase a new number
            var buyUrl = $"{TwilioRestBase}/2010-04-01/Accounts/{_twilio.AccountSid}/IncomingPhoneNumbers.json";
            var buyResp = await client.PostAsync(buyUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["PhoneNumber"]          = request.PhoneNumber,
                ["VoiceUrl"]             = voiceUrl,
                ["VoiceMethod"]          = "POST",
                ["StatusCallback"]       = statusCallback,
                ["StatusCallbackMethod"] = "POST",
            }));

            var buyBody = await buyResp.Content.ReadAsStringAsync();
            if (!buyResp.IsSuccessStatusCode)
                return StatusCode((int)buyResp.StatusCode, "Twilio purchase failed: " + buyBody);

            purchasedNumber = JsonNode.Parse(buyBody)?["phone_number"]?.GetValue<string>() ?? request.PhoneNumber;
            logger.LogInformation("Purchased new Twilio number {Number} for company {CompanyId}", purchasedNumber, companyId);
        }

        // Remove any existing phone number record for this company to avoid duplicates
        await Db.PhoneNumbers
            .Where(p => p.CompanyId == companyId)
            .ExecuteDeleteAsync();

        var phoneNumber = new PhoneNumber
        {
            CompanyId             = companyId,
            AiPhoneNumber         = purchasedNumber,
            EscalationPhoneNumber = "",
            IsActive              = true,
            CreatedAt             = DateTimeOffset.UtcNow,
        };

        Db.PhoneNumbers.Add(phoneNumber);
        await Db.SaveChangesAsync();

        return Created(string.Empty, new PhoneNumberDto(
            phoneNumber.PhoneNumberId,
            phoneNumber.AiPhoneNumber,
            phoneNumber.EscalationPhoneNumber,
            phoneNumber.IsActive,
            phoneNumber.CreatedAt));
    }

    private HttpClient CreateTwilioClient()
    {
        var client = httpClientFactory.CreateClient();
        var credentials = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes($"{_twilio.ApiKeySid}:{_twilio.ApiKeySecret}"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        return client;
    }
}
