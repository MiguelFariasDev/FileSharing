# syntax=docker/dockerfile:1
#
# Build context MUST be the repository root:
#
#   docker build -f src/FileSharing.Web/Dockerfile -t filesharing-web .
#
# (docker-compose.yml already sets this up — `docker compose build web` just works.)

# ---- build stage: SDK only, never shipped ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY Directory.Packages.props ./
COPY src/FileSharing.Domain/FileSharing.Domain.csproj src/FileSharing.Domain/
COPY src/FileSharing.Application/FileSharing.Application.csproj src/FileSharing.Application/
COPY src/FileSharing.Web/FileSharing.Web.csproj src/FileSharing.Web/
RUN dotnet restore src/FileSharing.Web/FileSharing.Web.csproj

# Only what FileSharing.Web actually references — never Api/Infrastructure/Mobile/tests, so
# unrelated changes elsewhere in the repo never invalidate this layer's cache, and this stage
# never needs Android workloads.
COPY src/FileSharing.Domain/ src/FileSharing.Domain/
COPY src/FileSharing.Application/ src/FileSharing.Application/
COPY src/FileSharing.Web/ src/FileSharing.Web/

RUN dotnet build src/FileSharing.Web/FileSharing.Web.csproj -c Release --no-restore -o /app/build

FROM build AS publish
RUN dotnet publish src/FileSharing.Web/FileSharing.Web.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- runtime stage: ASP.NET runtime only, no SDK, no source ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# See FileSharing.Api/Dockerfile's identical remark — curl only backs HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Already the default non-root user on this base image since .NET 8 — stated explicitly rather
# than relied upon implicitly.
USER app

COPY --from=publish /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "FileSharing.Web.dll"]
