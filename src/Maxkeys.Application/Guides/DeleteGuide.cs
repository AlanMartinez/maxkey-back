using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Maxkeys.Application.Guides;

/// <summary>
/// Hard-deletes an activation guide (unlike <c>Product</c>'s soft delete — a guide carries no
/// order-history reference to preserve). Throws <see cref="DomainConflictException"/> (409) when
/// any product still references it, so a product's FK is never left dangling. One class per use
/// case (ADR-02).
/// </summary>
public sealed class DeleteGuide
{
    private readonly IAppDbContext _db;

    public DeleteGuide(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var guide = await _db.ActivationGuides.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (guide is null)
        {
            return false;
        }

        var referenced = await _db.Products.AnyAsync(p => p.ActivationGuideId == id, cancellationToken);
        if (referenced)
        {
            throw new DomainConflictException("This guide is still linked to at least one product.");
        }

        _db.ActivationGuides.Remove(guide);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
