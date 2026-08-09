# Restaurant Image Storage & Lifecycle Plan — Implementation Plan

> Scope: Defines the architecture, cloud provider evaluation (AWS vs Azure vs Cloudflare), database schema/fields across microservices, image upload workflow (presigned URLs + direct S3 upload), processing pipeline, and local dev infrastructure (MinIO) for restaurant asset management.

---

## Status

> **Plan version**: `v1.1` (2026-08-06)
> **Current state**: ⏸ Not started

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Cloud Provider Evaluation (AWS vs Azure vs Cloudflare) | ⏸ Pending |
| 2 | Image Schema & Data Fields Across Microservices | 🔒 Blocked |
| 3 | Storage Abstraction Layer & Local Dev Setup | 🔒 Blocked |
| 4 | Upload Workflow & Presigned URL API (Catalog Service) | 🔒 Blocked |
| 5 | Asynchronous Image Processing & CDN Delivery | 🔒 Blocked |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same commit as the phase work.**

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate
> All implementation work on this plan MUST follow the **vertical slice architecture** pattern established in `Catalog.API` (every feature = one folder with an `Endpoint.cs` + `Handler.cs`), use **Carter** for routing, **MediatR** for CQRS, and **FluentValidation** pipeline behaviors for input validation. DO NOT use MVC controllers. DO NOT use `DateTime` — always use NodaTime `Instant`.

### 0.2 Code-quality guard rails
- **Quality Gate script**: The `phase-guard.ps1` script MUST be executed at the end of every phase with the `-Quick` parameter:
  `pwsh ./scripts/phase-guard.ps1 -PhaseName "<Phase Name>" -Quick`
