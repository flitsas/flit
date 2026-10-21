# core-diagnostico — Diagnóstico Vehicular (HU #13721), generado con la plantilla flit-servicio (Epic #13316, HU #13340).
# Build context: services/   +   contexto con nombre «contracts» = contracts/proto
#   docker build -f services/core-diagnostico/Dockerfile --build-context contracts=contracts/proto services
#
# Solo copia lo que compila: su código, el SDK de plataforma, los tipos comunes de los contratos y el contrato de
# Identidad (suscripción por petición, HU #13726). Replica la estructura
# del repo bajo /services para que las ProjectReference relativas resuelvan igual que en local.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /services/core-diagnostico
COPY core-diagnostico/global.json core-diagnostico/Directory.Build.props core-diagnostico/Directory.Packages.props ./
COPY core-api/Directory.Build.props core-api/Directory.Packages.props /services/core-api/
COPY core-api/src/Flit.Platform.Sdk/ /services/core-api/src/Flit.Platform.Sdk/
COPY core-api/src/Flit.Tramites.Grpc.Contracts/ /services/core-api/src/Flit.Tramites.Grpc.Contracts/
COPY core-identity/Directory.Build.props core-identity/Directory.Packages.props /services/core-identity/
COPY core-identity/src/Flit.Platform.Grpc.Contracts/ /services/core-identity/src/Flit.Platform.Grpc.Contracts/
COPY core-identity/src/Flit.Identidad.Grpc.Contracts/ /services/core-identity/src/Flit.Identidad.Grpc.Contracts/
COPY --from=contracts . /contracts/proto/
COPY core-diagnostico/src/ ./src/
RUN dotnet publish src/Flit.Diagnostico.Api/Flit.Diagnostico.Api.csproj \
    -c Release -o /app /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s \
  CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Flit.Diagnostico.Api.dll"]
