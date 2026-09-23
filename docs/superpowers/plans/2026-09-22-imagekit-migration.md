# ImageKit Media Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver catalog and carousel images from ImageKit after each key is verified as migrated, while retaining deterministic R2 fallback and enabling secure browser uploads for admins.

**Architecture:** Replace the single-origin `ImageUrlBuilder` with an `IImageUrlResolver` backed by validated ImageKit settings and a case-sensitive, immutable migrated-key manifest. Keep domain keys and public DTO shapes opaque and unchanged; projections ask the resolver for URLs. The API creates short-lived ImageKit upload signatures, and a separate operator CLI copies R2 assets then emits the manifest.

**Tech Stack:** .NET 8 minimal APIs, options validation, HMAC-SHA1, EF Core/Npgsql, R2 S3-compatible client, ImageKit Upload v1 API, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-22-imagekit-migration-design.md`

## Global Constraints

- Preserve `imageKey`, `detailImageKey`, image-key collections, DTO schemas, and public endpoint URLs.
- Keys are relative slash-separated paths only: no URL, query, leading slash, or `..`; ImageKit paths must begin `products/` or `carousel/`.
- `IMAGEKIT_PRIVATE_KEY` is backend-only/Fly secret; never return, log, or configure it in frontend-visible values.
- A key uses ImageKit only when present in the verified manifest; all other valid keys use existing R2 resolution. Never implement browser 404 fallback.
- Preserve the root `fly.toml` release command and every existing `[env]` entry; deploy only with `flyctl deploy -a maxkeys`.
- Do not remove R2, its data, or its credentials in this change.

## Review Focus

- A manifest key containing spaces or Unicode must be escaped once per path segment, without changing its stored opaque key (resolver tests).
- An invalid/traversal/absolute key must be rejected before persistence or transfer (key-validator and endpoint tests).
- A copied ImageKit object whose returned `filePath` differs from the source key must remain out of the manifest (migration tests).
- An auth response must be non-cacheable and contain only `token`, `signature`, `expire`, and `publicKey` (API tests).
- A new direct-upload key must resolve through ImageKit immediately only after its registry/manifest update is durable (upload-key policy tests).

---

## File Structure

- Modify `src/Maxkeys.Application/Catalog/StorageOptions.cs`: replace the concrete URL builder contract with `IImageUrlResolver`, validated ImageKit options, manifest loading, and path validation.
- Modify catalog and carousel use cases under `src/Maxkeys.Application/{Catalog,Carousel}`: inject `IImageUrlResolver`; preserve mapping DTOs and stored keys.
- Create `src/Maxkeys.Api/Endpoints/AdminMediaEndpoints.cs`: authorized ImageKit upload-auth endpoint and exact response contract.
- Modify `src/Maxkeys.Api/Program.cs` and `src/Maxkeys.Api/appsettings*.json`: registration, startup validation, endpoint mapping, and non-secret defaults.
- Modify root `fly.toml`: add only permitted public ImageKit environment settings without dropping current deployment configuration.
- Create `tools/Maxkeys.ImageKitMigration/`: idempotent operator CLI, report, and manifest writer; add it to `Maxkeys.sln`.
- Create `src/Maxkeys.Api/Media/imagekit-migrated-keys.json`: deployment manifest, initially `{ "version": 1, "keys": [] }`.
- Add focused unit/API tests in `tests/Maxkeys.Application.Tests/Catalog`, `tests/Maxkeys.Application.Tests/Carousel`, `tests/Maxkeys.Api.Tests/Admin`, and `tests/Maxkeys.Api.Tests/Fixtures`.

### Task 1: Validated resolution and key contract

**Files:** `StorageOptions.cs`; every catalog/carousel use case that currently receives `ImageUrlBuilder`; resolver and projection test files.

- [ ] Write failing resolver tests for empty keys, R2 fallback, manifest-selected ImageKit origin, normalized endpoint joins, segment escaping, and rejected absolute/query/traversal/leading-slash keys.
- [ ] Define `IImageUrlResolver.Resolve(string? imageKey): string`, `ImageKitOptions`, `MigratedKeysManifest`, and a single key-validation routine; bind options at startup, normalize `UrlEndpoint` once, load the manifest into `HashSet<string>(StringComparer.Ordinal)`, and fail startup for unreadable/invalid configuration.
- [ ] Make resolver return the existing empty-key result, ImageKit URL only for manifest membership, otherwise the current R2 URL; retain opaque input keys unchanged.
- [ ] Replace every `ImageUrlBuilder` constructor dependency and projection call in catalog/admin catalog/product detail/cart and carousel/admin carousel use cases with `IImageUrlResolver`; add projection tests covering primary, detail, gallery, carousel override, and product fallback images.
- [ ] Run `dotnet test tests/Maxkeys.Application.Tests/Maxkeys.Application.Tests.csproj --filter "FullyQualifiedName~Catalog|FullyQualifiedName~Carousel"`.

### Task 2: Secure admin upload authorization and submitted-key validation

**Files:** create `AdminMediaEndpoints.cs`; modify `Program.cs`; application commands that accept product, gallery, detail, and carousel image keys; API/admin tests and test fixtures.

- [ ] Write API tests proving `GET /admin/media/imagekit-auth` requires `AdminPolicy`, returns exactly the four specified JSON fields plus `Cache-Control: no-store`, uses future expiration below one hour, and verifies `HMACSHA1(privateKey, token + expire)`.
- [ ] Add an `ImageKitUploadAuthService` that generates a cryptographically random token, Unix-seconds expiration, and lowercase hexadecimal HMAC-SHA1 signature; map the endpoint under `/admin/media` with `AdminPolicy` and return only `token`, `signature`, `expire`, `publicKey`.
- [ ] Apply the shared validator to create/update product and carousel requests: accept only permitted prefixes and image extensions (`png`, `jpeg/jpg`, `webp`, `avif`), reject malformed keys before entity persistence, and route accepted post-cutover direct-upload keys into the durable migrated-key registry policy selected in Task 1.
- [ ] Add tests for allowed product/carousel keys and rejected invalid prefix, SVG, traversal, absolute URL, query string, and oversized-upload policy inputs. Document that browser MIME/size checks and ImageKit DAM path policy are mandatory defense layers, not substitutes for server validation.
- [ ] Run `dotnet test tests/Maxkeys.Api.Tests/Maxkeys.Api.Tests.csproj --filter "FullyQualifiedName~Admin"`.

### Task 3: Configuration, manifest deployment, and production wiring

**Files:** `Program.cs`, `appsettings.json`, `appsettings.Development.json`, root `fly.toml`, new manifest, test fixture configuration.

- [ ] Add `ImageKit:UrlEndpoint`, `PublicKey`, `PrivateKey`, `UploadMaxBytes`, and `MigratedKeysPath` configuration, with development/test-safe values and an initially empty deployed manifest.
- [ ] Register the resolver and upload-auth service in `Program.cs`, validate required enabled-upload settings before serving requests, and map `MapAdminMediaEndpoints()`.
- [ ] Add only `ImageKit__UrlEndpoint` and `ImageKit__PublicKey` to root `fly.toml` when policy permits; leave `ImageKit__PrivateKey` for `fly secrets set IMAGEKIT_PRIVATE_KEY=...`; preserve `release_command`, all CORS/Auth/Frontend/Payments values, and R2 URL.
- [ ] Update API test fixtures with an isolated temporary/test manifest and ImageKit values so current API tests continue to exercise deterministic R2 fallback.
- [ ] Run `dotnet test Maxkeys.sln` and inspect `fly.toml` against the deployment checklist in `AGENTS.md`.

### Task 4: Idempotent R2-to-ImageKit migration tool

**Files:** create `tools/Maxkeys.ImageKitMigration/Maxkeys.ImageKitMigration.csproj`, `Program.cs`, transfer/report services and tests; update `Maxkeys.sln`; generated manifest/report paths outside source control unless explicitly approved.

- [ ] Write tests with fake R2/ImageKit clients for dry run, successful copy, rerun skipping verified keys, failed transfer, checksum/size mismatch, and ImageKit `filePath` mismatch.
- [ ] Implement a CLI accepting configuration and `--dry-run`; enumerate product primary/detail/gallery and carousel keys from the database, optionally enumerate R2 `products/` and `carousel/` keys, then validate every candidate before transfer.
- [ ] Stream each R2 object to ImageKit at the exact original key; compare available byte size/checksum and require returned `filePath == sourceKey` before adding it to the manifest.
- [ ] Emit a secret-free report containing source key, ImageKit file id/path, size, checksum, timestamp, and result. Merge only verified keys into a versioned manifest, preserving prior verified entries; failures and ambiguities remain R2-backed.
- [ ] Run the migration-tool test project, then document the operator sequence: deploy empty manifest, dry-run, migrate, deploy verified manifest, validate, retain R2 through the rollback window.

### Task 5: Acceptance, rollback, and frontend handoff

**Files:** update `docs/` deployment/runbook material if one exists; no frontend files in this checkout.

- [ ] Manually verify catalog, detail primary/detail/gallery, cart thumbnail, and carousel URLs for one migrated and one intentionally unmigrated key.
- [ ] In the frontend repository, validate admin direct upload for each allowed folder, store ImageKit-returned `filePath` verbatim, and exercise unauthorized, oversize, invalid MIME, invalid-prefix, and traversal failures.
- [ ] Confirm ImageKit DAM policy denies paths outside `/products` and `/carousel` and enforces intended image/size rules; if it cannot, stop direct-upload rollout and use a backend byte-inspecting upload proxy instead.
- [ ] Record production evidence that Fly preserved all required settings and release migration command, manifest covers referenced keys, and R2 fallback traffic is absent for the agreed monitoring window before proposing a separate R2-retirement change.
- [ ] Roll back only by redeploying the prior/empty manifest; do not change database keys or delete copied ImageKit/R2 assets.

## Verification

- [ ] `dotnet test Maxkeys.sln`
- [ ] Run migration `--dry-run` against production-shaped configuration without writes; inspect report for invalid/missing keys.
- [ ] Deploy resolver with empty manifest, run verified migration, deploy the generated manifest, then complete the manual acceptance checks before enabling frontend uploads.
