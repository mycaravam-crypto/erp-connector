# Base images pinned to a digest, not just a mutable tag — a repointed tag (registry compromise
# or upstream mistake) would otherwise change what gets built with no detection. Update by
# re-resolving the tag's current digest (e.g. `docker buildx imagetools inspect <image>:<tag>`),
# not by hand.

# ── Stage 1: Build the Vue UI ─────────────────────────────────────────────────
FROM node:24-alpine@sha256:e67514e5d0f6c46656005e1b693b2ec9d52e80b641307de684d4a015ba7a4eaf AS ui-build
WORKDIR /app/ui
COPY src/connector-ui/package*.json ./
RUN npm ci --prefer-offline
COPY src/connector-ui/ ./
RUN npm run build-only

# ── Stage 2: Build the .NET API ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine@sha256:730abfea9d28f7d643bc29857363b35ddff06d8f22a388d912acfba5bf78fab2 AS api-build
WORKDIR /app
COPY Directory.Build.props ./
COPY src/ ./src/
COPY tests/ ./tests/
RUN dotnet restore src/Connector.Api/Connector.Api.csproj
RUN dotnet publish src/Connector.Api/Connector.Api.csproj \
    -c Release \
    -o /publish \
    --no-restore \
    --self-contained false

# ── Stage 3: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine@sha256:7e9c4b5dd81f7f319c91f54e490e83d0f6c9a62686d1d1418ce492d6f961828d AS runtime
WORKDIR /app

# Copy published API
COPY --from=api-build /publish ./

# Copy compiled UI into the static-files directory served by the API
# (adjust the path if you add a StaticFiles middleware or reverse proxy)
COPY --from=ui-build /app/ui/dist ./wwwroot

# Non-root user for least-privilege execution
RUN addgroup -S connector && adduser -S connector -G connector

# Create data directories with correct ownership before declaring them as volumes.
# Docker initialises named volumes from the image content at these paths, so the
# ownership must be set here — not after the VOLUME instruction.
RUN mkdir -p /data/db /data/staging && chown -R connector:connector /data

USER connector

VOLUME ["/data/db", "/data/staging"]

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Connector.Api.dll"]
