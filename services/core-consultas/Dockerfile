# core-consultas — servicio de plataforma (consultas, Epic #13316, HU #13340).
# Build context: services/   +   contexto con nombre «contracts» = contracts/proto
#   docker build -f services/core-consultas/Dockerfile --build-context contracts=contracts/proto services
#
# Solo copia lo que compila: su código, el SDK de plataforma, el módulo de consultas y los tipos comunes de los contratos. Replica la estructura
# del repo bajo /services para que las ProjectReference relativas resuelvan igual que en local.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /services/core-consultas
COPY core-consultas/global.json core-consultas/Directory.Build.props core-consultas/Directory.Packages.props ./
COPY core-api/Directory.Build.props core-api/Directory.Packages.props /services/core-api/
COPY core-api/src/Flit.Platform.Sdk/ /services/core-api/src/Flit.Platform.Sdk/
# HU #13343: proveedores y cadena de respaldo (módulo de consultas) y los tipos de tiempo que usa.
COPY core-api/src/Flit.Modules.Consultas/ /services/core-api/src/Flit.Modules.Consultas/
COPY core-api/src/Flit.Queries.Domain/ /services/core-api/src/Flit.Queries.Domain/
COPY core-identity/Directory.Build.props core-identity/Directory.Packages.props /services/core-identity/
COPY core-identity/src/Flit.Platform.Grpc.Contracts/ /services/core-identity/src/Flit.Platform.Grpc.Contracts/
COPY --from=contracts . /contracts/proto/
COPY core-consultas/src/ ./src/
RUN dotnet publish src/Flit.Consultas.Api/Flit.Consultas.Api.csproj \
    -c Release -o /app /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s \
  CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Flit.Consultas.Api.dll"]
