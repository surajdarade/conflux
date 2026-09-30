extern alias Inventory;
using Conflux.Contracts.Inventory;
using Conflux.Inventory.Features.Inventory.CreateInventory;
using Conflux.Order.Clients.Inventory;
using Conflux.Order.Infrastructure;
using Conflux.Order.IntegrationTests.Infrastructure;
using Inventory::Conflux.Inventory.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;

namespace Conflux.Order.IntegrationTests;

/// <summary>
/// Verifies the Order service Inventory gRPC client against a real
/// Inventory ASP.NET Core service backed by PostgreSQL.
/// </summary>
public sealed class InventoryGrpcClientTests {
    /// <summary>
    /// Verifies that inventory reservation travels through the Order gRPC
    /// client, Inventory gRPC service, application service, and database.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_ShouldReserveInventoryThroughRealGrpcService() {
        await using var inventoryFactory =
            new InventoryGrpcTestFactory();

        await inventoryFactory.StartAsync(
            TestContext.Current.CancellationToken);

        try {
            using var channel =
                inventoryFactory.CreateGrpcChannel();

            var grpcClient =
                new InventoryService.InventoryServiceClient(
                    channel);

            var createClient =
                inventoryFactory.CreateClient();

            var createResponse =
                await createClient.PostAsJsonAsync(
                    "/api/v1/inventory/items",
                    new CreateInventoryRequest
                    {
                        Sku =
                            $"TEST-{Guid.NewGuid():N}",
                        AvailableQuantity = 10
                    },
                    TestContext.Current.CancellationToken);

            createResponse.EnsureSuccessStatusCode();

            var created =
                await createResponse.Content
                    .ReadFromJsonAsync<CreateInventoryResponse>(
                        TestContext.Current.CancellationToken);

            Assert.NotNull(created);

            var inventoryItemId =
                created.InventoryId;

            var reservationId =
                Guid.NewGuid();

            var inventoryClient =
                new InventoryGrpcClient(
                    grpcClient);

            var reservation =
                await inventoryClient.ReserveAsync(
                    inventoryItemId,
                    reservationId,
                    3,
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                inventoryItemId,
                reservation.InventoryItemId);

            Assert.Equal(
                reservationId,
                reservation.ReservationId);

            Assert.Equal(
                3,
                reservation.ReservedQuantity);

            Assert.Equal(
                7,
                reservation.AvailableQuantity);

            Assert.Equal(
                3,
                reservation.TotalReservedQuantity);

            using var scope =
                inventoryFactory.Services.CreateScope();

            var dbContext =
                scope.ServiceProvider
                    .GetRequiredService<InventoryDbContext>();

            var inventory =
                await dbContext.InventoryItems.FindAsync(
                    [
                        inventoryItemId
                    ],
                    TestContext.Current.CancellationToken);

            Assert.NotNull(inventory);

            Assert.Equal(
                7,
                inventory.AvailableQuantity);

            Assert.Equal(
                3,
                inventory.ReservedQuantity);
        }
        finally {
            await inventoryFactory.StopAsync(
                TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Verifies that releasing an inventory reservation through the Order
    /// gRPC client restores the available inventory quantity.
    /// </summary>
    [Fact]
    public async Task ReleaseAsync_ShouldReleaseInventoryThroughRealGrpcService() {
        await using var inventoryFactory =
            new InventoryGrpcTestFactory();

        await inventoryFactory.StartAsync(
            TestContext.Current.CancellationToken);

        try {
            using var channel =
                inventoryFactory.CreateGrpcChannel();

            var grpcClient =
                new InventoryService.InventoryServiceClient(
                    channel);

            var createClient =
                inventoryFactory.CreateClient();

            var createResponse =
                await createClient.PostAsJsonAsync(
                    "/api/v1/inventory/items",
                    new CreateInventoryRequest
                    {
                        Sku =
                            $"TEST-{Guid.NewGuid():N}",
                        AvailableQuantity = 10
                    },
                    TestContext.Current.CancellationToken);

            createResponse.EnsureSuccessStatusCode();

            var created =
                await createResponse.Content
                    .ReadFromJsonAsync<CreateInventoryResponse>(
                        TestContext.Current.CancellationToken);

            Assert.NotNull(created);

            var inventoryItemId =
                created.InventoryId;

            var reservationId =
                Guid.NewGuid();

            var inventoryClient =
                new InventoryGrpcClient(
                    grpcClient);

            var reservation =
                await inventoryClient.ReserveAsync(
                    inventoryItemId,
                    reservationId,
                    4,
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                4,
                reservation.ReservedQuantity);

            var release =
                await inventoryClient.ReleaseAsync(
                    inventoryItemId,
                    reservationId,
                    TestContext.Current.CancellationToken);

            Assert.Equal(
                inventoryItemId,
                release.InventoryItemId);

            Assert.Equal(
                reservationId,
                release.ReservationId);

            Assert.Equal(
                4,
                release.ReleasedQuantity);

            Assert.Equal(
                10,
                release.AvailableQuantity);

            Assert.Equal(
                0,
                release.TotalReservedQuantity);

            Assert.False(
                release.AlreadyReleased);

            using var scope =
                inventoryFactory.Services.CreateScope();

            var dbContext =
                scope.ServiceProvider
                    .GetRequiredService<InventoryDbContext>();

            var inventory =
                await dbContext.InventoryItems.FindAsync(
                    [
                        inventoryItemId
                    ],
                    TestContext.Current.CancellationToken);

            Assert.NotNull(inventory);

            Assert.Equal(
                10,
                inventory.AvailableQuantity);

            Assert.Equal(
                0,
                inventory.ReservedQuantity);
        }
        finally {
            await inventoryFactory.StopAsync(
                TestContext.Current.CancellationToken);
        }
    }
}