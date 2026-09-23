using Maxkeys.Domain.Common;

namespace Maxkeys.Domain.Catalog;

/// <summary>Durable allow-list entry for a file confirmed by an admin after direct ImageKit upload.</summary>
public sealed class ImageKitAsset : Entity
{
    public string FilePath { get; private set; }

    public ImageKitAsset(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new DomainException("ImageKit file path must not be empty.");
        }

        FilePath = filePath;
    }
}