- **Zero Binary BLOBs in DB**: Database tables MUST store only image URLs and metadata — never raw binary payloads.
- **Strict Presigned Uploads**: Direct client-to-storage uploads via presigned URLs to keep microservice memory & network bandwidth clear.
- **Nullable reference types**: All new `.cs` files must have nullable enabled. No `#nullable disable` without justification.
- **Async all the way**: All I/O operations must be `async`/`await` with `CancellationToken` forwarded. No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`.
- **IOptions<T> for config**: All new settings classes MUST be registered via `builder.Services.Configure<T>(...)` and consumed through `IOptions<T>` — no string-keyed `IConfiguration` lookups.
- **No EF Core entities in API responses**: Always map to DTOs. New fields must be added to their corresponding response records.

---

## 1. Context

Restaurants need to upload menu item photos, category banners, and store logos. Currently:

- `Catalog.API` stores `ImageUrl` as a plain `string` (max 500, `NOT NULL`) on `MenuItem` — validated at the DB level but **not by FluentValidation**, meaning empty strings are accepted today.
- `Ordering.Domain` already holds an immutable `MenuItemImageUrl` string snapshot on `OrderItem`.
- `Basket.API`'s `BasketItem` model has **no image field at all** — the plan's analysis confirmed this field must be *added*, not verified.
- There is **no `IStorageService`, `IBlobStorage`, or AWS SDK reference anywhere** in the codebase — the entire object storage layer is greenfield.
- No upload endpoint, presigned URL logic, image processing pipeline, or MinIO container exists anywhere in the project.

This plan details the full lifecycle from cloud provider selection through schema changes, storage abstraction, upload API, and processing pipeline.

---

## 2. Goal

- Evaluate cloud storage & CDN providers (AWS S3, Azure Blob, Cloudflare R2) with pros, cons, and .NET SDK quality trade-offs.
- Define and **add** missing image fields across `Catalog.API`, `Basket.API`, and verify `Ordering.API`.
- Define a strongly-typed `IObjectStorageService` abstraction in `BuildingBlocks` and a `ObjectStorageSettings` class so the provider can be swapped without touching handlers.
- Provide a local S3-compatible development setup using **MinIO** in `docker-compose.yml`.
- Build direct-to-object-storage upload capabilities using presigned `PUT` URLs and a confirm-upload flow.
- Implement automated image resizing using **SixLabors.ImageSharp** (WebP generation, thumbnail creation) as an in-process `BackgroundService`.

---

## 3. Out of scope

- Video uploads or streaming media processing.
- Advanced AI image tagging / automated content moderation (deferred to future phase).
- Azure Blob Storage Azurite emulator setup (MinIO with S3 API covers all three providers locally).
- Multi-tenant bucket isolation (single shared bucket with path-prefixed keys for Phase 1–5).

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| **Storage abstraction** | `IObjectStorageService` interface in `BuildingBlocks` with `AWSSDK.S3` implementation | Cloudflare R2 and MinIO are both S3-compatible — one SDK covers all three local/cloud providers. Azure Blob requires its own SDK implementation behind the interface. |
| **Upload strategy** | Direct client-to-storage presigned `PUT` URLs | Avoids routing heavy file streams through `YarpApiGateway` or `Catalog.API`. 5MB max enforced at presigned URL level. |
| **Image processing library** | **SixLabors.ImageSharp** | MIT licensed, pure managed code (no native deps), `Span<T>`/`Memory<T>` optimized, no Dockerfile changes needed. `Magick.NET` rejected — native binary dependencies complicate multi-arch Docker images. |
| **Processing host** | **In-process `BackgroundService`** inside `Catalog.API` | Avoids adding a new project and Docker service for Phase 1–5. Can be extracted to `Catalog.Worker` in a future plan if throughput requires it. |
| **Settings class** | `ObjectStorageSettings` registered via `IOptions<T>` | Mandatory per `dotnet-best-practices` skill — no string-keyed `IConfiguration` lookups. Lives in `BuildingBlocks` so the processing worker can reference it without circular deps. |
| **Upload state model** | `ImageUploadStatus` enum on `MenuItem` + staging `PendingImageKey` | Provides an explicit state machine: `None → Pending → Confirmed`. Prevents empty `ImageUrl` strings from reaching the DB and makes the two-step flow explicit. |

---

## 5. Folder layout

```
orderly-microservices/
├── BuildingBlocks/
│   ├── BuildingBlocks.csproj
│   └── Storage/
│       ├── IObjectStorageService.cs          # abstraction
│       └── ObjectStorageSettings.cs          # IOptions<T> config class
│
├── Services/
│   ├── Catalog/
│   │   ├── Catalog.API/
│   │   │   ├── Features/
│   │   │   │   └── Media/
│   │   │   │       ├── GetMenuItemImageUploadUrl/
│   │   │   │       │   ├── GetMenuItemImageUploadUrlEndpoint.cs   # ICarterModule
│   │   │   │       │   └── GetMenuItemImageUploadUrlHandler.cs   # ICommand<Result>
│   │   │   │       └── ConfirmMenuItemImageUpload/
│   │   │   │           ├── ConfirmMenuItemImageUploadEndpoint.cs  # ICarterModule
│   │   │   │           └── ConfirmMenuItemImageUploadHandler.cs  # ICommand<Result>
│   │   │   ├── Infrastructure/
│   │   │   │   └── Storage/
│   │   │   │       └── S3ObjectStorageService.cs  # implements IObjectStorageService
│   │   │   └── Scheduling/
│   │   │       └── ImageProcessingBackgroundService.cs  # BackgroundService
│   │
│   ├── Basket/
│   │   └── Basket.API/
│   │       └── Models/
│   │           └── BasketItem.cs             # ADD MenuItemImageUrl field
```

---

## 6. Specification

### 6.1 Microservice Image Field Status

> [!IMPORTANT]
> Columns marked **ADD** require code + migration changes. Columns marked **EXISTS** are already in the codebase. Columns marked **VERIFY** need a code review to confirm before assuming they are complete.

| Microservice | Entity | Field | Status | Type | Purpose |
| :--- | :--- | :--- | :---: | :--- | :--- |
| **Catalog** | `MenuItem` | `ImageUrl` | EXISTS | `string` (max 500, NOT NULL) | Canonical CDN URL for main dish photo |
| **Catalog** | `MenuItem` | `ThumbnailUrl` | **ADD** | `string?` (max 500) | 150×150 thumbnail URL for fast list views |
| **Catalog** | `MenuItem` | `PendingImageKey` | **ADD** | `string?` | Staging object key during upload flow; cleared on confirm |
| **Catalog** | `MenuItem` | `ImageUploadStatus` | **ADD** | `enum` (None/Pending/Confirmed) | Explicit state machine for upload lifecycle |
| **Catalog** | `Restaurant` | `LogoUrl` | **VERIFY** | `string?` | Restaurant logo asset |
| **Catalog** | `Restaurant` | `BannerUrl` | **VERIFY** | `string?` | Restaurant banner/cover image |
| **Basket** | `BasketItem` | `MenuItemImageUrl` | **ADD** | `string` | Snapshot of image URL at cart entry time |
| **Ordering** | `OrderItem` | `MenuItemImageUrl` | EXISTS | `string` | Immutable snapshot of photo at order placement time |

### 6.2 `ObjectStorageSettings` — Strongly-Typed Configuration

Lives in `BuildingBlocks/Storage/ObjectStorageSettings.cs`. Registered in `Catalog.API/Program.cs` and any future worker.

```csharp
namespace BuildingBlocks.Storage;

