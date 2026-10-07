var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var identityDatabase = postgres.AddDatabase("IdentityDatabase");
var catalogDatabase = postgres.AddDatabase("CatalogDatabase");
var inventoryDatabase = postgres.AddDatabase("InventoryDatabase");
var orderDatabase = postgres.AddDatabase("OrderDatabase");
var paymentDatabase = postgres.AddDatabase("PaymentDatabase");
var fulfillmentDatabase = postgres.AddDatabase("FulfillmentDatabase");

var redis = builder.AddRedis("Redis")
    .WithDataVolume();

var kafka = builder.AddKafka("Kafka");

var identity = builder.AddProject<Projects.Conflux_Identity>("identity")
    .WithReference(identityDatabase)
    .WaitFor(identityDatabase);

var catalog = builder.AddProject<Projects.Conflux_Catalog>("catalog")
    .WithReference(catalogDatabase)
    .WithReference(redis)
    .WaitFor(catalogDatabase);

var inventory = builder.AddProject<Projects.Conflux_Inventory>("inventory")
    .WithReference(inventoryDatabase)
    .WaitFor(inventoryDatabase);

var payment = builder.AddProject<Projects.Conflux_Payment>("payment")
    .WithReference(paymentDatabase)
    .WaitFor(paymentDatabase);

var fulfillment = builder.AddProject<Projects.Conflux_Fulfillment>("fulfillment")
    .WithReference(fulfillmentDatabase)
    .WithReference(kafka)
    .WaitFor(fulfillmentDatabase)
    .WaitFor(kafka);

var order = builder.AddProject<Projects.Conflux_Order>("order")
    .WithReference(orderDatabase)
    .WithReference(kafka)
    .WithReference(inventory)
    .WithReference(payment)
    .WaitFor(orderDatabase)
    .WaitFor(inventory)
    .WaitFor(payment)
    .WaitFor(kafka);

builder.AddProject<Projects.Conflux_Gateway>("gateway")
    .WithReference(redis)
    .WithReference(identity)
    .WithReference(catalog)
    .WithReference(inventory)
    .WithReference(order)
    .WithReference(payment)
    .WithReference(fulfillment)
    .WaitFor(identity)
    .WaitFor(catalog)
    .WaitFor(inventory)
    .WaitFor(order)
    .WaitFor(payment)
    .WaitFor(fulfillment);
builder.AddProject<Projects.Conflux_Catalog_OutboxPublisher>("catalog-outbox")
    .WithReference(catalogDatabase)
    .WithReference(kafka);

builder.AddProject<Projects.Conflux_Catalog_ProjectionWorker>("catalog-projection")
    .WithReference(catalogDatabase)
    .WithReference(kafka);

builder.AddProject<Projects.Conflux_Inventory_OutboxPublisher>("inventory-outbox")
    .WithReference(inventoryDatabase)
    .WithReference(kafka);

builder.AddProject<Projects.Conflux_Order_OutboxPublisher>("order-outbox")
    .WithReference(orderDatabase)
    .WithReference(kafka);

builder.AddProject<Projects.Conflux_Payment_OutboxPublisher>("payment-outbox")
    .WithReference(paymentDatabase)
    .WithReference(kafka);

builder.Build().Run();
