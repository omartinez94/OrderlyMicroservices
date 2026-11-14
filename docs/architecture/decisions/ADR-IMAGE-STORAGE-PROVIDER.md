# ADR: Object Storage and CDN Provider Selection for Restaurant Assets

## Context

Orderly microservices require persistent storage and public edge delivery for restaurant assets, including menu item imagery, category banners, and store logos. These assets exhibit two distinct traffic patterns:
1. **Infrequent writes**: Uploaded by restaurant managers and admins via direct presigned URLs (bypassing `YarpApiGateway` and service memory).
2. **High-frequency public reads**: Consumed continuously by client web applications (`OrderlyWeb`), KDS terminals, and mobile digital menus.

We evaluated three potential cloud storage architectures:
1. **AWS S3 + Amazon CloudFront**
2. **Azure Blob Storage + Azure Front Door**
3. **Cloudflare R2 + Cloudflare CDN**

## Decision

We adopt **Cloudflare R2** for production object storage and edge delivery, paired with **MinIO** for local development, integrated via the official **`AWSSDK.S3`** .NET SDK inside `BuildingBlocks`.

## Considered Options & Trade-Off Matrix

| Criteria | AWS (S3 + CloudFront) | Azure (Blob + Front Door) | Cloudflare (R2 + CDN) [Selected] |
| :--- | :--- | :--- | :--- |
| **Egress Cost** | High (~$0.09/GB) | High (~$0.087/GB) | **$0.00 / GB (Zero Egress)** |
| **Storage Cost** | ~$0.023 / GB / month | ~$0.018 / GB / month | **~$0.015 / GB / month** |
| **API Protocol** | S3 API (Defacto standard) | Azure Blob REST API | **S3-compatible API** |
| **.NET SDK** | `AWSSDK.S3` (Mature, robust) | `Azure.Storage.Blobs` | **Reuses `AWSSDK.S3`** with custom endpoint |
| **Local Dev Parity** | MinIO / LocalStack | Azurite | **MinIO (Identical S3 API & SDK)** |

## Rationale

1. **Zero Egress Fee Economics**: Food imagery and digital menu boards generate thousands of edge requests per active restaurant daily. Cloudflare R2 charges zero data egress fees, completely removing bandwidth penalties as tenant scale expands.
2. **Zero Code Divergence via S3 Protocol**: Because R2 adheres to the AWS S3 API standard, the exact same `AWSSDK.S3` client logic in `BuildingBlocks` connects to:
   - `http://localhost:9000` (Local MinIO container in `docker-compose.yml`)
   - `https://<accountId>.r2.cloudflarestorage.com` (Cloudflare R2 in Staging / Production)
   - Standard AWS S3 if an enterprise client ever mandates AWS-hosted storage.
3. **Presigned PUT URL Parity**: Direct client-to-storage presigned upload signatures follow standard SigV4 semantics on both MinIO and R2, enabling identical validation and upload behavior in local testing and production.

## Consequences

- **Positive**:
  - No dual-SDK maintenance: One interface (`IObjectStorageService`) and one implementation (`S3ObjectStorageService`).
  - No cloud dependency for local development: Local developers run with MinIO out of the box.
  - Predictable, low cost at scale.
- **Negative / Mitigations**:
  - Cloudflare R2 requires configuring an account-specific endpoint URL (`https://<accountId>.r2.cloudflarestorage.com`) in `ObjectStorageSettings`. This is easily handled via standard environment variables / `appsettings.json`.