public class ObjectStorageSettings
{
    public const string SectionName = "ObjectStorage";

    public string BucketName { get; set; } = string.Empty;
    /// <summary>MinIO: http://localhost:9000 | AWS: leave empty (SDK default) | R2: https://<accountId>.r2.cloudflarestorage.com</summary>
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    /// <summary>Public CDN base URL prepended to object keys for final ImageUrl values.</summary>
    public string PublicCdnBaseUrl { get; set; } = string.Empty;
    /// <summary>Presigned URL expiry window in minutes. Default: 15.</summary>
    public int PresignedUrlExpiryMinutes { get; set; } = 15;
    /// <summary>Max upload size in bytes enforced in the presigned URL policy. Default: 5MB.</summary>
    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;
}
```

### 6.3 `IObjectStorageService` — Storage Abstraction

Lives in `BuildingBlocks/Storage/IObjectStorageService.cs`. Allows swapping between MinIO, AWS S3, Cloudflare R2, and Azure Blob (via its own adapter) without touching MediatR handlers.

```csharp
namespace BuildingBlocks.Storage;

public interface IObjectStorageService
{
    /// <summary>Generates a presigned PUT URL for direct client upload. Content-Type and size are enforced.</summary>
    Task<string> GeneratePresignedUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken ct = default);

    /// <summary>Returns true if the object key exists in the bucket (used by ConfirmUpload handler).</summary>
    Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default);

    /// <summary>Returns the public CDN URL for a confirmed object key.</summary>
    string GetPublicUrl(string objectKey);

    /// <summary>Deletes an object by key (used when a MenuItem is deleted or image replaced).</summary>
    Task DeleteAsync(string objectKey, CancellationToken ct = default);
}
```

### 6.4 `ImageUploadStatus` Enum — Upload State Machine

```csharp
namespace BuildingBlocks.Enums;

