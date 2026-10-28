# core-personanatural — BFF del portal Persona Natural (Epic #14021, HU #14038), nacido de la plantilla de servicio (Epic #13316).
# Build context: services/   +   contexto con nombre «contracts» = contracts/proto
#   docker build -f services/core-personanatural/Dockerfile --build-context contracts=contracts/proto services
#
# Solo copia lo que compila: su código, el SDK de plataforma y los tipos comunes de los contratos. Replica la estructura
# del repo bajo /services para que las ProjectReference relativas resuelvan igual que en local.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /services/core-personanatural
COPY core-personanatural/global.json core-personanatural/Directory.Build.props core-personanatural/Directory.Packages.props ./
COPY core-api/Directory.Build.props core-api/Directory.Packages.props /services/core-api/
COPY core-api/src/Flit.Platform.Sdk/ /services/core-api/src/Flit.Platform.Sdk/
# HU #14043: contratos gRPC de la fachada de Persona Natural (core-api) y del envío de correo (core-notificaciones).
COPY core-api/src/Flit.Tramites.Grpc.Contracts/ /services/core-api/src/Flit.Tramites.Grpc.Contracts/
COPY core-notificaciones/Directory.Build.props core-notificaciones/Directory.Packages.props /services/core-notificaciones/
COPY core-notificaciones/src/Flit.Notificaciones.Grpc.Contracts/ /services/core-notificaciones/src/Flit.Notificaciones.Grpc.Contracts/
COPY core-identity/Directory.Build.props core-identity/Directory.Packages.props /services/core-identity/
COPY core-identity/src/Flit.Platform.Grpc.Contracts/ /services/core-identity/src/Flit.Platform.Grpc.Contracts/
COPY core-consultas/Directory.Build.props core-consultas/Directory.Packages.props /services/core-consultas/
COPY core-consultas/src/Flit.Consultas.Grpc.Contracts/ /services/core-consultas/src/Flit.Consultas.Grpc.Contracts/
COPY --from=contracts . /contracts/proto/
COPY core-personanatural/src/ ./src/
RUN dotnet publish src/Flit.PersonaNatural.Api/Flit.PersonaNatural.Api.csproj \
    -c Release -o /app /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app ./
# Sin root (HU #14038): la imagen aspnet trae el usuario app (APP_UID); los puertos 8080 y del gRPC son altos, no necesitan privilegios.
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s \
  CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Flit.PersonaNatural.Api.dll"]
