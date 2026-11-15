using Document.Api.Data;
using Document.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("DocumentDb")
    ?? throw new InvalidOperationException("ConnectionStrings:DocumentDb is required.");

builder.Services.AddDbContext<DocumentDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

await EnsureDatabaseCreatedAsync(app.Services, app.Logger);

app.MapGet("/", () => Results.Ok(new
{
    service = "Document.Api",
    status = "Running",
    timestampUtc = DateTime.UtcNow
}));

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
