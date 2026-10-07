namespace Conflux.Contracts.Events;

/// <summary>Represents a product created in the Catalog write model.</summary>
public sealed record ProductCreated
{
    /// <summary>Gets the event identifier.</summary>
    public required Guid EventId { get; init; }
    /// <summary>Gets the event occurrence time.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
    /// <summary>Gets the correlation identifier.</summary>
    public required Guid CorrelationId { get; init; }
    /// <summary>Gets the causation identifier.</summary>
    public Guid? CausationId { get; init; }
    /// <summary>Gets the product identifier.</summary>
    public required Guid ProductId { get; init; }
    /// <summary>Gets the SKU.</summary>
    public required string Sku { get; init; }
    /// <summary>Gets the product name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the product description.</summary>
    public required string Description { get; init; }
    /// <summary>Gets the product price.</summary>
    public required decimal Price { get; init; }
    /// <summary>Gets the currency.</summary>
    public required string Currency { get; init; }
    /// <summary>Gets whether the product is active.</summary>
    public required bool IsActive { get; init; }
    /// <summary>Gets the creation timestamp.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets the update timestamp.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
