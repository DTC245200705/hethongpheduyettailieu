using Microsoft.EntityFrameworkCore;
using Notification.Service.Data;
using Notification.Service.Endpoints;
using Notification.Service.Hubs;
using Notification.Service.Messaging;
using Notification.Service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var connectionString = builder.Configuration.GetConnectionString("NotificationDb")
    ?? throw new InvalidOperationException("ConnectionStrings:NotificationDb is required.");

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();
builder.Services.AddScoped<NotificationDispatcher>();
builder.Services.AddSingleton<DocumentEventConsumer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DocumentEventConsumer>());

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

await EnsureDatabaseCreatedAsync(app.Services, app.Logger);

app.MapGet("/", () => Results.Ok(new
{
    service = "Notification.Service",
    status = "Running",
    timestampUtc = DateTime.UtcNow
}));

app.MapNotificationEndpoints();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

static async Task EnsureDatabaseCreatedAsync(IServiceProvider services, ILogger logger)
{
    for (var attempt = 1; attempt <= 15; attempt++)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Notification database initialized successfully.");
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Waiting for PostgreSQL (attempt {Attempt}/15)...", attempt);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
    throw new InvalidOperationException("Could not initialize PostgreSQL database after 15 attempts.");
}
