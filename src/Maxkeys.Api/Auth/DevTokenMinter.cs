using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Maxkeys.Api.Auth;

/// <summary>
/// Mints an Hs256 JWT for local dev testing of admin endpoints (design/auth spec
/// unaffected — <see cref="AdminAuthorizationHandler"/> stays fail-closed; this
/// only produces a token that satisfies it when <c>Auth:Mode=Hs256</c> and the
/// dev sub is in <c>Auth:AdminSubs</c>, both set in <c>appsettings.Development.json</c>
/// only). Never exposed as an HTTP endpoint — CLI-only, see <c>--print-dev-admin-token</c>
/// in <c>Program.cs</c>.
/// </summary>
internal static class DevTokenMinter
{
    public static string CreateHs256(string sub, string issuer, string audience, string secret, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", sub)]),
            NotBefore = now.AddMinutes(-1),
            Expires = now.Add(lifetime),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
