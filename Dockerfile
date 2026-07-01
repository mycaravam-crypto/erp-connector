# ── Stage 1: Build the Vue UI ─────────────────────────────────────────────────
FROM node:24-alpine AS ui-build
WORKDIR /app/ui
COPY src/connector-ui/package*.json ./
RUN npm ci --prefer-offline
COPY src/connector-ui/ ./
RUN npm run build-only

# ── Stage 2: Build the .NET API ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS api-build
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
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
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
