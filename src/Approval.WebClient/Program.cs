using System.Text;
using System.Text.Json;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((_, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Approval.WebClient")
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

var docApiUrl = builder.Configuration["Services:DocumentApiUrl"] ?? "http://localhost:5001";
var notiSvcUrl = builder.Configuration["Services:NotificationServiceUrl"] ?? "http://localhost:5002";

builder.Services.AddHttpClient("DocumentApi", c =>
{
    c.BaseAddress = new Uri(docApiUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient("NotificationService", c =>
{
    c.BaseAddress = new Uri(notiSvcUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new
{
    service = "Approval.WebClient",
    status = "Healthy",
    timestampUtc = DateTime.UtcNow
}));

app.MapGet("/api/client-config", (IConfiguration config) => Results.Ok(new
{
    notificationHubUrl = config["Services:PublicNotificationHubUrl"] ?? "http://localhost:5002/hubs/notifications",
    documentApiDocsUrl = (config["Services:PublicDocumentApiUrl"] ?? "http://localhost:5001").TrimEnd('/') + "/swagger"
}));

// Proxy to Document.Api over Docker internal network
app.MapGet("/api/documents", (HttpContext ctx, IHttpClientFactory factory) =>
    ProxyAsync(ctx, factory, "DocumentApi", HttpMethod.Get, "documents" + ctx.Request.QueryString));

app.MapPost("/api/documents", (HttpContext ctx, IHttpClientFactory factory) =>
    ProxyAsync(ctx, factory, "DocumentApi", HttpMethod.Post, "documents"));

app.MapGet("/api/documents/{id:guid}", (Guid id, HttpContext ctx, IHttpClientFactory factory) =>
    ProxyAsync(ctx, factory, "DocumentApi", HttpMethod.Get, $"documents/{id}"));

app.MapPost("/api/documents/{id:guid}/{action}", (Guid id, string action, HttpContext ctx, IHttpClientFactory factory) =>
    ProxyAsync(ctx, factory, "DocumentApi", HttpMethod.Post, $"documents/{id}/{action}"));

// Proxy to Notification.Service over Docker internal network
app.MapGet("/api/notifications", (HttpContext ctx, IHttpClientFactory factory) =>
    ProxyAsync(ctx, factory, "NotificationService", HttpMethod.Get, "notifications" + ctx.Request.QueryString));

app.MapPost("/api/notifications/{id:guid}/read", (Guid id, HttpContext ctx, IHttpClientFactory factory) =>
    ProxyAsync(ctx, factory, "NotificationService", HttpMethod.Post, $"notifications/{id}/read"));

app.MapPost("/api/notifications/read-all", (HttpContext ctx, IHttpClientFactory factory) =>
    ProxyAsync(ctx, factory, "NotificationService", HttpMethod.Post, "notifications/read-all" + ctx.Request.QueryString));

// Aggregated live health status for live demo & monitoring
app.MapGet("/api/system/status", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var docHealth = await FetchJsonSafeAsync(factory.CreateClient("DocumentApi"), "health", ct);
    var outboxStats = await FetchJsonSafeAsync(factory.CreateClient("DocumentApi"), "outbox/stats", ct);
    var notiHealth = await FetchJsonSafeAsync(factory.CreateClient("NotificationService"), "health", ct);

    return Results.Ok(new
    {
        checkedAtUtc = DateTime.UtcNow,
        documentApi = docHealth,
        outbox = outboxStats,
        notificationService = notiHealth
    });
});

app.Run();

static async Task ProxyAsync(
    HttpContext ctx,
    IHttpClientFactory factory,
    string clientName,
    HttpMethod method,
    string targetPath)
{
    var correlationId = ctx.Request.Headers[MessagingConstants.CorrelationIdHeader].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");

    var client = factory.CreateClient(clientName);
    using var reqMessage = new HttpRequestMessage(method, targetPath);
    reqMessage.Headers.TryAddWithoutValidation(MessagingConstants.CorrelationIdHeader, correlationId);

    if (method == HttpMethod.Post || method == HttpMethod.Put)
    {
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
        var bodyText = await reader.ReadToEndAsync();
        if (!string.IsNullOrEmpty(bodyText))
        {
            reqMessage.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");
        }
    }

    try
    {
        using var resp = await client.SendAsync(reqMessage, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)resp.StatusCode;
        ctx.Response.Headers[MessagingConstants.CorrelationIdHeader] = correlationId;
        ctx.Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
        var content = await resp.Content.ReadAsStringAsync(ctx.RequestAborted);
        await ctx.Response.WriteAsync(content, ctx.RequestAborted);
    }
    catch (Exception ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = $"Service '{clientName}' tạm thời không khả dụng.",
            detail = ex.Message,
            correlationId
        }));
    }
}

static async Task<object> FetchJsonSafeAsync(HttpClient client, string path, CancellationToken ct)
{
    try
    {
        using var resp = await client.GetAsync(path, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        return new { reachable = true, httpStatus = (int)resp.StatusCode, data = parsed };
    }
    catch (Exception ex)
    {
        return new { reachable = false, error = ex.Message };
    }
}
