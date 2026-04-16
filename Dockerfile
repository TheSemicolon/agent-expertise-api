# syntax=docker/dockerfile:1

# ── Build stage ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file first for layer caching of the restore step
COPY src/ExpertiseApi/ExpertiseApi.csproj src/ExpertiseApi/

RUN dotnet restore src/ExpertiseApi/ExpertiseApi.csproj

# Copy the rest of the source and publish
COPY src/ src/

RUN dotnet publish src/ExpertiseApi/ExpertiseApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ── Migration bundle stage ─────────────────────────────────────────────────────
FROM build AS bundle
RUN dotnet tool install --global dotnet-ef --version 10.0.*
ENV PATH="$PATH:/root/.dotnet/tools"

RUN dotnet ef migrations bundle \
    --project src/ExpertiseApi/ExpertiseApi.csproj \
    --configuration Release \
    --no-build \
    --self-contained \
    --output /app/efbundle

# ── Runtime stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=bundle /app/efbundle ./efbundle

COPY src/ExpertiseApi/models/ ./models/

RUN test -f models/model.onnx || (echo 'ERROR: Model files missing — run scripts/download-models.sh before building' && exit 1)

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

USER $APP_UID

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -sf http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "ExpertiseApi.dll"]
