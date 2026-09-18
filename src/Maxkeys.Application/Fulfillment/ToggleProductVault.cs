using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Enables or disables vault auto-fulfillment for a product (vault spec:
/// Per-Product Vault Toggle) via <see cref="Domain.Catalog.Product.SetVaultEnabled"/>.
/// Returns <see langword="null"/> for an unknown product so the endpoint can
/// map it to 404.
/// </summary>
public sealed class ToggleProductVault
{
    private readonly IAppDbContext _db;

    public ToggleProductVault(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<bool?> ExecuteAsync(Guid productId, bool enabled, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.SetVaultEnabled(enabled);
        await _db.SaveChangesAsync(cancellationToken);

        return product.VaultEnabled;
    }
}
