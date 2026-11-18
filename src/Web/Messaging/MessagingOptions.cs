namespace Microsoft.eShopWeb.Web.Messaging;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}

// The Strangler rollback switch. false = the shop works as before the migration: no messages, no stock check.
public class StockCheckOptions
{
    public const string SectionName = "StockCheck";

    public bool Enabled { get; set; }
}
