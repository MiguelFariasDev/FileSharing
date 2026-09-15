# syntax=docker/dockerfile:1
#
# Build context MUST be the repository root (not this directory) — FileSharing.Api references
# sibling projects (Domain/Application/Infrastructure) via ProjectReference, so the Dockerfile
# needs their .csproj/source files too:
#
#   docker build -f src/FileSharing.Api/Dockerfile -t filesharing-api .
#
# (docker-compose.yml already sets `context: ../..` + this dockerfile path — see
# infrastructure/docker/docker-compose.yml — so `docker compose build api` just works.)

# ---- build stage: SDK only, never shipped ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Copy csproj files first (and nothing else) so `dotnet restore` is cached across builds unless
# a dependency actually changed — the most expensive step, and the one that changes least often.
COPY Directory.Packages.props ./
COPY src/FileSharing.Domain/FileSharing.Domain.csproj src/FileSharing.Domain/
COPY src/FileSharing.Application/FileSharing.Application.csproj src/FileSharing.Application/
COPY src/FileSharing.Infrastructure/FileSharing.Infrastructure.csproj src/FileSharing.Infrastructure/
COPY src/FileSharing.Api/FileSharing.Api.csproj src/FileSharing.Api/
RUN dotnet restore src/FileSharing.Api/FileSharing.Api.csproj

# Now the rest of the source for just the projects this API actually needs — never Mobile/Web/
# tests, which would invalidate this layer's cache on every unrelated change and (for Mobile)
# require Android workloads this stage doesn't have.
COPY src/FileSharing.Domain/ src/FileSharing.Domain/
COPY src/FileSharing.Application/ src/FileSharing.Application/
COPY src/FileSharing.Infrastructure/ src/FileSharing.Infrastructure/
COPY src/FileSharing.Api/ src/FileSharing.Api/

RUN dotnet build src/FileSharing.Api/FileSharing.Api.csproj -c Release --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish src/FileSharing.Api/FileSharing.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- runtime stage: ASP.NET runtime only, no SDK, no source ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# curl only exists to back HEALTHCHECK below (both Docker Compose's own check and, later, an ECS
# Task Definition healthCheck block run a command *inside* the container — unlike an ALB target
# group health check, which probes from outside and needs nothing installed here at all).
# A few MB, accepted deliberately for a working, standard in-container probe rather than
# reimplementing an HTTP client in a shell script.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# The official ASP.NET Core runtime image already ships a low-privilege "app" user (not root)
# since .NET 8 — this just makes that explicit instead of relying on an implicit default that
# could silently change with a future base-image update.
USER app

COPY --from=publish /app/publish .

# ASPNETCORE_URLS is intentionally NOT hardcoded here — it's set via environment variable at
# `docker run`/ECS Task Definition time (see docker-compose.yml/docs/deployment.md), so the same
# image works whether the platform expects 8080 (Docker Compose/ECS convention below) or a
# different port assigned by the orchestrator.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Liveness only (never /health/ready — see Program.cs's own remarks: Docker/ECS should restart a
# stuck process, never a process that's merely waiting on a slow/down PostgreSQL or S3).
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "FileSharing.Api.dll"]
