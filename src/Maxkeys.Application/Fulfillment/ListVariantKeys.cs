using Maxkeys.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Lists every key loaded for a product variant, newest first, for the admin
/// vault audit view (vault spec: Vault Key Audit). Never exposes the
/// encrypted code — write-only by design (fulfillment spec: Key Encryption
/// at Rest), only status/metadata. Returns <see langword="null"/> for an
/// unknown variant so the endpoint can map it to 404.
/// </summary>
public sealed class ListVariantKeys
{
    private readonly IAppDbContext _db;

    public ListVariantKeys(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<VaultKeySummary>?> ExecuteAsync(Guid productVariantId, CancellationToken cancellationToken = default)
    {
        var variantExists = await _db.ProductVariants.AnyAsync(v => v.Id == productVariantId, cancellationToken);
        if (!variantExists)
        {
            return null;
        }

        return await _db.Keys
            .Where(k => k.ProductVariantId == productVariantId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new VaultKeySummary(k.Id, k.Status.ToString(), k.LoadedBy, k.CreatedAt, k.AssignedAt, k.OrderItemId))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// One loaded key's status/metadata for the admin vault audit view (vault
/// spec: Vault Key Audit response shape). Never includes the encrypted code.
/// </summary>
public sealed record VaultKeySummary(Guid KeyId, string Status, string LoadedBy, DateTimeOffset CreatedAt, DateTimeOffset? AssignedAt, Guid? OrderItemId);
