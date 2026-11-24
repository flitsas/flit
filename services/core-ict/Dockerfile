# core-ict Dockerfile — servicio de Integración con Terceros (ICT) de FLIT 2.0.
# Build context: services/core-ict/
# Framework-dependent (JIT, sin AOT): EF Core + gRPC codegen no son AOT-compatibles.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/
# Epic #13316 (HU #13346): cliente de core-consultas generado desde contracts/proto, que entra como contexto con nombre
# (--build-context contracts=contracts/proto); Flit.Ict.Infrastructure lo busca en ../../../../contracts/proto.
COPY --from=contracts . /contracts/proto/
RUN dotnet publish src/Flit.Ict.Api/Flit.Ict.Api.csproj \
    -c Release -o /app/api /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/api ./api
# 4020 = REST (HTTP/1.1). 4021 = callback gRPC h2c (HTTP/2 only).
# Kestrel no multiplexa HTTP/1.1 + HTTP/2 en claro en el mismo puerto (HTTP_1_1_REQUIRED).
EXPOSE 4020 4021
# Anula el ASPNETCORE_HTTP_PORTS=8080 de la imagen base aspnet (chocaría con ASPNETCORE_URLS).
# Ict__GrpcPort lo fija compose/gitops por ambiente (4021 DEV / 5021 QA / 6021 PDN).
ENV ASPNETCORE_HTTP_PORTS=
ENV ASPNETCORE_URLS=http://+:4020
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD wget -qO- http://localhost:4020/health || exit 1
CMD ["dotnet", "/app/api/Flit.Ict.Api.dll"]
