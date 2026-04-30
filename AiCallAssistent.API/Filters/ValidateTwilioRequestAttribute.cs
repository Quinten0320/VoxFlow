using System.Security.Cryptography;
using System.Text;
using AiCallAssistent.Application.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AiCallAssistent.API.Filters;

/// <summary>
/// Validates the X-Twilio-Signature header on incoming webhook requests.
/// Skipped in Development so endpoints can be called directly (e.g. from Swagger).
/// Returns 403 on a missing or invalid signature.
/// See: https://www.twilio.com/docs/usage/webhooks/webhooks-security
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ValidateTwilioRequestAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var env = context.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();

        if (env.IsDevelopment())
        {
            await next();
            return;
        }

        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILogger<ValidateTwilioRequestAttribute>>();

        var signature = context.HttpContext.Request.Headers["X-Twilio-Signature"].ToString();

        if (string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("Twilio request rejected: missing X-Twilio-Signature. Path: {Path}",
                context.HttpContext.Request.Path);
            context.Result = new StatusCodeResult(403);
            return;
        }

        var settings = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<TwilioSettings>>().Value;

        var request = context.HttpContext.Request;
        var url = $"{settings.BaseUrl}{request.Path}{request.QueryString}";

        var paramsSuffix = string.Empty;
        if (HttpMethods.IsPost(request.Method) && request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            paramsSuffix = string.Concat(
                form.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => kv.Key + kv.Value));
        }

        var expected = ComputeSignature(settings.AuthToken, url + paramsSuffix);

        // Constant-time comparison prevents timing attacks.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature)))
        {
            logger.LogWarning("Twilio request rejected: invalid signature for {Url}", url);
            context.Result = new StatusCodeResult(403);
            return;
        }

        await next();
    }

    private static string ComputeSignature(string authToken, string data)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }
}
