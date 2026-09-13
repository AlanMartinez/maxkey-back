using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Maxkeys.Api.Tests.Auth;

/// <summary>Mints locally signed test JWTs so Auth tests never depend on a real Supabase project.</summary>
internal static class TestTokens
{
    public static string CreateHs256(string sub, string issuer, string audience, string secret, TimeSpan? lifetime = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        return Write(sub, issuer, audience, new SigningCredentials(key, SecurityAlgorithms.HmacSha256), lifetime);
    }

    public static string CreateRs256(string sub, string issuer, string audience, SecurityKey signingKey, TimeSpan? lifetime = null) =>
        Write(sub, issuer, audience, new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256), lifetime);

    public static string CreateEs256(string sub, string issuer, string audience, SecurityKey signingKey, TimeSpan? lifetime = null) =>
        Write(sub, issuer, audience, new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256), lifetime);

    private static string Write(string sub, string issuer, string audience, SigningCredentials credentials, TimeSpan? lifetime)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", sub)]),
            NotBefore = now.AddMinutes(-10),
            Expires = now.Add(lifetime ?? TimeSpan.FromMinutes(5)),
            SigningCredentials = credentials,
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
