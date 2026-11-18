namespace Inventory.Api.Domain;

public enum ReservationStatus
{
    Reserved,
    Rejected
}

// One row per order Inventory has handled. OrderId is the primary key, so the same order
// can never be reserved twice (idempotent receiver).
public class Reservation
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Reservation() {}

    private Reservation(int orderId, ReservationStatus status, string? reason)
    {
        OrderId = orderId;
        Status = status;
        Reason = reason;
    }

    public int OrderId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static Reservation Reserved(int orderId) => new(orderId, ReservationStatus.Reserved, null);

    public static Reservation Rejected(int orderId, string reason) => new(orderId, ReservationStatus.Rejected, reason);
}
