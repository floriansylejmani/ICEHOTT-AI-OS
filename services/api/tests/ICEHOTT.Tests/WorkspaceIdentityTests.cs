using ICEHOTT.Domain;

namespace ICEHOTT.Tests;

public sealed class WorkspaceIdentityTests
{
    [Fact]
    public void NewWorkspaceIdsAreNonemptyAndUnique()
    {
        var first = WorkspaceId.New();
        var second = WorkspaceId.New();
        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.NotEqual(first, second);
        Assert.Equal(first, WorkspaceId.From(first.Value));
    }

    [Fact]
    public void EmptyGuidCannotBeUsedAsWorkspaceId() =>
        Assert.Throws<ArgumentException>(() => WorkspaceId.From(Guid.Empty));

    [Theory]
    [InlineData("my-workspace")]
    [InlineData("team123")]
    [InlineData("abc")]
    public void SafeSlugsAreAccepted(string slug) =>
        Assert.Equal(slug, WorkspaceSlug.Create(slug).Value);

    [Theory]
    [InlineData("")]
    [InlineData("ABCD")]
    [InlineData("a b")]
    [InlineData("-abc")]
    [InlineData("abc-")]
    [InlineData("äbc")]
    [InlineData("../")]
    [InlineData("x")]
    public void UntrustedSlugsAreRejected(string slug) =>
        Assert.Throws<ArgumentException>(() => WorkspaceSlug.Create(slug));
}
