namespace Maxkeys.Api.Tests.Fixtures;

/// <summary>Shares one <see cref="ApiTestFixture"/> (one Postgres container, one host) across this collection.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiTestFixture>
{
    public const string Name = "Api";
}
