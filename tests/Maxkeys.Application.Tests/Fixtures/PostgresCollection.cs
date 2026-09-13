namespace Maxkeys.Application.Tests.Fixtures;

/// <summary>Shares one <see cref="PostgresFixture"/> (one container) across all tests in this collection.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
