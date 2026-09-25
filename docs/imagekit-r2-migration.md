# R2 to ImageKit migration

Keep `imageKey` opaque. Copy each R2 object to ImageKit using identical key:

- `products/{slug}.png`
- `products/{slug}-2.png`
- `carousel/{name}.png`

Set backend-only secrets in Fly after creating ImageKit Media Library:

```powershell
fly secrets set -a maxkeys Storage__ImageKitPrivateKey='<private key>' Storage__ImageKitPublicKey='<public key>'
fly secrets set -a maxkeys Storage__ImageKitUrlEndpoint='https://ik.imagekit.io/<id>'
```

Do not add private key to `fly.toml`, frontend environment, or source control.

`ImageKitMigratedKeys` is explicit rollout manifest. Add an object key only after upload succeeds and delivery URL returns expected image. Keys missing from manifest continue resolving through R2.

Use ImageKit dashboard bulk upload/import or a one-off operator script with R2 credentials from a secure shell. Preserve each object key exactly as ImageKit `filePath`; do not alter database values. Validate catalog, carousel, product detail, and cart before adding every key or retiring R2.

## Frontend upload contract

`GET /admin/media/imagekit-auth` requires admin authorization and returns `200 OK` with `Cache-Control: no-store`:

```json
{ "token": "...", "signature": "...", "expire": 0, "publicKey": "..." }
```

Use those fields for ImageKit direct upload. Folder must be exactly `/products/uploads` for product image/gallery files or `/carousel/uploads` for slide files. Persist ImageKit `response.filePath` unchanged as opaque `imageKey`; backend resolves those canonical prefixes to ImageKit automatically. Existing valid relative keys remain manual R2 fallback unless listed in `ImageKitMigratedKeys`.

ImageKit direct-upload signatures cannot bind MIME type, byte size, or folder. Frontend must reject non-images and files above 20 MiB; ImageKit account upload policy must enforce same MIME/image, 20 MiB, and canonical-folder restrictions before admin UI is exposed.
