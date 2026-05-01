# syntax=docker/dockerfile:1

# ── Build stage ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file first for layer caching of the restore step
COPY src/ExpertiseApi/ExpertiseApi.csproj src/ExpertiseApi/

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/ExpertiseApi/ExpertiseApi.csproj

# Copy the rest of the source and publish
COPY src/ src/

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/ExpertiseApi/ExpertiseApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ── Migration bundle stage ─────────────────────────────────────────────────────
FROM build AS bundle
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet tool install --global dotnet-ef --version 10.0.*
ENV PATH="$PATH:/root/.dotnet/tools"

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet ef migrations bundle \
    --project src/ExpertiseApi/ExpertiseApi.csproj \
    --configuration Release \
    --no-build \
    --self-contained \
    --output /app/efbundle

# ── Runtime stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Match the upstream image's app user. ARG default is the fallback if a
# future base-image revision ever drops the inherited APP_UID env var.
ARG APP_UID=1654

COPY --from=build /app/publish .
COPY --from=bundle /app/efbundle ./efbundle

COPY src/ExpertiseApi/models/ ./models/

RUN test -f models/model.onnx || (echo 'ERROR: Model files missing — run scripts/download-models.sh before building' && exit 1)

RUN chown -R $APP_UID:$APP_UID /app/models/

# curl is installed for the HEALTHCHECK below. The slim aspnet:10.0 image ships
# with neither curl nor wget, so any HTTP-probe-based healthcheck must install
# something. The HEALTHCHECK directive is consumed by Docker / Compose only —
# Kubernetes uses its own livenessProbe / readinessProbe (see Helm chart).
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

USER $APP_UID

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -sf http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "ExpertiseApi.dll"]
