namespace Conflux.Order.Clients.Inventory;

/// <summary>
/// Provides the Order service with access to inventory operations.
/// </summary>
public interface IInventoryClient {
    /// <summary>
    /// Resolves an inventory item by its stock keeping unit.
    /// </summary>
    /// <param name="sku">
    /// The stock keeping unit to resolve.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token for the operation.
    /// </param>
    /// <returns>
    /// The inventory item identified by the SKU.
    /// </returns>
    Task<InventoryItemLookupResult> GetBySkuAsync(
        string sku,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reserves inventory for a business reservation.
    /// </summary>
    /// <param name="inventoryItemId">
    /// The inventory item identifier.
    /// </param>
    /// <param name="reservationId">
    /// The business reservation identifier.
    /// </param>
    /// <param name="quantity">
    /// The quantity to reserve.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token for the operation.
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
    /// Releases an existing inventory reservation.
    /// </summary>
    /// <param name="inventoryItemId">
    /// The inventory item identifier.
    /// </param>
    /// <param name="reservationId">
    /// The business reservation identifier.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token for the operation.
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
/// Represents inventory information resolved from a SKU.
/// </summary>
public sealed record InventoryItemLookupResult {
    /// <summary>
    /// Gets the inventory item identifier.
    /// </summary>
    public required Guid InventoryItemId { get; init; }

    /// <summary>
    /// Gets the stock keeping unit.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>
    /// Gets the currently available quantity.
    /// </summary>
    public int AvailableQuantity { get; init; }

    /// <summary>
    /// Gets the currently reserved quantity.
    /// </summary>
    public int ReservedQuantity { get; init; }
}

/// <summary>
/// Represents the result of an inventory reservation.
/// </summary>
public sealed record InventoryReservationResult {
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
    /// Gets the quantity reserved by this operation.
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
/// Represents the result of releasing an inventory reservation.
/// </summary>
public sealed record InventoryReleaseResult {
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
    /// Gets the quantity released by this operation.
    /// </summary>
    public int ReleasedQuantity { get; init; }

    /// <summary>
    /// Gets the quantity remaining available.
    /// </summary>
    public int AvailableQuantity { get; init; }

    /// <summary>
    /// Gets the total quantity currently reserved.
    /// </summary>
    public int TotalReservedQuantity { get; init; }

    /// <summary>
    /// Gets a value indicating whether the reservation was already released.
    /// </summary>
    public bool AlreadyReleased { get; init; }
}