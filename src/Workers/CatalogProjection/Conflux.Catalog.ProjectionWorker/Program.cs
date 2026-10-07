using Conflux.Catalog.Infrastructure;
using Conflux.Catalog.ReadModel;
using Conflux.Catalog.ProjectionWorker;
using Conflux.Kafka;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CatalogDatabase")));
builder.Services.Configure<CatalogProjectionOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddOptions<KafkaRetryOptions>().Bind(builder.Configuration.GetSection(KafkaRetryOptions.SectionName));
builder.Services.AddSingleton<KafkaRetryPublisher>();
builder.Services.AddHostedService<KafkaRetryWorker>();
builder.Services.AddHostedService<KafkaDlqRecoveryWorker>();
builder.Services.AddHostedService<ProductProjectionWorker>();
await builder.Build().RunAsync();
