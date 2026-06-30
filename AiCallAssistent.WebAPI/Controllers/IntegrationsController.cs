using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using AiCallAssistent.Infrastructure.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/integrations")]
public class IntegrationsController(
    AppDbContext db,
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<OutlookSettings> outlookSettings,
    IHttpClientFactory httpClientFactory,
    IOutlookCalendarService outlookCalendar,
    IMemoryCache cache,
    ILogger<IntegrationsController> logger,
    EmailSender emailSender,
    EmailTemplateService emailTemplates,
    IConfiguration configuration) : DashboardControllerBase(db)
{
    private static readonly string[] Scopes = ["Calendars.ReadWrite", "offline_access", "User.Read"];

    private const string AuthorizeEndpoint =
        "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";

    private const string TokenEndpoint =
        "https://login.microsoftonline.com/common/oauth2/v2.0/token";

    [HttpGet("outlook/status")]
    public async Task<IActionResult> OutlookStatus()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var token = await Db.CompanyOutlookTokens
            .Where(t => t.CompanyId == companyId)
            .Select(t => new { t.OutlookEmail, t.UpdatedAt })
            .FirstOrDefaultAsync();

        return Ok(new
        {
            connected = token != null,
            email = token?.OutlookEmail,
            lastSync = token?.UpdatedAt
        });
    }

    /// <summary>Returns the Microsoft OAuth URL. The frontend should redirect to it (not a popup).</summary>
    [HttpGet("outlook/connect")]
    public async Task<IActionResult> OutlookConnect()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var s = outlookSettings.Value;

        // Use a random GUID as the OAuth state to prevent CSRF.
        // Store companyId under that key for 10 minutes — enough for the OAuth round-trip.
        var oauthState = Guid.NewGuid().ToString("N");
        cache.Set($"oauth_state_{oauthState}", companyId, TimeSpan.FromMinutes(10));

        var authUrl =
            $"{AuthorizeEndpoint}" +
            $"?client_id={Uri.EscapeDataString(s.ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(s.RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(string.Join(" ", Scopes))}" +
            $"&state={oauthState}" +
            $"&prompt=select_account";

        return Ok(new { authUrl });
    }

    /// <summary>
    /// Receives the authorization code from Microsoft, exchanges it for tokens,
    /// stores them, then redirects to the dashboard.
    /// Anonymous because this is a browser redirect — no JWT is available.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("outlook/callback")]
    public async Task<IActionResult> OutlookCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] string? error_description)
    {
        if (error != null)
        {
            logger.LogWarning("Outlook OAuth error: {Error} — {Description}", error, error_description);
            return BadRequest(new { error, description = error_description });
        }

        if (code == null || state == null ||
            !cache.TryGetValue($"oauth_state_{state}", out short companyId))
            return BadRequest(new { error = "Invalid callback parameters" });

        // Consume the state so it cannot be replayed
        cache.Remove($"oauth_state_{state}");

        var s = outlookSettings.Value;
        var client = httpClientFactory.CreateClient();

        var tokenResponse = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = s.ClientId,
                ["client_secret"] = s.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = s.RedirectUri,
                ["scope"] = string.Join(" ", Scopes)
            }));

        if (!tokenResponse.IsSuccessStatusCode)
        {
            logger.LogError("Outlook token exchange failed for company {CompanyId}: HTTP {Status}",
                companyId, tokenResponse.StatusCode);
            return StatusCode(502, new { error = "Token exchange with Microsoft failed" });
        }

        var json = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = json.GetProperty("access_token").GetString()!;
        var refreshToken = json.GetProperty("refresh_token").GetString()!;
        var expiresIn = json.GetProperty("expires_in").GetInt32();

        string? email = null;
        try
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            var me = await client.GetFromJsonAsync<JsonElement>("https://graph.microsoft.com/v1.0/me");
            email = me.TryGetProperty("mail", out var mail) ? mail.GetString()
                  : me.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString()
                  : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not fetch Outlook email for company {CompanyId}", companyId);
        }

        var existing = await Db.CompanyOutlookTokens.FindAsync(companyId);
        if (existing == null)
        {
            Db.CompanyOutlookTokens.Add(new CompanyOutlookToken
            {
                CompanyId = companyId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                OutlookEmail = email,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.AccessToken = accessToken;
            existing.RefreshToken = refreshToken;
            existing.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            existing.OutlookEmail = email;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await Db.SaveChangesAsync();
        await LogAuditAsync(companyId, $"Outlook gekoppeld ({email})");

        logger.LogInformation("Outlook connected for company {CompanyId} ({Email})", companyId, email);

        // Send integration connected email to the company owner
        var ownerEmp = await Db.Employees
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.IsOwner && e.IsActive && e.Email != null);
        if (ownerEmp?.Email != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var (s, h) = emailTemplates.IntegrationConnected(ownerEmp.Name, "Microsoft Outlook");
                    await emailSender.SendNowAsync(companyId, ownerEmp.Email, ownerEmp.Name,
                        "integration_connected", s, h);
                }
                catch { }
            });
        }

        try
        {
            var employeeId = await Db.Employees
                .Where(e => e.CompanyId == companyId && e.IsActive)
                .Select(e => (long?)e.EmployeeId)
                .FirstOrDefaultAsync();

            if (employeeId != null)
            {
                var (imported, skipped) = await DoImportAsync(companyId, employeeId.Value,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(365));
                logger.LogInformation(
                    "Auto-import on connect for company {CompanyId}: {Imported} imported, {Skipped} skipped",
                    companyId, imported, skipped);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto-import failed for company {CompanyId}", companyId);
        }

        var redirectUrl = string.IsNullOrWhiteSpace(s.DashboardUrl)
            ? "/swagger"
            : s.DashboardUrl + "?outlook=connected";

        return Redirect(redirectUrl);
    }

    /// <summary>Re-syncs Outlook events into the database. Skips events that already exist.</summary>
    [HttpPost("outlook/import")]
    public async Task<IActionResult> OutlookImport(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var employeeId = await Db.Employees
            .Where(e => e.CompanyId == companyId && e.IsActive)
            .Select(e => (long?)e.EmployeeId)
            .FirstOrDefaultAsync();

        if (employeeId == null)
            return BadRequest(new { error = "No active employee found for this company." });

        var (imported, skipped) = await DoImportAsync(
            companyId, employeeId.Value,
            from ?? DateTimeOffset.UtcNow,
            to ?? DateTimeOffset.UtcNow.AddDays(365));

        return Ok(new { imported, skipped });
    }

    private async Task<(int imported, int skipped)> DoImportAsync(
        short companyId, long employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        var events = await outlookCalendar.GetEventsAsync(companyId, rangeFrom, rangeTo);
        if (events.Count == 0) return (0, 0);

        var existingStarts = await Db.Appointments
            .Where(a => a.CompanyId == companyId && a.StartTime >= rangeFrom && a.StartTime <= rangeTo)
            .Select(a => a.StartTime)
            .ToListAsync();
        var existingSet = existingStarts.ToHashSet();

        var toInsert = events
            .Where(ev => !existingSet.Contains(ev.Start))
            .Select(ev => new Appointment
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                Type = "imported",
                Description = ev.Subject,
                StartTime = ev.Start,
                EndTime = ev.End,
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        if (toInsert.Count > 0)
        {
            // Use a fresh DbContext for the INSERT — the shared scoped one was already used for reads.
            await using var writeDb = await dbFactory.CreateDbContextAsync();
            writeDb.Appointments.AddRange(toInsert);
            await writeDb.SaveChangesAsync();
        }

        logger.LogInformation(
            "Outlook import for company {CompanyId}: {Imported} imported, {Skipped} skipped",
            companyId, toInsert.Count, events.Count - toInsert.Count);

        return (toInsert.Count, events.Count - toInsert.Count);
    }

    [HttpDelete("outlook/disconnect")]
    public async Task<IActionResult> OutlookDisconnect()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var rows = await Db.CompanyOutlookTokens
            .Where(t => t.CompanyId == companyId)
            .ExecuteDeleteAsync();

        return rows == 0
            ? NotFound(new { error = "Outlook is not connected for this company" })
            : NoContent();
    }

    // ── WhatsApp ────────────────────────────────────────────────────────────────

    [HttpGet("whatsapp/status")]
    public async Task<IActionResult> WhatsAppStatus()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var row = await Db.AssistantSettings
            .Where(s => s.CompanyId == companyId)
            .Select(s => new { s.WhatsAppRequested, s.WhatsAppActive, s.WhatsAppPhoneNumber })
            .FirstOrDefaultAsync();

        if (row is null)
            return Ok(new { connected = false, requested = false, active = false, phoneNumber = (string?)null });

        return Ok(new
        {
            connected   = row.WhatsAppActive,
            requested   = row.WhatsAppRequested,
            active      = row.WhatsAppActive,
            phoneNumber = row.WhatsAppPhoneNumber,
        });
    }

    [HttpPut("whatsapp/connect")]
    public async Task<IActionResult> WhatsAppConnect([FromBody] WhatsAppConnectRequest request)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return BadRequest(new { error = "Vul een geldig telefoonnummer in." });

        var settings = await Db.AssistantSettings.FindAsync(companyId);
        if (settings == null)
        {
            settings = new Domain.Models.AssistantSettings { CompanyId = companyId };
            Db.AssistantSettings.Add(settings);
        }

        settings.WhatsAppPhoneNumber = request.PhoneNumber.Trim();
        settings.WhatsAppActive      = true;
        settings.WhatsAppRequested   = true;
        settings.UpdatedAt           = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync();
        await LogAuditAsync(companyId, $"WhatsApp nummer gekoppeld: {settings.WhatsAppPhoneNumber}");

        return Ok(new { connected = true, phoneNumber = settings.WhatsAppPhoneNumber });
    }

    [HttpPost("whatsapp/request")]
    public async Task<IActionResult> WhatsAppRequest()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var aiPhoneNumber = await Db.PhoneNumbers
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .Select(p => p.AiPhoneNumber)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(aiPhoneNumber))
            return BadRequest(new { error = "Koop eerst een telefoonnummer voordat je WhatsApp activeert." });

        var settings = await Db.AssistantSettings.FindAsync(companyId);
        if (settings == null)
        {
            settings = new Domain.Models.AssistantSettings { CompanyId = companyId };
            Db.AssistantSettings.Add(settings);
        }

        if (settings.WhatsAppActive)
            return BadRequest(new { error = "WhatsApp is al actief voor dit bedrijf." });

        settings.WhatsAppRequested = true;
        settings.WhatsAppRequestedAt = DateTimeOffset.UtcNow;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync();
        await LogAuditAsync(companyId, "WhatsApp Business aangevraagd");

        // Notify admin
        var company = await Db.Companies.FindAsync(companyId);
        var companyName = company?.CompanyName ?? $"Bedrijf {companyId}";
        _ = Task.Run(async () =>
        {
            try
            {
                var adminEmail = configuration["AdminEmail"] ?? "quintenwit41@gmail.com";
                var (subject, html) = emailTemplates.WhatsAppRequestedAdmin(companyName, companyId, aiPhoneNumber);
                await emailSender.SendNowAsync(null, adminEmail, "VoxFlow Admin", "whatsapp_requested_admin", subject, html);
            }
            catch { }
        });

        return Ok(new { requested = true });
    }

    [HttpDelete("whatsapp/disconnect")]
    public async Task<IActionResult> WhatsAppDisconnect()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var settings = await Db.AssistantSettings.FindAsync(companyId);
        if (settings == null)
            return NotFound(new { error = "No assistant settings found for this company." });

        settings.WhatsAppPhoneNumber = null;
        settings.WhatsAppActive = false;
        settings.WhatsAppRequested = false;
        settings.WhatsAppRequestedAt = null;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync();
        await LogAuditAsync(companyId, "WhatsApp ontkoppeld");

        return NoContent();
    }

    // ── Notify (wachtlijst) ──────────────────────────────────────────────────

    [HttpGet("notify")]
    public async Task<IActionResult> GetNotifyList()
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        var keys = await Db.IntegrationNotifyRequests
            .Where(r => r.CompanyId == companyId && !r.Resolved)
            .Select(r => r.IntegrationKey)
            .ToListAsync();

        return Ok(keys);
    }

    [HttpPost("notify")]
    public async Task<IActionResult> Subscribe([FromBody] NotifySubscribeRequest body)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        // Idempotent — skip if already subscribed
        var exists = await Db.IntegrationNotifyRequests
            .AnyAsync(r => r.CompanyId == companyId && r.IntegrationKey == body.IntegrationKey && !r.Resolved);

        if (!exists)
        {
            Db.IntegrationNotifyRequests.Add(new AiCallAssistent.Domain.Models.IntegrationNotifyRequest
            {
                CompanyId       = companyId,
                IntegrationKey  = body.IntegrationKey,
                IntegrationName = body.IntegrationName,
                CreatedAt       = DateTimeOffset.UtcNow,
            });
            await Db.SaveChangesAsync();
        }

        // Send confirmation to company owner + admin notification (fire-and-forget)
        var owner = await Db.Employees
            .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.IsOwner && e.IsActive && e.Email != null);
        var company = await Db.Companies.FindAsync(companyId);

        _ = Task.Run(async () =>
        {
            try
            {
                if (owner?.Email != null)
                {
                    var (s, h) = emailTemplates.IntegrationNotifyConfirm(owner.Name, body.IntegrationName);
                    await emailSender.SendNowAsync(companyId, owner.Email, owner.Name,
                        "integration_notify_confirm", s, h);
                }
                var adminEmail = configuration["AdminEmail"] ?? "quintenwit41@gmail.com";
                var (as_, ah) = emailTemplates.IntegrationNotifyAdmin(
                    company?.CompanyName ?? $"Bedrijf {companyId}", companyId, body.IntegrationName);
                await emailSender.SendNowAsync(null, adminEmail, "VoxFlow Admin", "integration_notify_admin", as_, ah);
            }
            catch { }
        });

        return Ok(new { subscribed = true });
    }

    [HttpDelete("notify/{key}")]
    public async Task<IActionResult> Unsubscribe(string key)
    {
        var (companyId, error) = await GetCompanyIdAsync();
        if (error != null) return error;

        await Db.IntegrationNotifyRequests
            .Where(r => r.CompanyId == companyId && r.IntegrationKey == key && !r.Resolved)
            .ExecuteDeleteAsync();

        return NoContent();
    }
}

public record NotifySubscribeRequest(string IntegrationKey, string IntegrationName);
