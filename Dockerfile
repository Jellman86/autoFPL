ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.302-noble@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.10-noble-chiseled-extra@sha256:f9bd6be9b5ab75b8196bff0f0972580edaea7fa8ca04e6ef530950e33caee5b0

FROM ${SDK_IMAGE} AS build
WORKDIR /src

COPY Directory.Build.props global.json ./
COPY src/backend/AutoFpl.Domain/AutoFpl.Domain.csproj src/backend/AutoFpl.Domain/packages.lock.json src/backend/AutoFpl.Domain/
COPY src/backend/AutoFpl.Contracts/AutoFpl.Contracts.csproj src/backend/AutoFpl.Contracts/packages.lock.json src/backend/AutoFpl.Contracts/
COPY src/backend/AutoFpl.Api/AutoFpl.Api.csproj src/backend/AutoFpl.Api/packages.lock.json src/backend/AutoFpl.Api/

RUN dotnet restore src/backend/AutoFpl.Api/AutoFpl.Api.csproj --locked-mode --nologo

COPY src/backend/AutoFpl.Domain/ src/backend/AutoFpl.Domain/
COPY src/backend/AutoFpl.Contracts/ src/backend/AutoFpl.Contracts/
COPY src/backend/AutoFpl.Api/ src/backend/AutoFpl.Api/

RUN dotnet publish src/backend/AutoFpl.Api/AutoFpl.Api.csproj \
    --configuration Release \
    --no-restore \
    --nologo \
    --output /app/publish \
    /p:UseAppHost=false

FROM ${RUNTIME_IMAGE} AS runtime
ARG SOURCE_REVISION=unknown

LABEL org.opencontainers.image.title="autoFPL API" \
      org.opencontainers.image.description="Fail-closed autoFPL decision snapshot API" \
      org.opencontainers.image.source="https://github.com/Jellman86/autoFPL" \
      org.opencontainers.image.revision="${SOURCE_REVISION}" \
      org.opencontainers.image.licenses="AGPL-3.0-only"

WORKDIR /app
COPY --from=build --chown=1654:1654 /app/publish/ ./

USER 1654
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD ["dotnet", "AutoFpl.Api.dll", "--health-check"]

ENTRYPOINT ["dotnet", "AutoFpl.Api.dll"]
