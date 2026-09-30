namespace Conflux.Order.Clients.Inventory;

/// <summary>
/// Provides the Order service with access to inventory operations.
/// </summary>
public interface IInventoryClient
{
    /// <summary>
    /// Reserves inventory for an order operation.
    /// </summary>
    /// <param name="inventoryItemId">
    /// The inventory item to reserve.
    /// </param>
    /// <param name="reservationId">
    /// The durable reservation identifier used for idempotency.
    /// </param>
    /// <param name="quantity">
    /// The quantity to reserve.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The inventory reservation result.
    /// </returns>
    Task<InventoryReservationResult> ReserveAsync(
        Guid inventoryItemId,
        Guid reservationId,
        int quantity,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases inventory previously reserved for an order operation.
    /// </summary>
    /// <param name="inventoryItemId">
    /// The inventory item associated with the reservation.
    /// </param>
    /// <param name="reservationId">
    /// The reservation identifier.
    /// </param>
    /// <param name="cancellationToken">
    /// The token used to cancel the operation.
    /// </param>
    /// <returns>
    /// The inventory release result.
    /// </returns>
    Task<InventoryReleaseResult> ReleaseAsync(
        Guid inventoryItemId,
        Guid reservationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Represents the result returned after reserving inventory.
/// </summary>
public sealed record InventoryReservationResult
{
    /// <summary>
    /// Gets the inventory item identifier.
    /// </summary>
    public required Guid InventoryItemId { get; init; }

    /// <summary>
    /// Gets the reservation identifier.
    /// </summary>
    public required Guid ReservationId { get; init; }

    /// <summary>
    /// Gets the stock keeping unit.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the quantity reserved.
    /// </summary>
    public int ReservedQuantity { get; init; }

    /// <summary>
    /// Gets the quantity remaining available.
    /// </summary>
    public int AvailableQuantity { get; init; }

    /// <summary>
    /// Gets the total quantity currently reserved.
    /// </summary>
    public int TotalReservedQuantity { get; init; }

    /// <summary>
    /// Gets a value indicating whether the reservation already existed.
    /// </summary>
    public bool AlreadyReserved { get; init; }
}

/// <summary>
/// Represents the result returned after releasing inventory.
/// </summary>
public sealed record InventoryReleaseResult
{
    /// <summary>
    /// Gets the inventory item identifier.
    /// </summary>
    public required Guid InventoryItemId { get; init; }

    /// <summary>
    /// Gets the reservation identifier.
    /// </summary>
    public required Guid ReservationId { get; init; }

    /// <summary>
    /// Gets the stock keeping unit.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the quantity released.
    /// </summary>
    public int ReleasedQuantity { get; init; }

    /// <summary>
    /// Gets the quantity currently available.
    /// </summary>
    public int AvailableQuantity { get; init; }

    /// <summary>
    /// Gets the total quantity currently reserved.
    /// </summary>
    public int TotalReservedQuantity { get; init; }

    /// <summary>
    /// Gets a value indicating whether the reservation had already been released.
    /// </summary>
    public bool AlreadyReleased { get; init; }
}