public enum ImageUploadStatus
{
    /// <summary>No image upload initiated. MenuItem.ImageUrl may be empty or a legacy URL.</summary>
    None = 0,
    /// <summary>Presigned URL issued. Client is expected to upload. PendingImageKey is set.</summary>
    Pending = 1,
    /// <summary>Upload confirmed. ImageUrl reflects the CDN URL. PendingImageKey is cleared.</summary>
    Confirmed = 2
}
```

### 6.5 Media Feature Vertical Slices

Both features live under `Catalog.API/Features/Media/` and follow the same Carter + MediatR pattern as all other features in `Catalog.API`.

#### `GetMenuItemImageUploadUrl` — Request a presigned upload token

- **Route**: `POST /api/v1/restaurants/{restaurantId:guid}/menu-items/{menuItemId:guid}/image-upload-url`
- **Why POST**: Generates a side-effect token on the storage provider (not idempotent, not a safe read).
- **Handler logic**:
  1. Load `MenuItem` by `menuItemId` + `restaurantId`; throw `NotFoundException` if not found.
  2. Build `objectKey = $"restaurants/{restaurantId}/menu-items/{menuItemId}/{Guid.NewGuid()}.{ext}"`.
  3. Call `IObjectStorageService.GeneratePresignedUploadUrlAsync(objectKey, contentType)`.
  4. Persist `PendingImageKey = objectKey`, `ImageUploadStatus = Pending` to DB.
  5. Return `{ PresignedUrl, ObjectKey, ExpiresAt }`.
- **FluentValidation**: `ContentType` must be one of `image/jpeg`, `image/png`, `image/webp`.

#### `ConfirmMenuItemImageUpload` — Confirm completed upload & activate image

- **Route**: `POST /api/v1/restaurants/{restaurantId:guid}/menu-items/{menuItemId:guid}/confirm-image`
- **Handler logic**:
  1. Load `MenuItem`; throw `NotFoundException` if not found.
  2. Verify `ImageUploadStatus == Pending`; throw `DomainException` otherwise.
  3. Call `IObjectStorageService.ObjectExistsAsync(PendingImageKey)` — throw `DomainException` if object not found in bucket (client lied about uploading).
  4. Set `ImageUrl = IObjectStorageService.GetPublicUrl(PendingImageKey)`.
  5. Queue object key for async processing (thumbnail generation).
  6. Clear `PendingImageKey`, set `ImageUploadStatus = Confirmed`.
  7. Call `ICatalogCache.InvalidateMenuAsync(restaurantId)`.
  8. Publish `MenuItemImageUploadedIntegrationEvent` via `IOutboxPublisher`.
  9. `SaveChangesAsync`.

### 6.6 FluentValidation — `ImageUrl` Fix (Existing Handler)

> [!CAUTION]
> `CreateMenuItemCommandValidator` currently has **no `ImageUrl` rule**. Empty strings pass validation and are persisted to the DB (`NOT NULL` column). This must be fixed in Phase 2 before the new upload flow is built.

The existing validator in [`CreateMenuItemHandler.cs`](file:///C:/Users/omar_/Source/Repos/kalaa/orderly/OrderlyMicroservices/orderly-microservices/Services/Catalog/Catalog.API/Features/MenuItems/CreateMenuItem/CreateMenuItemHandler.cs) must be extended:

```csharp
// Add to CreateMenuItemCommandValidator constructor:
RuleFor(x => x.ImageUrl)
    .NotEmpty().WithMessage("ImageUrl is required.")
    .MaximumLength(500).WithMessage("ImageUrl must not exceed 500 characters.")
    .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
    .WithMessage("ImageUrl must be a valid absolute URL.");

