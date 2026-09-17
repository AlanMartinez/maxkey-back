using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Catalog;

/// <summary>
/// One image in a <see cref="Product"/>'s gallery, keyed by R2 object key —
/// same manual-key mechanism as <see cref="Product.ImageKey"/>/<see cref="Product.DetailImageKey"/>,
/// no upload/SDK involved. Whole-list replace, not per-item editing: admin
/// resubmits the full ordered key list on <c>UpdateProduct</c>, mirroring the
/// PUT-full-record convention used elsewhere in admin catalog (design D3).
/// <see cref="SortOrder"/> 0 is the primary/first image.
/// </summary>
public sealed class ProductImage : Entity
{
    public Guid ProductId { get; private set; }
    public string ImageKey { get; private set; }
    public int SortOrder { get; private set; }

    public ProductImage(Guid productId, string imageKey, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(imageKey))
        {
            throw new DomainException("Product image key must not be empty.");
        }

        ProductId = productId;
        ImageKey = imageKey;
        SortOrder = sortOrder;
    }
}
