using System.Text.Json;
using Document.Api.Data;
using Document.Api.Endpoints;
using Document.Api.HealthChecks;
using Document.Api.Messaging;
using Document.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using ILogger = Microsoft.Extensions.Logging.ILogger;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((_, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Document.Api")
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var connectionString = builder.Configuration.GetConnectionString("DocumentDb")
    ?? throw new InvalidOperationException("ConnectionStrings:DocumentDb is required.");

builder.Services.AddDbContext<DocumentDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxPublisherWorker>();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgresql")
    .AddCheck<RabbitMqHealthCheck>("rabbitmq");

var app = builder.Build();

app.UseCors();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

await EnsureDatabaseCreatedAsync(app.Services, app.Logger);

app.MapGet("/", () => Results.Ok(new
{
    service = "Document.Api",
    status = "Running",
    timestampUtc = DateTime.UtcNow
}));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            service = "Document.Api",
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 2)
            })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

app.MapGet("/outbox/stats", async (DocumentDbContext db, CancellationToken ct) =>
{
    var pending = await db.OutboxMessages.CountAsync(m => m.ProcessedAtUtc == null, ct);
    var processed = await db.OutboxMessages.CountAsync(m => m.ProcessedAtUtc != null, ct);
    return Results.Ok(new { pending, processed });
}).WithTags("Observability");

var documentsGroup = app.MapDocumentCrudEndpoints();
documentsGroup.MapDocumentWorkflowEndpoints();

app.Run();

static async Task EnsureDatabaseCreatedAsync(IServiceProvider services, ILogger logger)
{
    for (var attempt = 1; attempt <= 15; attempt++)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Document database initialized successfully.");
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
