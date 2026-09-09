# FileSharing - Claude Code Instructions

## Project Overview

FileSharing is a secure temporary file-sharing platform.

The system allows authenticated users to upload files from an Android .NET MAUI application, store them privately in Amazon S3, generate temporary public sharing links, and receive real-time notifications when files are downloaded.

Every file expires exactly 24 hours after upload.

The project is intended to demonstrate production-oriented software engineering using C#, .NET, AWS, distributed systems and secure file handling.

---

# Technology Stack

## Backend

- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- Npgsql
- JWT Bearer Authentication
- SignalR
- Hangfire
- Serilog

## Mobile

- .NET MAUI
- Android
- C#
- CommunityToolkit.Mvvm
- HttpClient
- IProgress<T>

## Web

- Blazor
- C#
- SignalR

## Cloud

- AWS ECS Fargate
- AWS RDS PostgreSQL
- AWS S3
- AWS Secrets Manager
- AWS Application Load Balancer
- AWS CloudWatch

## Development

- Docker Compose
- PostgreSQL
- LocalStack
- GitHub Actions

---

# Architecture

The backend follows Clean Architecture.

Dependency direction:

```text
Domain
   ↑
Application
   ↑
Infrastructure
   ↑
API
```

Rules:

- `Domain` has zero dependencies on any other project. It contains entities, value objects, domain enums, and domain exceptions only.
- `Application` depends only on `Domain`. It contains use cases (commands/queries via MediatR), interfaces for infrastructure concerns (`IFileStorageService`, `IFileRepository`, `INotificationService`), DTOs, and validation (FluentValidation).
- `Infrastructure` depends on `Application` and `Domain`. It implements the interfaces defined in `Application`: EF Core `DbContext`, repositories, the S3 client wrapper, Hangfire job implementations, SignalR hub implementation details that touch persistence.
- `API` depends on all layers but should be a thin composition layer: controllers/minimal API endpoints, dependency injection wiring, middleware, SignalR hub registration, Swagger configuration.
- Never let `Domain` or `Application` reference `Microsoft.AspNetCore.*`, `AWSSDK.*`, or `Npgsql` directly. Those belong to `Infrastructure`/`API`.

## Solution structure

```text
FileSharing.sln
src/
  FileSharing.Domain/
  FileSharing.Application/
  FileSharing.Infrastructure/
  FileSharing.Api/
  FileSharing.Web/              # Blazor Server project
  FileSharing.Mobile/           # .NET MAUI project (Android target)
  FileSharing.Shared/           # DTOs/contracts shared between Api, Web, Mobile
tests/
  FileSharing.Domain.Tests/
  FileSharing.Application.Tests/
  FileSharing.Api.IntegrationTests/
docker-compose.yml
.github/workflows/
```

`FileSharing.Shared` holds request/response contracts (e.g. `UploadRequestDto`, `FileStatusDto`, `DownloadNotifiedEvent`) referenced by `Api`, `Web`, and `Mobile` so the three clients never drift from the API contract.

---

# Domain Model

Core entity: `SharedFile`

```text
SharedFile
- Id: Guid
- OwnerId: Guid
- ShareToken: string       // non-sequential, high-entropy, used in the public link
- S3Key: string
- OriginalFileName: string
- ContentType: string
- SizeInBytes: long
- UploadedAtUtc: DateTime
- ExpiresAtUtc: DateTime   // always UploadedAtUtc + 24h
- Status: FileStatus       // Active | Expired | Deleted
```

`FileDownloadEvent`

```text
FileDownloadEvent
- Id: Guid
- SharedFileId: Guid
- DownloadedAtUtc: DateTime
- IpAddress: string
- UserAgent: string
```

Domain rules that belong in `Domain`/`Application`, not in controllers:

- `ExpiresAtUtc` is always computed as `UploadedAtUtc.AddHours(24)`; it is never client-supplied.
- A `SharedFile` is only downloadable when `Status == Active` and `DateTime.UtcNow < ExpiresAtUtc`.
- `ShareToken` generation must use a cryptographically secure random generator (`RandomNumberGenerator`), never `Guid.NewGuid()` alone truncated, and never a sequential/incrementing id.
- Accessing an expired or unknown token must return the same generic "not available" response — do not leak whether a token ever existed.

---

# API Endpoints (guideline, adjust as implemented)

```text
POST   /api/auth/register
POST   /api/auth/login
POST   /api/files/upload-url        -> returns presigned PUT URL + SharedFile record (Pending)
POST   /api/files/{id}/confirm      -> marks upload as completed after client finishes S3 PUT
GET    /api/files/mine              -> list of the authenticated user's shared files + status
GET    /s/{shareToken}              -> public endpoint, validates token/expiry, returns presigned GET URL
GET    /api/files/{id}/downloads    -> download history for a file (owner only)
```

Hub:

```text
/hubs/notifications                 -> SignalR hub, one group per OwnerId (or per SharedFileId)
```

---

# SignalR Notification Flow

