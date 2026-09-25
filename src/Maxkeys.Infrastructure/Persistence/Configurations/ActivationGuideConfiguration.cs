using Maxkeys.Domain.Guides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maxkeys.Infrastructure.Persistence.Configurations;

/// <summary>activation-guides spec: UNIQUE(slug).</summary>
public sealed class ActivationGuideConfiguration : IEntityTypeConfiguration<ActivationGuide>
{
    public void Configure(EntityTypeBuilder<ActivationGuide> builder)
    {
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Slug).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Title).HasMaxLength(200).IsRequired();
        builder.Property(g => g.ContentMarkdown).HasColumnType("text").IsRequired();

        builder.HasIndex(g => g.Slug).IsUnique();
    }
}
