using AiCallAssistent.API.WebSockets;
using AiCallAssistent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
