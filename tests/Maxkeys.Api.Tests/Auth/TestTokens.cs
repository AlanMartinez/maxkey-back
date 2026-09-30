using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Maxkeys.Api.Tests.Auth;

/// <summary>Mints locally signed test JWTs so Auth tests never depend on a real Supabase project.</summary>
internal static class TestTokens
{
    public static string CreateHs256(
        string sub,
        string issuer,
        string audience,
        string secret,
        TimeSpan? lifetime = null,
        IEnumerable<Claim>? extraClaims = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        return Write(sub, issuer, audience, new SigningCredentials(key, SecurityAlgorithms.HmacSha256), lifetime, extraClaims);
    }

    /// <summary>
    /// Mints the Supabase shape the buyer endpoints read: an <c>email</c> claim plus
    /// the nested <c>app_metadata.provider</c> that <c>VerifiedEmailResolver</c>
    /// requires before an email may claim a guest order.
    /// </summary>
    public static string CreateSupabaseHs256(
        string sub,
        string issuer,
        string audience,
        string secret,
        string email,
        string provider = "google") =>
        CreateHs256(sub, issuer, audience, secret, lifetime: null, extraClaims:
        [
            new Claim("email", email),
            new Claim(
                "app_metadata",
                $$"""{"provider":"{{provider}}","providers":["{{provider}}"]}""",
                JsonClaimValueTypes.Json),
        ]);

    public static string CreateRs256(string sub, string issuer, string audience, SecurityKey signingKey, TimeSpan? lifetime = null) =>
        Write(sub, issuer, audience, new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256), lifetime);

    public static string CreateEs256(string sub, string issuer, string audience, SecurityKey signingKey, TimeSpan? lifetime = null) =>
        Write(sub, issuer, audience, new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256), lifetime);

    private static string Write(
        string sub,
        string issuer,
        string audience,
        SigningCredentials credentials,
        TimeSpan? lifetime,
        IEnumerable<Claim>? extraClaims = null)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", sub), .. extraClaims ?? []]),
            NotBefore = now.AddMinutes(-10),
            Expires = now.Add(lifetime ?? TimeSpan.FromMinutes(5)),
            SigningCredentials = credentials,
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
