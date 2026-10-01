# core-identity Dockerfile — Epic #13217 (HU #13234). El servicio de identidad de la FLIT Suite.
# Build context: services/  (igual que core-api)
#
# Solo copia lo que core-identity compila: su código y las librerías compartidas de core-api que usa. Nada de
# Trámites, OT, reportes ni Flit.Infrastructure (lo vigila Flit.Identity.Tests/BuildClosureTests, que también exige
# que esta lista coincida con su árbol de dependencias). Si se agrega una referencia, se agrega aquí.
#
# El contenedor replica la estructura del repo bajo /services, así las ProjectReference relativas resuelven igual que
# en local (core-identity -> ../../../core-api/src/...).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /services/core-identity
COPY core-identity/global.json core-identity/Directory.Build.props core-identity/Directory.Packages.props ./
COPY core-api/Directory.Build.props core-api/Directory.Packages.props /services/core-api/
# Librerías compartidas (core-api/src) — el cierre de dependencias de core-identity.
COPY core-api/src/Flit.Admin.Application/ /services/core-api/src/Flit.Admin.Application/
COPY core-api/src/Flit.Admin.Domain/ /services/core-api/src/Flit.Admin.Domain/
COPY core-api/src/Flit.Identity.Infrastructure/ /services/core-api/src/Flit.Identity.Infrastructure/
COPY core-api/src/Flit.Modules.Improntas.Domain/ /services/core-api/src/Flit.Modules.Improntas.Domain/
COPY core-api/src/Flit.Modules.Platform/ /services/core-api/src/Flit.Modules.Platform/
COPY core-api/src/Flit.Modules.Security.Application/ /services/core-api/src/Flit.Modules.Security.Application/
COPY core-api/src/Flit.Modules.Security.Domain/ /services/core-api/src/Flit.Modules.Security.Domain/
COPY core-api/src/Flit.Queries.Domain/ /services/core-api/src/Flit.Queries.Domain/
COPY core-api/src/Flit.Suite.AspNetCore/ /services/core-api/src/Flit.Suite.AspNetCore/
# Configuración base compartida con core-api (Flit.Identity.Api la enlaza: mismo emisor, llaves y clientes OIDC).
COPY core-api/src/Flit.Api/appsettings.json core-api/src/Flit.Api/appsettings.Development.json /services/core-api/src/Flit.Api/
# El servicio.
COPY core-identity/src/ ./src/
# Framework-dependent (JIT), como core-api: sin AOT ni sus analizadores (rompen el publish con TreatWarningsAsErrors).
RUN dotnet publish src/Flit.Identity.Api/Flit.Identity.Api.csproj \
    -c Release -o /app/identity /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app/identity
COPY --from=build /app/identity ./
ENV ASPNETCORE_URLS=http://+:8082
EXPOSE 8082
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD wget -qO- http://localhost:8082/health || exit 1
ENTRYPOINT ["dotnet", "Flit.Identity.Api.dll"]
