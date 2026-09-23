using Maxkeys.Domain.Common;
using Maxkeys.Domain.Guides;

namespace Maxkeys.Domain.Tests.Guides;

public class ActivationGuideTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesGuide()
    {
        var guide = new ActivationGuide("microsoft-gift-card-activation", "Cómo activar tu Microsoft Gift Card", "## Paso 1");

        Assert.Equal("microsoft-gift-card-activation", guide.Slug);
        Assert.Equal("Cómo activar tu Microsoft Gift Card", guide.Title);
        Assert.Equal("## Paso 1", guide.ContentMarkdown);
    }

    [Fact]
    public void Constructor_WithNoContent_DefaultsToEmptyString()
    {
        var guide = new ActivationGuide("slug", "Title");

        Assert.Equal(string.Empty, guide.ContentMarkdown);
    }

    [Fact]
    public void Constructor_WithEmptySlug_Throws()
    {
        Assert.Throws<DomainException>(() => new ActivationGuide("", "Title"));
    }

    [Fact]
    public void Constructor_WithUppercaseSlug_Throws()
    {
        Assert.Throws<DomainException>(() => new ActivationGuide("Microsoft-Guide", "Title"));
    }

    [Fact]
    public void Constructor_WithEmptyTitle_Throws()
    {
        Assert.Throws<DomainException>(() => new ActivationGuide("slug", ""));
    }

    [Fact]
    public void Update_WithValidData_ReplacesAllFields()
    {
        var guide = new ActivationGuide("slug", "Title", "Old content");

        guide.Update("new-slug", "New Title", "New content");

        Assert.Equal("new-slug", guide.Slug);
        Assert.Equal("New Title", guide.Title);
        Assert.Equal("New content", guide.ContentMarkdown);
    }

    [Fact]
    public void Update_WithEmptySlug_Throws()
    {
        var guide = new ActivationGuide("slug", "Title");

        Assert.Throws<DomainException>(() => guide.Update("", "Title", null));
    }

    [Fact]
    public void Update_WithUppercaseSlug_Throws()
    {
        var guide = new ActivationGuide("slug", "Title");

        Assert.Throws<DomainException>(() => guide.Update("Slug", "Title", null));
    }

    [Fact]
    public void Update_WithEmptyTitle_Throws()
    {
        var guide = new ActivationGuide("slug", "Title");

        Assert.Throws<DomainException>(() => guide.Update("slug", "", null));
    }

    [Fact]
    public void Update_WithNullContent_SetsEmptyString()
    {
        var guide = new ActivationGuide("slug", "Title", "content");

        guide.Update("slug", "Title", null);

        Assert.Equal(string.Empty, guide.ContentMarkdown);
    }
}