// Same rule must be added to UpdateMenuItemCommandValidator.
```

> Once the presigned upload flow is live (Phase 4), `ImageUrl` on `CreateMenuItemCommand` becomes **optional** (nullable `string?`) because items are created first and images uploaded separately. This change must be coordinated between Phase 2 and Phase 4.

---

## 7. Integration Points

### 7.1 Outbox Integration

`ConfirmMenuItemImageUpload` handler publishes a `MenuItemImageUploadedIntegrationEvent` via the existing `IOutboxPublisher` infrastructure — consistent with how `CreateMenuItemCommandHandler` already publishes `MenuItemChangedIntegrationEvent`. This allows downstream consumers (e.g., a future search indexer) to react to image changes.

### 7.2 Cache Invalidation

Both `GetMenuItemImageUploadUrl` and `ConfirmMenuItemImageUpload` must call `ICatalogCache.InvalidateMenuAsync(restaurantId)` after mutations — consistent with all other `MenuItem` mutation handlers.

### 7.3 Basket Snapshot on Checkout

When `BasketCheckoutEventHandler` in `Ordering.Application` maps basket items to `OrderItem`, `MenuItemImageUrl` must be populated from the basket item snapshot. Currently `BasketCheckoutEventHandler` sets it to `string.Empty`. Once `BasketItem` gains the field, this handler must be updated to forward it.

---

## 8. Security guardrails

> [!CAUTION]
> Presigned URL endpoints must be protected by restaurant-scoped authorization. A restaurant admin must not be able to generate upload tokens for another restaurant's menu items.

| Risk | Mitigation |
|---|---|
| Unrestricted file type upload | Presigned URL policy enforces `Content-Type` to one of `image/jpeg`, `image/png`, `image/webp`. Validated by FluentValidation before the SDK call. |
| Storage exhaustion / DoS | Presigned URLs expire in 15 minutes (`PresignedUrlExpiryMinutes`). Max upload size: 5MB (`MaxUploadBytes`), enforced at the policy level — not by a middleware. |
| Cross-restaurant upload abuse | `GetMenuItemImageUploadUrl` loads the `MenuItem` by both `menuItemId` AND `restaurantId` from the route — a menu item belonging to a different restaurant returns 404. |
| Unverified upload activation | `ConfirmMenuItemImageUpload` calls `IObjectStorageService.ObjectExistsAsync` before activating the URL — a client cannot fake a confirmation without having actually uploaded to the bucket. |
| Public bucket tampering | The bucket remains private. `GetPublicUrl` returns CDN-routed URLs, not direct bucket URLs. |
| Stale presigned keys | `PendingImageKey` is overwritten each time `GetMenuItemImageUploadUrl` is called, and cleared on confirm — no leaked keys accumulate on the entity. |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Key deliverables | Goal |
|:---:|---|---|---|
| **1** | Cloud Provider Evaluation | ADR file, enriched comparison matrix | Choose provider; produce Architecture Decision Record. |
| **2** | Image Schema & Data Fields | EF Core migration, BasketItem field, validator fix | All image fields are present and validated correctly. |
| **3** | Storage Abstraction & Local Dev | `IObjectStorageService`, `ObjectStorageSettings`, MinIO in docker-compose | Storage layer is injectable and locally runnable. |
| **4** | Upload Workflow & Presigned API | Two Carter feature slices, state machine, outbox event | Full two-step upload flow works end-to-end against MinIO. |
| **5** | Processing Pipeline & CDN Delivery | `BackgroundService`, ImageSharp thumbnails, CDN headers | Uploaded images auto-generate WebP variants; served via CDN. |

---

### Phase 1 — Cloud Provider Evaluation (AWS vs Azure vs Cloudflare)

**Goal**: Produce a formal Architecture Decision Record (ADR) comparing AWS S3 + CloudFront, Azure Blob Storage + Front Door, and Cloudflare R2 + Cloudflare Images. The ADR becomes the source of truth for which provider's SDK is used in Phase 3.

**Status**: ⏸ Pending

**Deliverables**:
- [ ] Evaluate **AWS (S3 + CloudFront)**: Industry standard; most mature .NET SDK (`AWSSDK.S3`); high egress without CloudFront; Lambda required for image processing.
- [ ] Evaluate **Azure (Blob Storage + CDN/Front Door)**: Native fit for AKS/Azure deployments; excellent .NET SDK (`Azure.Storage.Blobs`); moderate egress; Azure Functions for processing; separate SDK from S3 — requires its own `IObjectStorageService` implementation.
- [ ] Evaluate **Cloudflare (R2 + Cloudflare Images)**: Zero egress fees; built-in image resize/transform API (no processing worker needed if using Cloudflare Images); uses S3-compatible API — same `AWSSDK.S3` SDK as AWS/MinIO, no extra adapter needed; fastest growing edge network.
- [ ] Add `.NET SDK quality` row to §9 comparison matrix.
- [ ] Produce an ADR file at `docs/architecture/decisions/ADR-IMAGE-STORAGE-PROVIDER.md`.
- [ ] Update §4 Tech Decisions table with the chosen provider and rationale.

**Exit criteria**: ADR file committed with a formal provider recommendation. §4 and §9 of this plan updated accordingly.

---

### Phase 2 — Image Schema & Data Fields Across Microservices

**Goal**: Ensure all image-related fields exist in the correct models, are validated, and are covered by EF Core migrations. This phase fixes the existing empty-`ImageUrl` validator gap and adds all missing fields.

**Status**: 🔒 Blocked (blocked on Phase 1 only for `LogoUrl`/`BannerUrl` confirmation; other items can proceed)

**Deliverables**:
- [ ] **Fix `CreateMenuItemCommandValidator`**: Add `ImageUrl` rules (NotEmpty, MaxLength(500), valid absolute URL). Same fix for `UpdateMenuItemCommandValidator`.
- [ ] **Add `ThumbnailUrl string?`** to `MenuItem` model and EF Core config (`HasMaxLength(500)`). Run `dotnet ef migrations add AddMenuItemThumbnailUrl -p Catalog.API`.
- [ ] **Add `PendingImageKey string?`** to `MenuItem` model. Run migration.
- [ ] **Add `ImageUploadStatus` enum** to `BuildingBlocks.Enums` and property to `MenuItem`. Run migration.
- [ ] **Add `MenuItemImageUrl string`** to `Basket.API`'s `BasketItem` model. No migration needed (Marten document store), but default deserialization must handle existing documents that lack the field (empty string default).
- [ ] **Verify `Restaurant` model** for `LogoUrl` and `BannerUrl` columns. Add and migrate if missing.
- [ ] **Update `BasketCheckoutEventHandler`** in `Ordering.Application` to forward `MenuItemImageUrl` from the basket item (currently hardcoded to `string.Empty`).

**Exit criteria**: `pwsh ./scripts/phase-guard.ps1 -PhaseName "Phase 2" -Quick` passes. A `POST /api/v1/restaurants/{id}/menu-items` request with an empty `ImageUrl` now returns HTTP 400.

---

### Phase 3 — Storage Abstraction Layer & Local Dev Setup

**Goal**: Introduce the `IObjectStorageService` interface and `ObjectStorageSettings` into `BuildingBlocks`, implement `S3ObjectStorageService`, and provision MinIO in `docker-compose.yml`. Phases 2 and 3 can run in **parallel** since they have no dependency on each other.

**Status**: 🔒 Blocked (blocked on Phase 1 — provider choice determines which SDK NuGet package to add)

**Deliverables**:
- [ ] Add `BuildingBlocks/Storage/IObjectStorageService.cs` (see §6.3).
- [ ] Add `BuildingBlocks/Storage/ObjectStorageSettings.cs` (see §6.2).
- [ ] Add `ImageUploadStatus` enum to `BuildingBlocks.Enums` (see §6.4).
- [ ] Add `AWSSDK.S3` NuGet to `Catalog.API.csproj` (covers MinIO, AWS, and Cloudflare R2).
- [ ] Implement `Catalog.API/Infrastructure/Storage/S3ObjectStorageService.cs` implementing `IObjectStorageService`.
- [ ] Register `IObjectStorageService` in `Catalog.API/Program.cs` via `builder.Services.AddSingleton<IObjectStorageService, S3ObjectStorageService>()`.
- [ ] Register `ObjectStorageSettings` via `builder.Services.Configure<ObjectStorageSettings>(builder.Configuration.GetSection(ObjectStorageSettings.SectionName))`.
- [ ] Add `minio` service to `docker-compose.yml` with a healthcheck.
- [ ] Add `minio-init` one-shot service (or `mc` command) to auto-create `orderly-restaurant-assets` bucket on first start.
- [ ] Add `ObjectStorage` section to `appsettings.Development.json` pointing to `http://minio:9000`.

