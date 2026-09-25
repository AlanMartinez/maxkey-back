using System.Security.Cryptography;
using System.Text;
using Maxkeys.Api.Auth;
using Maxkeys.Application.Catalog;
using Maxkeys.Application.Persistence;
using Maxkeys.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maxkeys.Api.Endpoints;

/// <summary>Admin-only credentials for browser-direct ImageKit uploads.</summary>
public static class AdminMediaEndpoints
{
    public static IEndpointRouteBuilder MapAdminMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/media").RequireAuthorization(AdminPolicy.Name);

        group.MapGet("/imagekit-auth", (IOptions<StorageOptions> storageOptions, HttpResponse response) =>
        {
            var options = storageOptions.Value;
            if (!ImageKitUploadPolicy.CanIssueClientUploadCredentials(options))
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "ImageKit browser uploads are not configured safely");
            }

            var token = Guid.NewGuid().ToString();
            var expire = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
            var signature = CreateSignature(token, expire, options.ImageKitPrivateKey);

            response.Headers.CacheControl = "no-store";
            return Results.Ok(new ImageKitAuthResponse(token, signature, expire, options.ImageKitPublicKey));
        });

        group.MapPost("/imagekit-assets", async (ImageKitAssetRegistrationRequest body, IAppDbContext db, CancellationToken cancellationToken) =>
        {
            var filePath = ImageKeyPolicy.NormalizeImageKitUploadPath(body.FilePath);
            if (!ImageKeyPolicy.IsSafeRelativeKey(filePath!) || !ImageKeyPolicy.IsImageKitUploadKey(filePath!))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "ImageKit file path must use products/, carousel/ or guides/");
            }

            var exists = await db.ImageKitAssets.AnyAsync(asset => asset.FilePath == filePath, cancellationToken);
            if (!exists)
            {
                db.ImageKitAssets.Add(new ImageKitAsset(filePath!));
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.NoContent();
        });

        return app;
    }

    private static string CreateSignature(string token, long expire, string privateKey)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(privateKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(token + expire));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>Short-lived ImageKit upload credentials. Never includes ImageKit private key.</summary>
public sealed record ImageKitAuthResponse(string Token, string Signature, long Expire, string PublicKey);

/// <summary>Opaque ImageKit <c>filePath</c> returned by an authenticated direct upload.</summary>
public sealed record ImageKitAssetRegistrationRequest(string FilePath);
