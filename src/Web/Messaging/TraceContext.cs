using System.Diagnostics;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.eShopWeb.Web.Messaging;

// W3C trace context over RabbitMQ (the same as in Inventory). HTTP carries "traceparent" by itself;
// messages don't, so I copy it into the message headers. Then one order has one TraceId in Seq.
public static class TraceContext
{
    private const string Header = "traceparent";

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
