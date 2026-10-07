namespace Conflux.Contracts.Events;

/// <summary>
/// Defines the stable v1 event type identifiers used on Kafka headers.
/// </summary>
public static class EventContractRegistry
{
    /// <summary>Gets the stable mapping between event type identifiers and CLR contracts.</summary>
    public static IReadOnlyDictionary<string, Type> V1 { get; } =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["catalog.product-created.v1"] = typeof(ProductCreated),
            ["inventory.reserved.v1"] = typeof(InventoryReserved),
            ["inventory.reservation-released.v1"] = typeof(InventoryReservationReleased),
            ["order.inventory-reserved.v1"] = typeof(OrderInventoryReserved),
            ["order.confirmed.v1"] = typeof(OrderConfirmed),
            ["payment.authorized.v1"] = typeof(PaymentAuthorized),
            ["payment.captured.v1"] = typeof(PaymentCaptured),
            ["payment.voided.v1"] = typeof(PaymentVoided),
            ["payment.refunded.v1"] = typeof(PaymentRefunded)
        };

    /// <summary>Resolves a stable event type identifier.</summary>
    /// <param name="eventType">The Kafka event type header value.</param>
    /// <returns>The contract type, or <see langword="null"/> when unknown.</returns>
    public static Type? Resolve(string eventType) =>
        V1.TryGetValue(eventType, out var type) ? type : null;
}