**Exit criteria**: `docker-compose up -d minio` starts cleanly. `docker-compose up -d catalog.api` boots without errors. A manual call to `IObjectStorageService.GeneratePresignedUploadUrlAsync` in a test or scratch endpoint returns a valid presigned URL against the local MinIO instance.

---

### Phase 4 — Upload Workflow & Presigned URL API (Catalog Service)

**Goal**: Implement the two-step upload flow — presigned URL generation and upload confirmation — as two Carter vertical slices in `Catalog.API/Features/Media/`.

**Status**: 🔒 Blocked (blocked on Phase 3)

**Deliverables**:
- [ ] Create `Features/Media/GetMenuItemImageUploadUrl/GetMenuItemImageUploadUrlEndpoint.cs` — Carter `ICarterModule`, `POST /api/v1/restaurants/{restaurantId:guid}/menu-items/{menuItemId:guid}/image-upload-url`.
- [ ] Create `Features/Media/GetMenuItemImageUploadUrl/GetMenuItemImageUploadUrlHandler.cs` — MediatR `ICommand<GetMenuItemImageUploadUrlResult>` (see §6.5 for full handler logic).
- [ ] Create `Features/Media/ConfirmMenuItemImageUpload/ConfirmMenuItemImageUploadEndpoint.cs` — Carter `ICarterModule`, `POST /api/v1/restaurants/{restaurantId:guid}/menu-items/{menuItemId:guid}/confirm-image`.
- [ ] Create `Features/Media/ConfirmMenuItemImageUpload/ConfirmMenuItemImageUploadHandler.cs` — calls `IObjectStorageService.ObjectExistsAsync`, sets `ImageUrl`, clears `PendingImageKey`, sets `ImageUploadStatus = Confirmed`, invalidates cache, publishes `MenuItemImageUploadedIntegrationEvent` via `IOutboxPublisher`.
- [ ] Define `MenuItemImageUploadedIntegrationEvent` in `BuildingBlocks.Messaging.Events`.
- [ ] Add FluentValidation rules: `ContentType` must be one of `image/jpeg`, `image/png`, `image/webp`.
- [ ] Make `ImageUrl` optional (`string?`) on `CreateMenuItemCommand` now that images are uploaded separately. Update `CreateMenuItemCommandValidator` accordingly (remove the URL format rule added in Phase 2; keep it for `UpdateMenuItem` which may still accept a direct URL for migration/legacy paths).
- [ ] Update `YarpApiGateway` route config if upload endpoints need to be proxied.

