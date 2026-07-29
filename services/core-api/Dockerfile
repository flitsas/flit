# core-api Dockerfile — una sola imagen con Flit.Api, Flit.Gateway y el migrador V1.
# Build context: services/  (incluye core-api + core-ict Grpc.Contracts)
#
# Framework-dependent (NO AOT): QuestPDF en Flit.Modules.Receipts aun no es
# AOT-compatible (ADR-0002). Cada servicio del compose elige su DLL via `command`:
#   core-api -> dotnet /app/api/Flit.Api.dll        (default CMD)
#   gateway  -> dotnet /app/gateway/Flit.Gateway.dll
#   migrador -> dotnet /app/migrador/Flit.DataMigration.V1.dll  (perfil `migracion`)
#
# El migrador va en la imagen para poder correrlo en el servidor donde estan las
# credenciales y la red hacia V1 y V2. NO se levanta solo: vive tras el perfil
# `migracion` del compose y se invoca a mano (`docker compose run`). Es una
# herramienta de operacion, no un servicio.
#
# Flit.Api / Flit.Infrastructure referencian
#   ../../../core-ict/src/Flit.Ict.Grpc.Contracts
# desde services/core-api/src/... → resuelve a /core-ict/... en la imagen
# (WORKDIR /src + ProjectReference sube tres niveles hasta /).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY core-api/global.json ./
COPY core-api/Directory.Build.props core-api/Directory.Packages.props ./
COPY core-api/src/ ./src/
# Migrador V1 -> V2. Su unica dependencia es src/Flit.Infrastructure, ya copiado.
COPY core-api/tools/ ./tools/
# Contratos gRPC compartidos (ProjectReference fuera de core-api/).
COPY core-ict/Directory.Build.props core-ict/Directory.Packages.props /core-ict/
COPY core-ict/src/Flit.Ict.Grpc.Contracts/ /core-ict/src/Flit.Ict.Grpc.Contracts/
# Framework-dependent (JIT): desactivar AOT y sus analizadores (IsAotCompatible),
# que estan en true por defecto en Directory.Build.props y rompen el publish con
# TreatWarningsAsErrors (IL2026/IL3050) al no ser un build AOT real.
RUN dotnet publish src/Flit.Api/Flit.Api.csproj \
    -c Release -o /app/api /p:PublishAot=false /p:IsAotCompatible=false
RUN dotnet publish src/Flit.Gateway/Flit.Gateway.csproj \
    -c Release -o /app/gateway /p:PublishAot=false /p:IsAotCompatible=false
RUN dotnet publish tools/Flit.DataMigration.V1/Flit.DataMigration.V1.csproj \
    -c Release -o /app/migrador /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/api ./api
COPY --from=build /app/gateway ./gateway
COPY --from=build /app/migrador ./migrador
# Puerto interno por defecto (Flit.Api). El gateway lo sobreescribe via ASPNETCORE_URLS.
EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8081
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD wget -qO- http://localhost:8081/api/v1/health || exit 1
# Default: Flit.Api. El servicio gateway sobreescribe `command` en el compose.
CMD ["dotnet", "/app/api/Flit.Api.dll"]
