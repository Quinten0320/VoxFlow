using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Application.DTOs;
using AiCallAssistent.Application.Helpers;
using AiCallAssistent.Application.Services;
using AiCallAssistent.Domain.Models;
using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.Infrastructure.Services;

public class OutlookCalendarService(
    HttpClient httpClient,
    AppDbContext db,
    IOptions<OutlookSettings> settings,
    ILogger<OutlookCalendarService> logger) : IOutlookCalendarService
{
    private const string GraphEventsUrl = "https://graph.microsoft.com/v1.0/me/events";
    private const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";

    public async Task CreateEventAsync(short companyId, Appointment appointment, string employeeName,
        string displayName, string? customerName)
    {
        var accessToken = await GetValidAccessTokenAsync(companyId);
        if (accessToken == null) return; // company not connected — skip silently

        var startNl = NlTimeZone.ConvertFromUtc(appointment.StartTime);
        var endNl = NlTimeZone.ConvertFromUtc(appointment.EndTime);

        var subject = $"{displayName} — {startNl.DateTime:HH:mm}–{endNl.DateTime:HH:mm}";

        var descriptionParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(customerName))
            descriptionParts.Add($"Customer: {customerName}");
        if (!string.IsNullOrWhiteSpace(appointment.Description))
            descriptionParts.Add(appointment.Description);
        if (descriptionParts.Count == 0)
            descriptionParts.Add("Appointment booked via AI assistant.");
        var descriptionText = string.Join("\n\n", descriptionParts);

        var body = new
        {
            subject,
            start = new
            {
                dateTime = startNl.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                timeZone = "Europe/Amsterdam"
            },
            end = new
            {
                dateTime = endNl.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                timeZone = "Europe/Amsterdam"
            },
            body = new
            {
                contentType = "Text",
                content = descriptionText
            }
        };

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await httpClient.PostAsJsonAsync(GraphEventsUrl, body);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Microsoft Graph {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }

        logger.LogInformation(
            "Outlook event created for appointment {AppointmentId} (company {CompanyId})",
            appointment.AppointmentId, companyId);
    }

    public async Task<List<OutlookEventDto>> GetEventsAsync(short companyId, DateTimeOffset from, DateTimeOffset to)
    {
        var accessToken = await GetValidAccessTokenAsync(companyId);
        if (accessToken == null) return [];

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        httpClient.DefaultRequestHeaders.Remove("Prefer");
        httpClient.DefaultRequestHeaders.Add("Prefer", "outlook.timezone=\"UTC\"");

        var url = "https://graph.microsoft.com/v1.0/me/calendarView" +
                  $"?startDateTime={from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}" +
                  $"&endDateTime={to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}" +
                  "&$select=subject,start,end,bodyPreview" +
                  "&$top=500";

        var json = await httpClient.GetFromJsonAsync<JsonElement>(url);
        if (!json.TryGetProperty("value", out var valueEl)) return [];

        var events = new List<OutlookEventDto>();
        foreach (var ev in valueEl.EnumerateArray())
        {
            var subject = ev.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "";
            var startStr = ev.GetProperty("start").GetProperty("dateTime").GetString()!;
            var endStr = ev.GetProperty("end").GetProperty("dateTime").GetString()!;
            var bodyPreview = ev.TryGetProperty("bodyPreview", out var bp) ? bp.GetString() : null;

            // Graph returns UTC without offset when Prefer: UTC is set — force Kind=Utc before parsing
            var start = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Parse(startStr), DateTimeKind.Utc));
            var end = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Parse(endStr), DateTimeKind.Utc));

            events.Add(new OutlookEventDto(subject, start, end, bodyPreview));
        }

        logger.LogInformation(
            "Fetched {Count} Outlook events for company {CompanyId}", events.Count, companyId);

        return events;
    }

    /// <summary>Returns null if the company has not connected Outlook.</summary>
    private async Task<string?> GetValidAccessTokenAsync(short companyId)
    {
        var token = await db.CompanyOutlookTokens.FindAsync(companyId);
        if (token == null) return null;

        if (token.TokenExpiry <= DateTimeOffset.UtcNow.AddMinutes(5))
            await RefreshTokenAsync(token);

        return token.AccessToken;
    }

    private async Task RefreshTokenAsync(CompanyOutlookToken token)
    {
        var s = settings.Value;

        var response = await httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = s.ClientId,
                ["client_secret"] = s.ClientSecret,
                ["refresh_token"] = token.RefreshToken,
                ["scope"] = "Calendars.ReadWrite offline_access"
            }));

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Outlook token refresh failed {(int)response.StatusCode}: {error}",
                null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        token.AccessToken = json.GetProperty("access_token").GetString()!;
        token.TokenExpiry = DateTimeOffset.UtcNow.AddSeconds(
            json.GetProperty("expires_in").GetInt32());

        // Microsoft may or may not include a new refresh token
        if (json.TryGetProperty("refresh_token", out var rt))
            token.RefreshToken = rt.GetString()!;

        token.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        logger.LogInformation("Outlook token refreshed for company {CompanyId}", token.CompanyId);
    }
}
