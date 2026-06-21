using System.Threading.RateLimiting;
using AiCallAssistent.API.WebSockets;
using AiCallAssistent.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

builder.Services.AddRateLimiter(options =>
{
    // Global: 60 requests per minute per IP (Twilio webhooks are low-volume)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSharedInfrastructure(builder.Configuration);
builder.Services.AddCallPipelineInfrastructure(builder.Configuration);
builder.Services.AddBackgroundServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/ws/twilio"))
    {
        await TwilioStreamEndpoint.HandleAsync(context);
        return;
    }
    await next(context);
});

app.MapControllers();
app.Run();
