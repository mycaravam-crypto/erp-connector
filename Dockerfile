# Base images pinned to a digest, not just a mutable tag — a repointed tag (registry compromise
# or upstream mistake) would otherwise change what gets built with no detection. Update by
# re-resolving the tag's current digest (e.g. `docker buildx imagetools inspect <image>:<tag>`),
# not by hand.

# ── Stage 1: Build the Vue UI ─────────────────────────────────────────────────
FROM node:25-alpine@sha256:bdf2cca6fe3dabd014ea60163eca3f0f7015fbd5c7ee1b0e9ccb4ced6eb02ef4 AS ui-build
WORKDIR /app/ui
COPY src/connector-ui/package*.json ./
RUN npm ci --prefer-offline
COPY src/connector-ui/ ./
RUN npm run build-only

# ── Stage 2: Build the .NET API ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:3cc3bbbbf93d82104892f42aa9106b6be4d120346dea0649643a97c801525256 AS api-build
WORKDIR /app
COPY Directory.Build.props VERSION ./
COPY src/ ./src/
COPY tests/ ./tests/
RUN dotnet restore src/Connector.Api/Connector.Api.csproj
RUN dotnet publish src/Connector.Api/Connector.Api.csproj \
    -c Release \
    -o /publish \
    --no-restore \
    --self-contained false

# ── Stage 3: Runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:f62a272ac1b46e83f56b8ed0416572f31cd1128e2c4a5e63eb34d348e4a36095 AS runtime
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
