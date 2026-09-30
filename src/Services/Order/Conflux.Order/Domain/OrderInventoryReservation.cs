namespace Conflux.Order.Domain;

/// <summary>
/// Represents the durable correlation between an order item and an
/// inventory reservation.
/// </summary>
public sealed class OrderInventoryReservation {
    private OrderInventoryReservation() {
    }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="OrderInventoryReservation"/> class.
    /// </summary>
    /// <param name="id">
    /// The unique identifier of this reservation record.
    /// </param>
    /// <param name="orderItemId">
    /// The order item associated with the reservation.
    /// </param>
    /// <param name="inventoryItemId">
    /// The inventory item reserved by the Inventory service.
    /// </param>
    /// <param name="reservationId">
    /// The idempotency identifier used with the Inventory service.
    /// </param>
    /// <param name="quantity">
    /// The quantity reserved.
    /// </param>
    public OrderInventoryReservation(
        Guid id,
        Guid orderItemId,
        Guid inventoryItemId,
        Guid reservationId,
        int quantity) {
        if (orderItemId == Guid.Empty) {
            throw new ArgumentException(
                "Order item ID cannot be empty.",
                nameof(orderItemId));
        }

        if (inventoryItemId == Guid.Empty) {
            throw new ArgumentException(
                "Inventory item ID cannot be empty.",
                nameof(inventoryItemId));
        }

        if (reservationId == Guid.Empty) {
            throw new ArgumentException(
                "Reservation ID cannot be empty.",
                nameof(reservationId));
        }

        if (quantity <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Reservation quantity must be greater than zero.");
        }

        Id = id;
        OrderItemId = orderItemId;
        InventoryItemId = inventoryItemId;
        ReservationId = reservationId;
        Quantity = quantity;
        Status = OrderInventoryReservationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the unique identifier of this reservation record.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the identifier of the order item associated with this reservation.
    /// </summary>
    public Guid OrderItemId { get; private set; }

    /// <summary>
    /// Gets the identifier of the inventory item being reserved.
    /// </summary>
    public Guid InventoryItemId { get; private set; }

    /// <summary>
    /// Gets the idempotency identifier used for the Inventory reservation.
    /// </summary>
    public Guid ReservationId { get; private set; }

    /// <summary>
    /// Gets the quantity associated with the inventory reservation.
    /// </summary>
    public int Quantity { get; private set; }

    /// <summary>
    /// Gets the current lifecycle state of the reservation.
    /// </summary>
    public OrderInventoryReservationStatus Status { get; private set; }

    /// <summary>
    /// Gets the time at which this reservation record was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the time at which the reservation was released, if applicable.
    /// </summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>
    /// Marks the reservation as successfully created in Inventory.
    /// </summary>
    public void MarkReserved() {
        if (Status != OrderInventoryReservationStatus.Pending) {
            throw new InvalidOperationException(
                $"A reservation in {Status} state cannot be marked as reserved.");
        }

        Status = OrderInventoryReservationStatus.Reserved;
    }

    /// <summary>
    /// Marks the reservation as released.
    /// </summary>
    public void MarkReleased() {
        if (Status == OrderInventoryReservationStatus.Released) {
            return;
        }

        if (Status != OrderInventoryReservationStatus.Reserved) {
            throw new InvalidOperationException(
                $"A reservation in {Status} state cannot be released.");
        }

        Status = OrderInventoryReservationStatus.Released;
        ReleasedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the reservation as failed.
    /// </summary>
    public void MarkFailed() {
        if (Status != OrderInventoryReservationStatus.Pending) {
            throw new InvalidOperationException(
                $"A reservation in {Status} state cannot be marked as failed.");
        }

        Status = OrderInventoryReservationStatus.Failed;
    }
}