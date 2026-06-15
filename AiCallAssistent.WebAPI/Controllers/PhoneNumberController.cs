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

    /// <summary>Returns available Dutch Twilio phone numbers for purchase.</summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailable()
    {
        var (_, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var url = $"{TwilioRestBase}/2010-04-01/Accounts/{_twilio.AccountSid}"
            + "/AvailablePhoneNumbers/NL/Local.json?Limit=10";

        logger.LogInformation("Fetching available numbers. AccountSid={AccountSid} ApiKeySid={ApiKeySid} URL={Url}",
            _twilio.AccountSid, _twilio.ApiKeySid, url);

        var client = CreateTwilioClient();
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        logger.LogInformation("Twilio available numbers response: {Status} {Body}",
            (int)response.StatusCode, body);

        if (!response.IsSuccessStatusCode)
        {
            // 404 from Twilio for NL numbers = regulatory compliance not yet set up.
            // Return empty list so the UI shows "no numbers available" rather than crashing.
            logger.LogWarning("Twilio AvailablePhoneNumbers returned {Status}: {Body}",
                (int)response.StatusCode, body);
            return Ok(Array.Empty<AvailablePhoneNumberDto>());
        }

        var root = JsonNode.Parse(body);
        var available = root?["available_phone_numbers"]?.AsArray()
            .Select(n => new AvailablePhoneNumberDto(
                n!["phone_number"]!.GetValue<string>(),
                n["friendly_name"]!.GetValue<string>()))
            .ToList() ?? [];

        return Ok(available);
    }

    /// <summary>Purchases a Twilio phone number and saves it to the company account.</summary>
    [HttpPost("purchase")]
    public async Task<IActionResult> Purchase([FromBody] PurchasePhoneNumberRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var voiceUrl = $"{_twilio.BaseUrl}/api/twilio/answer";
        var statusCallback = $"{_twilio.BaseUrl}/api/twilio/status";

        var url = $"{TwilioRestBase}/2010-04-01/Accounts/{_twilio.AccountSid}/IncomingPhoneNumbers.json";
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PhoneNumber"]          = request.PhoneNumber,
            ["VoiceUrl"]             = voiceUrl,
            ["VoiceMethod"]          = "POST",
            ["StatusCallback"]       = statusCallback,
            ["StatusCallbackMethod"] = "POST",
        });

        var client = CreateTwilioClient();
        var response = await client.PostAsync(url, formContent);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, "Twilio purchase failed: " + body);

        var root = JsonNode.Parse(body);
        var purchasedNumber = root?["phone_number"]?.GetValue<string>() ?? request.PhoneNumber;
        var friendlyName = root?["friendly_name"]?.GetValue<string>() ?? purchasedNumber;

        var phoneNumber = new PhoneNumber
        {
            CompanyId = companyId,
            AiPhoneNumber = purchasedNumber,
            EscalationPhoneNumber = null,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
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
