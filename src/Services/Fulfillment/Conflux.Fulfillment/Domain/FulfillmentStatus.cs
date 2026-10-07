namespace Conflux.Fulfillment.Domain;

/// <summary>Represents the fulfillment lifecycle.</summary>
public enum FulfillmentStatus
{
    /// <summary>The fulfillment has been created but not started.</summary>
    Pending,
    /// <summary>The fulfillment is being processed.</summary>
    InProgress,
    /// <summary>The fulfillment has completed.</summary>
    Completed
}
