using Conflux.Fulfillment.Domain;
using FluentAssertions;
using Xunit;

using FullfillmentEntity = Conflux.Fulfillment.Domain.Fulfillment;

namespace Conflux.Fulfillment.UnitTests;

/// <summary>Verifies fulfillment state transitions.</summary>
public sealed class FulfillmentDomainTests
{
    /// <summary>Verifies the valid Pending → InProgress → Completed transition.</summary>
    [Fact]
    public void Fulfillment_TransitionsThroughValidLifecycle()
    {
        var fulfillment = new FullfillmentEntity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        fulfillment.Status.Should().Be(FulfillmentStatus.Pending);

        fulfillment.Start();
        fulfillment.Status.Should().Be(FulfillmentStatus.InProgress);

        fulfillment.Complete();
        fulfillment.Status.Should().Be(FulfillmentStatus.Completed);
    }

    /// <summary>Verifies invalid completion from Pending is rejected.</summary>
    [Fact]
    public void Fulfillment_CannotCompleteBeforeStarting()
    {
        var fulfillment = new FullfillmentEntity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var action = () => fulfillment.Complete();
        action.Should().Throw<InvalidOperationException>();
    }
}
