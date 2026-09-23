# ImageKit Media Migration Design

## Intent and success criteria

Move catalog and carousel media delivery from Cloudflare R2 to ImageKit without changing public API shapes, endpoint URLs, or stored image keys. Admin users must be able to upload approved images from the browser without exposing ImageKit private credentials.

Success means existing records continue to render during migration, new browser uploads persist an opaque storage key, and ImageKit becomes the primary URL origin after assets are copied.

## Non-goals

- Do not change `imageKey`, `detailImageKey`, or image-key collection DTO fields.
- Do not store complete ImageKit URLs in domain entities or database columns.
- Do not expose `IMAGEKIT_PRIVATE_KEY` to browser code, API responses, logs, or frontend environment variables.
- Do not remove R2 until functional validation and migration completeness checks pass.

## Key and URL contract

`imageKey` remains an opaque relative storage key, for example `products/elden-ring.png`, `products/elden-ring-2.png`, or `carousel/summer-sale.png`. Consumers keep receiving unchanged DTOs and endpoint URLs.

Introduce an infrastructure `IImageUrlResolver` used at all API projection points that currently turn keys into browser URLs. Given an image key, it returns:

1. `IMAGEKIT_URL_ENDPOINT` + `/` + URL-escaped key when key is present in explicit migrated-key manifest.
2. Existing R2 URL otherwise.
3. Existing null/empty-key behavior unchanged.

The ImageKit endpoint is normalized once at configuration binding so duplicate separators cannot be generated. Image keys are normalized and validated as relative slash-separated paths; they must not contain an absolute URL, query string, `..`, or leading slash.

Browser 404 fallback is intentionally forbidden. It cannot distinguish transient delivery failures, deleted files, authorization failures, and unmigrated assets; it also produces broken first paint and forces frontend origin knowledge. Resolver chooses origin deterministically from migration state.

## Configuration and secrets

Add backend-only configuration section:

```text
ImageKit:UrlEndpoint      = IMAGEKIT_URL_ENDPOINT
ImageKit:PublicKey        = IMAGEKIT_PUBLIC_KEY
ImageKit:PrivateKey       = IMAGEKIT_PRIVATE_KEY
ImageKit:UploadMaxBytes   = configurable maximum image size
ImageKit:MigratedKeysPath = deployed manifest path or configuration source
```

`UrlEndpoint` and `PublicKey` may be read by backend API. `PrivateKey` is server-only and binds only in backend configuration. Startup validates endpoint format, non-empty keys needed for enabled upload support, positive maximum size, and reachable/readable manifest. No secret is emitted through health checks, errors, telemetry, or configuration dumps.

`IMAGEKIT_PRIVATE_KEY`, R2 credentials, and other existing secrets remain Fly secrets. Public endpoint/key may live in root `fly.toml` `[env]` only if deployment policy permits; private key must never appear there.

## Migration manifest and R2 fallback

Use an explicit immutable manifest of successfully copied keys. Preferred form: JSON list committed/generated as deployment artifact, loaded into a case-sensitive set at startup. A manifest entry is written only after ImageKit upload succeeds and returned `filePath` exactly equals expected key.

Example:

```json
{ "version": 1, "keys": ["products/example.png", "products/example-2.png"] }
```

Manifest makes fallback deterministic and avoids any client behavior change. It must cover product images, detail images, all product image collections, and carousel keys. It is source-of-truth during transition; do not infer migration from URL responses.

## Asset migration procedure

Provide an operator-run, idempotent migration CLI or script using R2 read credentials and ImageKit server credentials:

1. Enumerate referenced database keys and optionally R2 objects under `products/` and `carousel/`.
2. Reject unsupported paths before transfer.
3. Stream each R2 object to ImageKit with its original key as ImageKit `fileName`/path so ImageKit returns same `filePath`.
4. Preserve exact paths such as `products/{slug}.png` and `products/{slug}-2.png`; never derive filenames from URLs.
5. Compare source byte size and checksum when R2 metadata permits; record source key, ImageKit file id/path, size, checksum, time, and result in migration report.
6. Add only verified keys to manifest. Re-runs skip verified entries and retry failed entries.
7. Deploy manifest with resolver change. Keep R2 active through validation and rollback window.

Script must not print credentials or file content. A dry-run lists candidates and validates configuration without writes. Failed/ambiguous entries remain absent from manifest and therefore keep R2 fallback.

## Admin direct upload

Add authenticated endpoint:

```text
GET /admin/media/imagekit-auth
```

Endpoint belongs to existing `AdminPolicy`; non-admin or unauthenticated requests receive normal authorization failures. It creates ImageKit upload authentication values using `IMAGEKIT_PRIVATE_KEY`:

- `token`: cryptographically random one-time upload token.
- `expire`: Unix epoch seconds, short-lived, future-dated, and less than one hour ahead.
- `signature`: HMAC-SHA1 of `token + expire`, keyed with private key.
- `publicKey`: configured ImageKit public key.