1. When the Blazor client loads the sender's dashboard, it connects to `/hubs/notifications` and joins a group named after the authenticated `OwnerId`.
2. When `GET /s/{shareToken}` is called successfully and the presigned download URL is issued, the API records a `FileDownloadEvent` and then calls `IHubContext<NotificationHub>.Clients.Group(ownerId).SendAsync("FileDownloaded", payload)`.
3. `payload` should include: `sharedFileId`, `originalFileName`, `downloadedAtUtc`, and a masked/partial IP if you want to show it in the UI.
4. The Blazor dashboard listens for `FileDownloaded` and updates the relevant row's status/toast without a page refresh.
5. Configure automatic reconnection on the client (`.withAutomaticReconnect()` for the JS client, or the equivalent Blazor Server built-in circuit resilience) — do not assume the connection stays alive indefinitely.

Do not couple the hub directly to EF Core — the hub should only push already-prepared DTOs, and persistence should happen in the Application layer (a `DownloadFileCommand` handler), not inside hub methods.

---

# Background Jobs (Hangfire)

Recurring job `ExpireFilesJob`, scheduled every 15 minutes:

1. Query `SharedFile` where `Status == Active AND ExpiresAtUtc <= UtcNow`.
2. For each match: delete the object from S3 via `IFileStorageService.DeleteAsync(s3Key)`, then set `Status = Expired` and persist.
3. The job must be idempotent: if the S3 object is already gone (e.g. previous run partially succeeded), treat a "not found" delete response as success, not as an error, and still mark the record `Expired`.
4. Wrap the loop so that a failure on one file does not abort processing of the rest; log failures with Serilog and let Hangfire retry only the job trigger, not silently swallow individual item errors.
5. Expose the Hangfire Dashboard only behind authentication in non-local environments (do not leave `/hangfire` open in production).

---

# Security Requirements

- `ShareToken` must have at least 128 bits of entropy (e.g. 22+ base62 characters or a GUID without dashes), generated server-side only.
- Presigned S3 URLs: upload URLs and download URLs must both be short-lived (a few minutes), never the long 24h file lifetime.
- Rate limit the `GET /s/{shareToken}` endpoint (`Microsoft.AspNetCore.RateLimiting`) to slow down brute-force token guessing.
- Validate `ContentType` and enforce a maximum file size before issuing a presigned upload URL.
- S3 bucket must have public access blocked at the bucket level; all access happens exclusively through presigned URLs generated by the API.
- Secrets (DB connection string, JWT signing key, AWS credentials) come from AWS Secrets Manager in deployed environments and from `.env`/user-secrets locally — never commit them.
- JWT tokens: short-lived access token + refresh token flow; do not put file data or S3 keys inside the JWT payload.

---

# Testing Strategy

- `FileSharing.Domain.Tests`: pure unit tests for entity invariants (expiry calculation, status transitions) with xUnit, no mocks needed.
- `FileSharing.Application.Tests`: xUnit + Moq for command/query handlers, mocking `IFileStorageService`, `IFileRepository`, `IHubNotifier`.
- `FileSharing.Api.IntegrationTests`: `WebApplicationFactory` with a real Postgres via Testcontainers (or LocalStack for S3-dependent flows) — at minimum cover: upload flow end-to-end, expired-token access returns generic not-found, and the Hangfire expiration job actually deletes and updates status.
- Never write tests that assert on Hangfire's internal scheduling; test the job's `Execute` method directly as a plain class.

---

# Development Workflow

- Local development runs via `docker-compose up`: API + PostgreSQL + LocalStack (S3 emulation). Point `AWSSDK.S3` at the LocalStack endpoint using an `IOptions<S3Settings>` override in the `Development` environment.
- Use EF Core migrations (`dotnet ef migrations add <Name> -p src/FileSharing.Infrastructure -s src/FileSharing.Api`) — never hand-edit the database schema.
- Run `dotnet format` before committing; treat analyzer warnings from `Directory.Build.props` as build-breaking in CI.
- The Android MAUI project should be built and tested against a local emulator during development; do not attempt an iOS build target for now.

## GitHub Actions

Two independent workflows:

1. `api-web-ci.yml`: restore, build, run `Domain.Tests`, `Application.Tests`, `Api.IntegrationTests`, then build Docker image, push to a registry, deploy to ECS Fargate on merge to `main`.
2. `mobile-android-ci.yml`: restore, `dotnet build -f net8.0-android` (or the MAUI Android TFM in use), run any Mobile unit tests that don't require a device/emulator. Do not attempt iOS builds in this workflow.

---

# Conventions

- Namespaces mirror folder structure exactly, matching project name (`FileSharing.Application.Files.Commands.UploadFile`, etc.).
- Use MediatR: one command/query per use case, one handler per command/query, thin controllers that only map HTTP to MediatR calls.
- Use FluentValidation validators registered as MediatR pipeline behaviors, not manual `if` checks inside handlers.
- All datetimes are stored and compared in UTC; never use `DateTime.Now` in Domain or Application code.
- Prefer `Result<T>`-style outcomes (or exceptions mapped centrally in middleware) over ad-hoc `null` returns for expected failure cases (e.g. expired token, file not found).

---

# What Claude Code should NOT do here

- Do not generate an iOS build pipeline or iOS-specific MAUI code at this stage — the mobile scope for this iteration is Android only.
- Do not implement file storage using local disk or any provider other than S3/LocalStack, even for "quick" prototypes.
- Do not put S3, EF Core, or SignalR hub context types inside `Domain` or `Application` — always go through the interfaces defined there.
- Do not make `ExpiresAtUtc` configurable per upload; it must always be a fixed 24 hours from `UploadedAtUtc` for this version of the project.