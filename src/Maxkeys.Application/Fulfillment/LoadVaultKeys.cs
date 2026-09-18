using Maxkeys.Application.Persistence;
using Maxkeys.Application.Security;
using Maxkeys.Domain.Common;
using Maxkeys.Domain.Keys;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Fulfillment;

/// <summary>
/// Bulk-loads vault key stock for a product variant (vault spec: Bulk Key
/// Load). Same encryption path as <see cref="AttachKeyToOrderItem"/> — each
/// code is encrypted independently and inserted as an
/// <see cref="KeyStatus.Available"/> <see cref="Key"/> row. Returns
/// <see langword="null"/> for an unknown variant so the API layer can map it
/// to 404.
/// </summary>
public sealed class LoadVaultKeys
{
    private readonly IAppDbContext _db;
    private readonly KeyCipher _keyCipher;

    public LoadVaultKeys(IAppDbContext db, KeyCipher keyCipher)
    {
        _db = db;
        _keyCipher = keyCipher;
    }

    public async Task<LoadVaultKeysResult?> ExecuteAsync(
        Guid productVariantId, IReadOnlyList<string> codes, string loadedBy, CancellationToken cancellationToken = default)
    {
        if (codes is null || codes.Count == 0)
        {
            throw new DomainException("At least one key code must be provided.");
        }

        if (codes.Any(string.IsNullOrWhiteSpace))
        {
            throw new DomainException("Key codes must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(loadedBy))
        {
            throw new DomainException("Loaded-by identity must not be empty.");
        }

        var variantExists = await _db.ProductVariants.AnyAsync(v => v.Id == productVariantId, cancellationToken);
        if (!variantExists)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var code in codes)
        {
            var (blob, version) = _keyCipher.Encrypt(code);
            _db.Keys.Add(new Key(productVariantId, blob, version, loadedBy, now));
        }

        await _db.SaveChangesAsync(cancellationToken);

        var availableCount = await _db.Keys.CountAsync(
            k => k.ProductVariantId == productVariantId && k.Status == KeyStatus.Available, cancellationToken);

        return new LoadVaultKeysResult(codes.Count, availableCount);
    }
}

/// <summary>Result of a bulk vault key load (vault spec: Bulk Key Load response shape).</summary>
public sealed record LoadVaultKeysResult(int AddedCount, int AvailableCount);
