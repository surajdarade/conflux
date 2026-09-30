namespace Conflux.Inventory.Domain;

/// <summary>
/// Represents a durable inventory reservation created for a business operation.
/// </summary>
public sealed class InventoryReservation
{
    private InventoryReservation()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryReservation"/> class.
    /// </summary>
    /// <param name="id">
    /// The unique identifier of this reservation.
    /// </param>
    /// <param name="reservationId">
    /// The idempotency identifier of the business reservation operation.
    /// </param>
    /// <param name="inventoryItemId">
    /// The inventory item associated with the reservation.
    /// </param>
    /// <param name="quantity">
    /// The quantity reserved.
    /// </param>
    public InventoryReservation(
        Guid id,
        Guid reservationId,
        Guid inventoryItemId,
        int quantity)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation ID cannot be empty.",
                nameof(reservationId));
        }

        if (inventoryItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "Inventory item ID cannot be empty.",
                nameof(inventoryItemId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Reservation quantity must be greater than zero.");
        }

        Id = id;
        ReservationId = reservationId;
        InventoryItemId = inventoryItemId;
        Quantity = quantity;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the unique identifier of the reservation record.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the idempotency identifier of the business reservation operation.
    /// </summary>
    public Guid ReservationId { get; private set; }

    /// <summary>
    /// Gets the unique identifier of the associated inventory item.
    /// </summary>
    public Guid InventoryItemId { get; private set; }

    /// <summary>
    /// Gets the quantity reserved by this operation.
    /// </summary>
    public int Quantity { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the reservation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the reservation was released.
    /// </summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this reservation has been released.
    /// </summary>
    public bool IsReleased =>
        ReleasedAt.HasValue;

    /// <summary>
    /// Marks the reservation as released.
    /// </summary>
    public void MarkReleased()
    {
        if (IsReleased)
        {
            return;
        }

        ReleasedAt = DateTimeOffset.UtcNow;
    }
}