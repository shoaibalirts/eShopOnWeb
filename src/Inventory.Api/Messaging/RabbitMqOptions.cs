namespace Inventory.Api.Messaging;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}