**Exit criteria**: End-to-end flow works against local MinIO: `POST image-upload-url` → client uploads to the presigned URL → `POST confirm-image` → `MenuItem.ImageUrl` reflects the CDN URL. `MenuItemImageUploadedIntegrationEvent` appears in the outbox table.

---

### Phase 5 — Asynchronous Image Processing & CDN Delivery

**Goal**: Implement an in-process `BackgroundService` inside `Catalog.API` that picks up confirmed uploads, resizes them using `SixLabors.ImageSharp`, stores WebP variants back into the object store, and updates `ThumbnailUrl`.

**Status**: 🔒 Blocked (blocked on Phase 4)

**Deliverables**:
- [ ] Add `SixLabors.ImageSharp` NuGet to `Catalog.API.csproj`.
- [ ] Implement `Catalog.API/Scheduling/ImageProcessingBackgroundService.cs` as `BackgroundService`. Uses `ExecuteAsync(CancellationToken stoppingToken)` with a polling loop (or channel/queue fed by `MenuItemImageUploadedIntegrationEvent` consumer).
- [ ] Processing steps per image: download original from object store → resize to 800×800 standard → resize to 150×150 thumbnail → encode both as WebP → upload back to bucket with suffix (`-standard.webp`, `-thumb.webp`) → update `MenuItem.ImageUrl` (standard) and `ThumbnailUrl` (thumb) → `SaveChangesAsync` → invalidate cache.
- [ ] Configure CDN/bucket object headers: `Cache-Control: public, max-age=31536000, immutable` on all processed variants.
- [ ] Register `BackgroundService` in `Program.cs` via `builder.Services.AddHostedService<ImageProcessingBackgroundService>()`.

**Exit criteria**: After confirming an upload, within a configurable polling window, `MenuItem.ThumbnailUrl` is set and a WebP variant is accessible via the CDN URL.

---

## 10. Technical Considerations

### 10.1 Cross-cutting

