# core-comparendos — Gestión de Comparendos (Epic #12878), nacido de la plantilla de servicio (HU #13340).
# Build context: services/   +   contexto con nombre «contracts» = contracts/proto
#   docker build -f services/core-comparendos/Dockerfile --build-context contracts=contracts/proto services
#
# Solo copia lo que compila: su código, el SDK de plataforma, los tipos comunes de los contratos y los contratos de
# Notificaciones (correos), Consultas (fuentes de comparendos) e Identidad (productos habilitados). Replica la estructura
# del repo bajo /services para que las ProjectReference relativas resuelvan igual que en local.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /services/core-comparendos
COPY core-comparendos/global.json core-comparendos/Directory.Build.props core-comparendos/Directory.Packages.props ./
COPY core-api/Directory.Build.props core-api/Directory.Packages.props /services/core-api/
COPY core-api/src/Flit.Platform.Sdk/ /services/core-api/src/Flit.Platform.Sdk/
COPY core-identity/Directory.Build.props core-identity/Directory.Packages.props /services/core-identity/
COPY core-identity/src/Flit.Platform.Grpc.Contracts/ /services/core-identity/src/Flit.Platform.Grpc.Contracts/
COPY core-notificaciones/Directory.Build.props core-notificaciones/Directory.Packages.props /services/core-notificaciones/
COPY core-notificaciones/src/Flit.Notificaciones.Grpc.Contracts/ /services/core-notificaciones/src/Flit.Notificaciones.Grpc.Contracts/
COPY core-identity/src/Flit.Identidad.Grpc.Contracts/ /services/core-identity/src/Flit.Identidad.Grpc.Contracts/
COPY core-consultas/Directory.Build.props core-consultas/Directory.Packages.props /services/core-consultas/
COPY core-consultas/src/Flit.Consultas.Grpc.Contracts/ /services/core-consultas/src/Flit.Consultas.Grpc.Contracts/
COPY --from=contracts . /contracts/proto/
COPY core-comparendos/src/ ./src/
RUN dotnet publish src/Flit.Comparendos.Api/Flit.Comparendos.Api.csproj \
    -c Release -o /app /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s \
  CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Flit.Comparendos.Api.dll"]
