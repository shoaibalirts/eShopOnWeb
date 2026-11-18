using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Inventory.Api.Messaging;

// W3C trace context over RabbitMQ. HTTP already carries the "traceparent" header between the gateway
// and the monolith; messages don't, so I copy it into the message headers myself. Then the logs of one
// order share one TraceId in Seq, across the gateway, the monolith and Inventory.
public static class TraceContext
{
    private const string Header = "traceparent";

    // Continue the sender's trace while I handle the message. Serilog reads the TraceId from Activity.Current.
    public static Activity StartConsumer(BasicDeliverEventArgs delivery)
    {
        var activity = new Activity($"consume {delivery.RoutingKey}");
        if (delivery.BasicProperties.Headers?.TryGetValue(Header, out var value) == true && value is byte[] bytes)
        {
            activity.SetParentId(Encoding.UTF8.GetString(bytes));
        }
        return activity.Start();
    }

    public static void Inject(BasicProperties properties)
    {
        if (Activity.Current?.Id is { } traceParent)
        {
            properties.Headers ??= new Dictionary<string, object?>();
            properties.Headers[Header] = traceParent;
        }
    }
}
