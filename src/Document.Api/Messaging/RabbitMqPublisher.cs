using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shared.Contracts;

namespace Document.Api.Messaging;

public sealed class RabbitMqPublisher : IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly object _syncLock = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool CanConnect()
    {
        try
        {
            lock (_syncLock)
            {
                EnsureConnectedLocked();
                return _connection is { IsOpen: true } && _channel is { IsOpen: true };
            }
        }
        catch
        {
            return false;
        }
    }

    public void Publish(string routingKey, string payloadJson, Guid eventId, string correlationId)
    {
        lock (_syncLock)
        {
            EnsureConnectedLocked();

            var body = Encoding.UTF8.GetBytes(payloadJson);
            var properties = _channel!.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = eventId.ToString();
            properties.CorrelationId = correlationId;
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            properties.Headers = new Dictionary<string, object>
            {
                [MessagingConstants.CorrelationIdHeader] = correlationId
            };

            _channel.BasicPublish(
                exchange: MessagingConstants.ExchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body);
        }
    }

    private void EnsureConnectedLocked()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            return;

        DisposeConnectionLocked();

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        _connection = factory.CreateConnection("document-api-publisher");
        _channel = _connection.CreateModel();

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

        _logger.LogInformation(
            "Connected to RabbitMQ at {Host}:{Port} and ensured topology {Exchange} -> {Queue}",
            _options.Host, _options.Port, MessagingConstants.ExchangeName, MessagingConstants.NotificationQueueName);
    }

    private void DisposeConnectionLocked()
    {
        try { _channel?.Dispose(); } catch { }
        try { _connection?.Dispose(); } catch { }
        _channel = null;
        _connection = null;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            DisposeConnectionLocked();
        }
    }
}