**`BasketItem` Marten schema migration** — Marten is a document store; adding a new field (`MenuItemImageUrl`) to `BasketItem` does not require a SQL migration, but all pre-existing basket documents in PostgreSQL lack the property. The `BasketItem` class must initialize `MenuItemImageUrl = string.Empty` so `JsonSerializer` deserializes old documents cleanly without null-reference exceptions.

**`ImageUrl` optionality transition** — Phase 2 adds a hard validation rule (non-empty valid URL) to `CreateMenuItemCommand`. Phase 4 relaxes `ImageUrl` to optional to support the presigned flow. These two changes must be coordinated in the same or consecutive commits — avoid a window where both constraints are active simultaneously.

**`BackgroundService` error handling** — `ImageProcessingBackgroundService.ExecuteAsync` must catch and log exceptions per iteration rather than letting them bubble up and crash the host. Use `ILogger<T>` structured logging for each processing failure. A dead-letter strategy (retry count cap, mark `MenuItem` with `ImageUploadStatus = ProcessingFailed`) should be considered during Phase 5 implementation.

**Presigned URL `Content-Type` enforcement** — The AWS S3 SDK presigned URL includes the `Content-Type` in the signature. The client **must** send the exact `Content-Type` header it declared when requesting the URL, or the upload will be rejected by the bucket. This must be documented in the API response (`ContentType` field echoed back) and enforced in the confirm handler (verify content-type header matches expected).

### 10.2 Cloud Provider Comparison Matrix

| Criteria | AWS (S3 + CloudFront) | Azure (Blob + Front Door) | Cloudflare (R2 + Images) |
| :--- | :--- | :--- | :--- |
| **Egress Costs** | High (~$0.09/GB) | High (~$0.087/GB) | **Zero ($0/GB)** |
| **Storage Cost** | ~$0.023/GB | ~$0.018/GB | **~$0.015/GB** |
| **Image Processing** | S3 Event → Lambda → Sharp | Blob Trigger → Azure Functions | **Built-in Cloudflare Images API** (Phase 5 may be skippable) |
| **Local Dev Compatibility** | Excellent (MinIO / LocalStack) | Good (Azurite / MinIO) | Excellent (MinIO S3 API) |
| **Developer Ecosystem** | Dominant standard | Strong for .NET Enterprise | Fastest-growing edge platform |
| **.NET SDK Quality** | **Best** (`AWSSDK.S3` — most mature, most examples) | Good (`Azure.Storage.Blobs` — verbose but complete) | **Reuses AWS SDK** (`AWSSDK.S3` with custom endpoint — no separate SDK) |

---

## How to use this template

Refer to `.agents/plan/_template.md` for execution guidelines.

---

## Changelog

### v1.1 (2026-08-06) — post-analysis revision
- Applied all findings from codebase analysis (C# skills + live code review).
- **Critical fixes**: Corrected Phase 2 `BasketItem` task from "verify" to "add" (field does not exist). Added missing `IObjectStorageService` abstraction deliverable to Phase 3. Added `ObjectStorageSettings` spec to §6.2. Added `ImageUrl` FluentValidation fix to Phase 2. Named all Carter endpoint + handler files in §5 and §6.5.
- **New sections**: §6.2 `ObjectStorageSettings`, §6.3 `IObjectStorageService`, §6.4 `ImageUploadStatus` enum, §6.5 Media vertical slice detail, §6.6 validator fix, §7 Integration Points.
- **Phase detail expanded**: All phases now have explicit file names, route paths, handler logic steps, and migration commands.
- **Tech decisions updated**: `SixLabors.ImageSharp` chosen over `Magick.NET` (rationale added). Processing host decided as in-process `BackgroundService`. Upload state machine (`ImageUploadStatus`) added.
- **§9 comparison matrix**: Added `.NET SDK quality` row.
- Phase 3 renamed to "Storage Abstraction Layer & Local Dev Setup".
- Noted that Phases 2 and 3 can run in parallel.

### v1.0 (2026-08-06) — initial draft
- Created implementation plan for Restaurant Image Storage & Lifecycle.
- Added cloud provider comparison phase (AWS vs Azure vs Cloudflare).
- Defined microservice field requirements and MinIO local dev strategy.
