using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Notification.Service.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;
using Shared.Contracts;
using Shared.Contracts.Events;

namespace Notification.Service.Messaging;

public sealed class DocumentEventConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentEventConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;
    private IModel? _channel;

    public bool IsConnected => _connection is { IsOpen: true } && _channel is { IsOpen: true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DocumentEventConsumer starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!IsConnected)
                {
                    ConnectAndStartConsuming(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Unable to connect to RabbitMQ at {Host}:{Port}. Retrying in 3 seconds...",
                    _options.Host, _options.Port);
                CleanupConnection();
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }

        CleanupConnection();
    }

    private void ConnectAndStartConsuming(CancellationToken stoppingToken)
    {
        CleanupConnection();

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        _connection = factory.CreateConnection("notification-service-consumer");
        _channel = _connection.CreateModel();

        // Ensure DLX and DLQ
        _channel.ExchangeDeclare(
            exchange: MessagingConstants.DeadLetterExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: MessagingConstants.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);

        _channel.QueueBind(
            queue: MessagingConstants.DeadLetterQueueName,
            exchange: MessagingConstants.DeadLetterExchangeName,
            routingKey: string.Empty);

        // Ensure main exchange & queue
        _channel.ExchangeDeclare(
            exchange: MessagingConstants.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        var queueArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = MessagingConstants.DeadLetterExchangeName
        };

        _channel.QueueDeclare(
            queue: MessagingConstants.NotificationQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArgs);

        _channel.QueueBind(
            queue: MessagingConstants.NotificationQueueName,
            exchange: MessagingConstants.ExchangeName,
            routingKey: MessagingConstants.RoutingKeyWildcard);

        _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, ea) => await HandleDeliveryAsync(ea, stoppingToken);

        _channel.BasicConsume(
            queue: MessagingConstants.NotificationQueueName,
            autoAck: false,
            consumer: consumer);

        logger.LogInformation(
            "Connected to RabbitMQ and consuming queue {Queue} (DLQ: {Dlq})",
            MessagingConstants.NotificationQueueName, MessagingConstants.DeadLetterQueueName);
    }

    private async Task HandleDeliveryAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var correlationId = ExtractCorrelationId(ea.BasicProperties);
        using var _ = LogContext.PushProperty("CorrelationId", correlationId);

        DocumentStateChangedEvent? evt;
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            evt = JsonSerializer.Deserialize<DocumentStateChangedEvent>(json, JsonOptions);
            if (evt is null)
            {
                throw new JsonException("Deserialized event payload is null.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Malformed message (DeliveryTag={DeliveryTag}). Routing directly to DLQ.", ea.DeliveryTag);
            _channel?.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                await dispatcher.HandleEventAsync(evt, ct);

                _channel?.BasicAck(ea.DeliveryTag, multiple: false);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Error processing Event {EventId} on attempt {Attempt}/{MaxAttempts}.",
                    evt.EventId, attempt, maxAttempts);

                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), ct);
                }
                else
                {
                    logger.LogError(
                        "Exhausted {MaxAttempts} attempts for Event {EventId}. Sending message to DLQ ({Dlq}).",
                        maxAttempts, evt.EventId, MessagingConstants.DeadLetterQueueName);
                    _channel?.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            }
        }
    }

    private static string ExtractCorrelationId(IBasicProperties? props)
    {
        if (!string.IsNullOrWhiteSpace(props?.CorrelationId))
            return props.CorrelationId;

        if (props?.Headers is not null &&
            props.Headers.TryGetValue(MessagingConstants.CorrelationIdHeader, out var raw))
        {
            if (raw is byte[] bytes)
                return Encoding.UTF8.GetString(bytes);
            if (raw is string str && !string.IsNullOrWhiteSpace(str))
                return str;
        }

        return Guid.NewGuid().ToString("N");
    }

    private void CleanupConnection()
    {
        try { _channel?.Dispose(); } catch { }
        try { _connection?.Dispose(); } catch { }
        _channel = null;
        _connection = null;
    }

    public override void Dispose()
    {
        CleanupConnection();
        base.Dispose();
    }
}
