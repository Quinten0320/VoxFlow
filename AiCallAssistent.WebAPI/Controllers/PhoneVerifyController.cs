using AiCallAssistent.Application.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/verify")]
[Authorize]
public class PhoneVerifyController(
    IHttpClientFactory httpClientFactory,
    IOptions<TwilioSettings> twilioOptions,
    ILogger<PhoneVerifyController> logger) : ControllerBase
{
    private readonly TwilioSettings _twilio = twilioOptions.Value;

    private static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = raw.Trim().Replace(" ", "").Replace("-", "");
        if (n.StartsWith('+'))  return n;
        if (n.StartsWith("00")) return '+' + n[2..];
        if (n.StartsWith("0"))  return "+31" + n[1..];
        return n;
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] PhoneVerifySendRequest request)
    {
        if (string.IsNullOrWhiteSpace(_twilio.VerifyServiceSid))
            return StatusCode(503, new { error = "SMS-verificatie is niet geconfigureerd." });

        var phone = NormalizePhone(request.PhoneNumber?.Trim());
        if (string.IsNullOrWhiteSpace(phone) || !Regex.IsMatch(phone, @"^\+[1-9]\d{6,14}$"))
            return BadRequest(new { error = "Vul een geldig telefoonnummer in (bijv. 0612345678 of +31612345678)." });

        try
        {
            var client = httpClientFactory.CreateClient("Twilio");
            var url = $"https://verify.twilio.com/v2/Services/{_twilio.VerifyServiceSid}/Verifications";

            var response = await client.PostAsync(url, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["To"]      = phone,
                    ["Channel"] = "sms",
                }));

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogError("Twilio Verify send failed ({Status}): {Body}", (int)response.StatusCode, body);
                return StatusCode(502, new { error = "Kon de verificatiecode niet versturen. Probeer het opnieuw." });
            }

            return Ok(new { sent = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Twilio Verify send threw");
            return StatusCode(502, new { error = "Kon de verificatiecode niet versturen." });
        }
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] PhoneVerifyConfirmRequest request)
    {
        if (string.IsNullOrWhiteSpace(_twilio.VerifyServiceSid))
            return StatusCode(503, new { error = "SMS-verificatie is niet geconfigureerd." });

        var phone = NormalizePhone(request.PhoneNumber?.Trim());
        var code  = request.Code?.Trim();

        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(code))
            return BadRequest(new { error = "Telefoonnummer en code zijn verplicht." });

        try
        {
            var client = httpClientFactory.CreateClient("Twilio");
            var url = $"https://verify.twilio.com/v2/Services/{_twilio.VerifyServiceSid}/VerificationCheck";

            var response = await client.PostAsync(url, new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["To"]   = phone,
                    ["Code"] = code,
                }));

            var json = await response.Content.ReadFromJsonAsync<TwilioVerifyCheckResponse>();

            if (json?.Status == "approved")
                return Ok(new { verified = true });

            return Ok(new { verified = false, error = "Ongeldige of verlopen code." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Twilio Verify check threw");
            return StatusCode(502, new { error = "Verificatie mislukt. Probeer het opnieuw." });
        }
    }
}

public record PhoneVerifySendRequest(string PhoneNumber);
public record PhoneVerifyConfirmRequest(string PhoneNumber, string Code);
internal record TwilioVerifyCheckResponse(string? Status);
