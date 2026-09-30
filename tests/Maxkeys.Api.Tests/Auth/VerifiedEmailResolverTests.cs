using System.Security.Claims;
using Maxkeys.Api.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Tests.Auth;

/// <summary>
/// Covers <see cref="VerifiedEmailResolver"/>, the single control deciding whether a
/// token's email may take ownership of a guest order — and therefore of its keys,
/// since the buyer reveal endpoint is the only path that discloses a code (ADR-14).
/// Every case that is not a provider-verified email MUST resolve to
/// <see langword="null"/>.
/// </summary>
public sealed class VerifiedEmailResolverTests
{
    private const string Email = "buyer@example.com";

    [Fact]
    public void Google_token_resolves_the_email()
    {
        var resolved = CreateSut().Resolve(Principal(Email, provider: "google"));
        Assert.Equal(Email, resolved);
    }

    [Fact]
    public void Provider_match_is_case_insensitive()
    {
        var resolved = CreateSut().Resolve(Principal(Email, provider: "Google"));
        Assert.Equal(Email, resolved);
    }

    [Fact]
    public void Email_password_signup_is_refused()
    {
        // The attack this whole class exists for: Supabase's public /auth/v1/signup can
        // mint a token for an address its owner never confirmed, and that token must not
        // be able to claim the real buyer's guest order.
        var resolved = CreateSut().Resolve(Principal(Email, provider: "email"));
        Assert.Null(resolved);
    }

    [Fact]
    public void Unknown_provider_is_refused()
    {
        var resolved = CreateSut().Resolve(Principal(Email, provider: "github"));
        Assert.Null(resolved);
    }

    [Fact]
    public void Token_without_app_metadata_is_refused()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("email", Email)], "Test"));
        Assert.Null(CreateSut().Resolve(principal));
    }

    [Fact]
    public void Token_with_unparseable_app_metadata_is_refused()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("email", Email), new Claim("app_metadata", "not-json")], "Test"));
        Assert.Null(CreateSut().Resolve(principal));
    }

    [Fact]
    public void Token_with_app_metadata_lacking_provider_is_refused()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("email", Email), new Claim("app_metadata", """{"providers":["google"]}""")], "Test"));
        Assert.Null(CreateSut().Resolve(principal));
    }

    [Fact]
    public void Token_without_an_email_claim_is_refused()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("app_metadata", """{"provider":"google"}""")], "Test"));
        Assert.Null(CreateSut().Resolve(principal));
    }

    [Fact]
    public void Empty_allowlist_denies_every_caller()
    {
        var resolved = CreateSut(providers: []).Resolve(Principal(Email, provider: "google"));
        Assert.Null(resolved);
    }

    private static VerifiedEmailResolver CreateSut(string[]? providers = null) =>
        new(
            new StaticOptionsMonitor(new AuthOptions { VerifiedEmailProviders = providers ?? ["google"] }),
            NullLogger<VerifiedEmailResolver>.Instance);

    private static ClaimsPrincipal Principal(string email, string provider) =>
        new(new ClaimsIdentity(
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("email", email),
                new Claim("app_metadata", $$"""{"provider":"{{provider}}"}"""),
            ],
            "Test"));

    private sealed class StaticOptionsMonitor : IOptionsMonitor<AuthOptions>
    {
        public StaticOptionsMonitor(AuthOptions value)
        {
            CurrentValue = value;
        }

        public AuthOptions CurrentValue { get; }

        public AuthOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<AuthOptions, string?> listener) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
