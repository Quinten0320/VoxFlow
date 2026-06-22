using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.WebAPI.Controllers;

[Route("api/integrations/outlook")]
[ApiController]
[AllowAnonymous]
public class OutlookCallbackController(
    AppDbContext db,
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<OutlookSettings> outlookSettings,
    IHttpClientFactory httpClientFactory,
    IOutlookCalendarService outlookCalendar,
    ILogger<OutlookCallbackController> logger) : ControllerBase
{
    private const string TokenEndpoint =
        "https://login.microsoftonline.com/common/oauth2/v2.0/token";

    private static readonly string[] Scopes =
        ["Calendars.ReadWrite", "offline_access", "User.Read"];

    /// <summary>
    /// Receives the authorization code from Microsoft, exchanges it for tokens,
    /// stores them in the DB, then redirects the browser back to the dashboard.
    /// Must be anonymous — no JWT is available during a browser redirect.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
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

        if (code == null || !short.TryParse(state, out var companyId))
            return BadRequest(new { error = "Invalid callback parameters." });

        var s = outlookSettings.Value;
        var client = httpClientFactory.CreateClient();

        var tokenResponse = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["client_id"]     = s.ClientId,
                ["client_secret"] = s.ClientSecret,
                ["code"]          = code,
                ["redirect_uri"]  = s.RedirectUri,
                ["scope"]         = string.Join(" ", Scopes)
            }));

        if (!tokenResponse.IsSuccessStatusCode)
        {
            var body = await tokenResponse.Content.ReadAsStringAsync();
            logger.LogError("Outlook token exchange failed for company {CompanyId}: {Body}", companyId, body);
            return StatusCode(502, new { error = "Token exchange with Microsoft failed." });
        }

        var json = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken  = json.GetProperty("access_token").GetString()!;
        var refreshToken = json.GetProperty("refresh_token").GetString()!;
        var expiresIn    = json.GetProperty("expires_in").GetInt32();

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

        var existing = await db.CompanyOutlookTokens.FindAsync(companyId);
        if (existing == null)
        {
            db.CompanyOutlookTokens.Add(new CompanyOutlookToken
            {
                CompanyId    = companyId,
                AccessToken  = accessToken,
                RefreshToken = refreshToken,
                TokenExpiry  = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                OutlookEmail = email,
                CreatedAt    = DateTimeOffset.UtcNow,
                UpdatedAt    = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.AccessToken  = accessToken;
            existing.RefreshToken = refreshToken;
            existing.TokenExpiry  = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            existing.OutlookEmail = email;
            existing.UpdatedAt    = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Outlook connected for company {CompanyId} ({Email})", companyId, email);

        try
        {
            var employeeId = await db.Employees
                .Where(e => e.CompanyId == companyId && e.IsActive)
                .Select(e => (long?)e.EmployeeId)
                .FirstOrDefaultAsync();

            if (employeeId != null)
            {
                var events = await outlookCalendar.GetEventsAsync(
                    companyId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(365));

                if (events.Count > 0)
                {
                    var existingStarts = await db.Appointments
                        .Where(a => a.CompanyId == companyId && a.StartTime >= DateTimeOffset.UtcNow)
                        .Select(a => a.StartTime)
                        .ToListAsync();
                    var existingSet = existingStarts.ToHashSet();

                    var toInsert = events
                        .Where(ev => !existingSet.Contains(ev.Start))
                        .Select(ev => new Appointment
                        {
                            CompanyId  = companyId,
                            EmployeeId = employeeId.Value,
                            Type       = "imported",
                            Description = ev.Subject,
                            StartTime  = ev.Start,
                            EndTime    = ev.End,
                            CreatedAt  = DateTimeOffset.UtcNow
                        })
                        .ToList();

                    if (toInsert.Count > 0)
                    {
                        await using var writeDb = await dbFactory.CreateDbContextAsync();
                        writeDb.Appointments.AddRange(toInsert);
                        await writeDb.SaveChangesAsync();
                    }

                    logger.LogInformation(
                        "Auto-import on connect for company {CompanyId}: {Count} appointments imported",
                        companyId, toInsert.Count);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto-import failed for company {CompanyId}", companyId);
        }

        var dashboardUrl = string.IsNullOrWhiteSpace(s.DashboardUrl)
            ? "/swagger"
            : s.DashboardUrl + "?outlook=connected";

        return Redirect(dashboardUrl);
    }
}
