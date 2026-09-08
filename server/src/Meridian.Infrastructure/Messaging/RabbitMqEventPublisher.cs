using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Meridian.Infrastructure.Messaging;

public interface IEventPublisher
{
    bool TryPublish(string eventType, string payload);
}

public sealed class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqEventPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool TryPublish(string eventType, string payload)
    {
        lock (_sync)
        {
            try
            {
                var channel = EnsureChannel();
                var properties = channel.CreateBasicProperties();
                properties.ContentType = "application/json";
                properties.DeliveryMode = 2;

                channel.BasicPublish(
                    exchange: _options.Exchange,
                    routingKey: ToRoutingKey(eventType),
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(payload));
                return true;
            }
            catch (Exception ex)
            {
                ResetConnection();
                _logger.LogWarning(
                    "RabbitMQ unreachable at {Host}:{Port}; message of type {EventType} will be retried. ({Error})",
                    _options.HostName, _options.Port, eventType, ex.Message);
                return false;
            }
        }
    }

    private IModel EnsureChannel()
    {
        if (_channel is { IsOpen: true })
            return _channel;

        ResetConnection();

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
        };

        _connection = factory.CreateConnection("meridian-outbox");
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true);
        return _channel;
    }

    private void ResetConnection()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        _channel = null;
        _connection = null;
    }

    private static string ToRoutingKey(string eventType)
    {
        var builder = new StringBuilder(eventType.Length + 4);
        for (var i = 0; i < eventType.Length; i++)
        {
            var c = eventType[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    builder.Append('.');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            ResetConnection();
        }
    }
}