Exact response contract:

```json
{ "token": "...", "signature": "...", "expire": 0, "publicKey": "..." }
```

No private key, endpoint secret, R2 credential, or unrestricted upload policy is returned. Response uses `no-store` cache control. Rate limiting follows admin endpoint policy; token generation failures return generic server error without secret details.

Browser upload flow:

1. Admin calls auth endpoint with existing session.
2. UI validates selected file locally for immediate feedback.
3. UI uploads directly to ImageKit using returned signed fields and requested allowed path.
4. UI uses ImageKit returned `filePath` as opaque `imageKey` in existing product/carousel create or update request.
5. Backend validates submitted key before persistence. New direct-upload keys are treated as ImageKit keys immediately, either by atomically updating manifest/configured key registry or by resolver policy for keys created after ImageKit cutover.

## Upload restrictions

Server-side authorization/signing flow permits only intended requests:

- Folder/path begins exactly `products/` or `carousel/`.
- File is an image from allowlist (`image/png`, `image/jpeg`, `image/webp`, `image/avif`); SVG excluded unless separately threat-modeled.
- Uploaded byte size is no greater than configured `ImageKit:UploadMaxBytes`.
- Filename/path validation prohibits traversal, absolute paths, and arbitrary folders.
- Key is collision-safe: client uses generated filename under permitted prefix, or ImageKit unique-file behavior is enabled. Existing catalog keys are never overwritten accidentally.

ImageKit Upload v1 signature covers only `token + expire`; it does not cryptographically bind `folder`, MIME, or size. Therefore backend validates the returned `filePath` before persistence, while ImageKit DAM Path Policy must deny destinations outside `/products` and `/carousel`. Client upload submits ImageKit `checks` for MIME/size validation and UI pre-validates selection, but these are defense in depth because browser parameters can be changed. Before enabling production uploads, prove ImageKit configuration can enforce allowed folders and image/size rules. If ImageKit cannot enforce all constraints, direct uploads do not meet this design: use a backend upload proxy that inspects bytes and forwards only approved files.

## API and frontend impact

Public product, carousel, catalog, detail, and cart DTO schemas remain unchanged. Only URL-producing application mapping changes origin selection. Existing browser render paths consume resolved URL as today.

Frontend source is absent from this checkout. Backend work includes endpoint and contract tests; browser upload component, ImageKit client integration, and visual regression/manual validation must be performed in frontend repository before R2 retirement.

## Deployment

Root `fly.toml` is production source of truth. Preserve existing release command and all existing `[env]` values while adding non-secret ImageKit settings. Deploy `-a maxkeys`, never from `deploy/fly.toml`.

Set secrets through Fly secrets:

```text
IMAGEKIT_PRIVATE_KEY
```

Set `IMAGEKIT_URL_ENDPOINT` and `IMAGEKIT_PUBLIC_KEY` through root `fly.toml` `[env]` or Fly secrets according to policy. Confirm release migration command, CORS, Auth, Frontend base URL, notification URL, and all existing required values remain after deploy because Fly replaces `[env]` wholesale.

Rollout order: provision ImageKit Media Library; configure secrets/settings; deploy resolver with empty manifest (R2 only); execute migration; deploy verified manifest; enable UI upload; validate; retain R2 for rollback window; retire R2 only after acceptance evidence and approved removal change.

## Validation and tests

Backend automated coverage:

- Resolver emits ImageKit URL only for manifest keys and R2 URL for other valid keys.
- URL joining/escaping and invalid-key rejection.
- Product primary, detail, gallery, and carousel projections preserve opaque keys and resolve correct origin.
- Auth endpoint requires existing admin policy; response has exactly four fields: `token`, `signature`, `expire`, `publicKey`.
- Signature matches ImageKit algorithm; expiration is bounded future time; response is no-store.
- Submitted upload key accepts only `products/`/`carousel/`, permitted image forms, and maximum size policy.
- Migration dry-run, successful copy, idempotent rerun, mismatched returned path, and failed transfer keep manifest safe.

Manual/integration acceptance before R2 removal:

- Catalog image grid renders migrated and intentionally unmigrated assets.
- Carousel renders each slide origin correctly.
- Product detail primary/detail/gallery images render.
- Cart thumbnails render from resolved URLs.
- Admin upload succeeds for permitted image in each folder; persisted `imageKey` equals returned ImageKit `filePath`.
- Unauthorized upload-auth request is rejected; oversize, invalid MIME, invalid prefix, and traversal attempts fail.
- Production deployment retains all required Fly settings and release migration command.

## Rollback and retirement

Rollback resolver by deploying empty/previous manifest; opaque keys and database records need no migration rollback. Keep copied ImageKit assets intact. Do not delete R2 objects, credentials, or code until validation has passed in production, manifest covers every referenced key, no R2 fallback traffic is observed through agreed monitoring window, and a separate approved retirement change removes fallback safely.
