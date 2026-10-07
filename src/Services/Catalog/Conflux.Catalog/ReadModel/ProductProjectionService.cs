using Conflux.Catalog.Domain;
using Conflux.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Conflux.Catalog.ReadModel;

/// <summary>
/// Maintains the Catalog read projection.
/// </summary>
public sealed class ProductProjectionService {
    private readonly CatalogDbContext _dbContext;

    /// <summary>
    /// Initializes the projection service.
    /// </summary>
    /// <param name="dbContext">
    /// The Catalog database context.
    /// </param>
    public ProductProjectionService(
        CatalogDbContext dbContext) {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Projects a source product into the read model.
    /// </summary>
    /// <param name="product">
    /// The source product.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    public async Task ProjectAsync(
        Product product,
        CancellationToken cancellationToken) {
        var projection =
            await _dbContext.ProductReadModels
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.ProductId == product.Id,
                    cancellationToken);

        if (projection is null) {
            _dbContext.ProductReadModels.Add(
                new ProductReadModel(
                    product.Id,
                    product.Sku,
                    product.Name,
                    product.Description,
                    product.Price,
                    product.Currency,
                    product.IsActive,
                    product.CreatedAt,
                    product.UpdatedAt));
        }
        else {
            projection.Apply(
                product.Name,
                product.Description,
                product.Price,
                product.Currency,
                product.IsActive,
                product.CreatedAt,
                product.UpdatedAt);
        }
    }

    /// <summary>
    /// Rebuilds the complete read projection from the write model.
    /// </summary>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    public async Task RebuildAsync(
        CancellationToken cancellationToken) {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        await _dbContext.ProductReadModels
            .ExecuteDeleteAsync(cancellationToken);

        var products =
            await _dbContext.Products
                .AsNoTracking()
                .ToListAsync(cancellationToken);

        _dbContext.ProductReadModels.AddRange(
            products.Select(
                product =>
                    new ProductReadModel(
                        product.Id,
                        product.Sku,
                        product.Name,
                        product.Description,
                        product.Price,
                        product.Currency,
                        product.IsActive,
                        product.CreatedAt,
                        product.UpdatedAt)));

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);
    }
}