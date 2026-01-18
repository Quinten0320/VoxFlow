using AiCallAssistent.Infrastructure.ElevenLabs.Models;
using AiCallAssistent.Infrastructure.ElevenLabs.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// OpenAPI / Swagger
builder.Services.AddOpenApi();

// ElevenLabs TTS
builder.Services.Configure<ElevenLabsOptions>(
    builder.Configuration.GetSection("ElevenLabs"));
builder.Services.AddHttpClient<ElevenLabsTextToSpeechService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ElevenLabsOptions>>().Value;
    client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    client.BaseAddress = new Uri("https://api.elevenlabs.io/v1/");
});

var app = builder.Build();

// Forwarded headers (MOET als je achter ngrok / proxy draait)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto
});

app.UseStaticFiles();

// Alleen in productie HTTPS afdwingen
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

// Swagger alleen in Development
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
