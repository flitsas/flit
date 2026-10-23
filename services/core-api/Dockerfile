# core-api Dockerfile — una sola imagen con Flit.Api, Flit.Gateway y el migrador V1.
# Build context: services/  (incluye core-api + core-ict Grpc.Contracts)
#
# Framework-dependent (NO AOT): QuestPDF en Flit.Modules.Receipts aun no es
# AOT-compatible (ADR-0002). Cada servicio del compose elige su DLL via `command`:
#   core-api      -> dotnet /app/api/Flit.Api.dll        (default CMD)
#   gateway       -> dotnet /app/gateway/Flit.Gateway.dll
#   migrador      -> dotnet /app/migrador/Flit.DataMigration.V1.dll       (perfil `migracion`)
#   migracion-api -> dotnet /app/migracion-api/Flit.DataMigration.Api.dll (perfil `migracion`)
#
# El migrador va en la imagen para poder correrlo en el servidor donde estan las
# credenciales y la red hacia V1 y V2. NO se levanta solo: vive tras el perfil
# `migracion` del compose y se invoca a mano (`docker compose run`). Es una
# herramienta de operacion, no un servicio.
#
# migracion-api es el MISMO motor (Flit.DataMigration.Core) expuesto por HTTP, un tramite
# por peticion, para que operaciones lo dispare desde Postman sin entrar por SSH. Va como
# servicio aparte y NO dentro de Flit.Api porque el gateway fija ActivityTimeout 00:00:30
# en core-api-cluster y la instancia 3 tarda mas que eso: habria devuelto 504 sobre
# migraciones ya completadas. Ademas, asi las credenciales de V1 no viven en el proceso
# que atiende a los clientes, y se apaga con `docker compose stop` al terminar la ola.
#
# El contenedor replica la estructura del repo bajo /services, así las ProjectReference relativas resuelven igual
# que en local:
#   core-api      -> ../../../core-ict/src/Flit.Ict.Grpc.Contracts            (contratos gRPC con ICT)
#   core-api      -> ../../../core-identity/src/Flit.Identity.{Application,Web} (SOLO durante la transición del
#                    Epic #13217; se quita en el corte, HU #13235)
#   core-identity -> ../core-api/Directory.*.props (mismas reglas y versiones de paquetes)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /services/core-api
COPY core-api/global.json ./
COPY core-api/Directory.Build.props core-api/Directory.Packages.props ./
COPY core-api/src/ ./src/
# Migrador V1 -> V2. Su unica dependencia es src/Flit.Infrastructure, ya copiado.
COPY core-api/tools/ ./tools/
# Contratos gRPC compartidos (ProjectReference fuera de core-api/).
COPY core-ict/Directory.Build.props core-ict/Directory.Packages.props /services/core-ict/
COPY core-ict/src/Flit.Ict.Grpc.Contracts/ /services/core-ict/src/Flit.Ict.Grpc.Contracts/
# Epic #13217 — casos de uso de identidad que core-api atiende como respaldo durante la transición.
COPY core-identity/Directory.Build.props core-identity/Directory.Packages.props /services/core-identity/
COPY core-identity/src/Flit.Identity.Application/ /services/core-identity/src/Flit.Identity.Application/
COPY core-identity/src/Flit.Identity.Web/ /services/core-identity/src/Flit.Identity.Web/
# Epic #13316 (HU #13344): cliente gRPC de core-consultas. Su contrato se genera desde contracts/proto, que entra como
# contexto con nombre (--build-context contracts=contracts/proto), igual que en core-identity.
COPY core-identity/src/Flit.Platform.Grpc.Contracts/ /services/core-identity/src/Flit.Platform.Grpc.Contracts/
COPY core-consultas/Directory.Build.props core-consultas/Directory.Packages.props /services/core-consultas/
COPY core-consultas/src/Flit.Consultas.Grpc.Contracts/ /services/core-consultas/src/Flit.Consultas.Grpc.Contracts/
COPY core-consultas/src/Flit.Consultas.Grpc.Mapping/ /services/core-consultas/src/Flit.Consultas.Grpc.Mapping/
COPY core-notificaciones/Directory.Build.props core-notificaciones/Directory.Packages.props /services/core-notificaciones/
COPY core-notificaciones/src/Flit.Notificaciones.Grpc.Contracts/ /services/core-notificaciones/src/Flit.Notificaciones.Grpc.Contracts/
COPY --from=contracts . /contracts/proto/
# Framework-dependent (JIT): desactivar AOT y sus analizadores (IsAotCompatible),
# que estan en true por defecto en Directory.Build.props y rompen el publish con
# TreatWarningsAsErrors (IL2026/IL3050) al no ser un build AOT real.
RUN dotnet publish src/Flit.Api/Flit.Api.csproj \
    -c Release -o /app/api /p:PublishAot=false /p:IsAotCompatible=false
RUN dotnet publish src/Flit.Gateway/Flit.Gateway.csproj \
    -c Release -o /app/gateway /p:PublishAot=false /p:IsAotCompatible=false
RUN dotnet publish tools/Flit.DataMigration.V1/Flit.DataMigration.V1.csproj \
    -c Release -o /app/migrador /p:PublishAot=false /p:IsAotCompatible=false
RUN dotnet publish tools/Flit.DataMigration.Api/Flit.DataMigration.Api.csproj \
    -c Release -o /app/migracion-api /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/api ./api
COPY --from=build /app/gateway ./gateway
COPY --from=build /app/migrador ./migrador
COPY --from=build /app/migracion-api ./migracion-api
# Puerto interno por defecto (Flit.Api). El gateway lo sobreescribe via ASPNETCORE_URLS.
EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8081
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD wget -qO- http://localhost:8081/api/v1/health || exit 1
# Default: Flit.Api. El servicio gateway sobreescribe `command` en el compose.
CMD ["dotnet", "/app/api/Flit.Api.dll"]
