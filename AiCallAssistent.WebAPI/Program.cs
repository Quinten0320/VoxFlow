using System.Threading.RateLimiting;
using AiCallAssistent.Application.Configuration;
using AiCallAssistent.Infrastructure;
using AiCallAssistent.WebAPI.WebSockets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "AiCallAssistent Web API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Supabase access_token (JWT)"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});

builder.Services.AddMemoryCache();

builder.Services.AddRateLimiter(options =>
{
    // Global: 120 requests per minute per IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                AutoReplenishment = true,
            }));

    // Strict: 5 attempts per 15 minutes — used on admin login
    options.AddFixedWindowLimiter("strict", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(15);
        opt.AutoReplenishment = true;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.Configure<AiCallAssistent.Application.Configuration.AdminSettings>(
    builder.Configuration.GetSection("Admin"));
builder.Services.Configure<AiCallAssistent.Application.Configuration.StripeSettings>(
    builder.Configuration.GetSection("Stripe"));
Stripe.StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"] ?? "";
builder.Services.AddSharedInfrastructure(builder.Configuration);
builder.Services.AddCallPipelineInfrastructure(builder.Configuration);
builder.Services.AddBackgroundServices();
builder.Services.AddEmailInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var configured = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        string[] allowedMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"];

        if (builder.Environment.IsDevelopment() && configured.Length == 0)
        {
            policy
                .SetIsOriginAllowed(origin =>
                {
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                    return uri.Host == "localhost" || uri.Host == "127.0.0.1";
                })
                .AllowAnyHeader()
                .WithMethods(allowedMethods)
                .AllowCredentials();
        }
        else
        {
            policy
                .WithOrigins(configured)
                .AllowAnyHeader()
                .WithMethods(allowedMethods)
                .AllowCredentials();
        }
    });
});

// Supabase Auth — verifies JWTs using Supabase's public JWKS endpoint
var supabaseAuthority = builder.Configuration
    .GetSection("Supabase").Get<SupabaseSettings>()!.Authority;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = supabaseAuthority;
        options.MapInboundClaims = false; // keep "sub", "email" etc. as-is
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidIssuer = supabaseAuthority,
            ValidateAudience = true,
            ValidAudience = "authenticated"
        };
        options.Events = new()
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>()
                    .LogWarning("JWT authentication failed: {Error}", ctx.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "AiCallAssistent Web API");
    });
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
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
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
