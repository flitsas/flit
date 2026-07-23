# core-ict Dockerfile — servicio de Integración con Terceros (ICT) de FLIT 2.0.
# Build context: services/core-ict/
# Framework-dependent (JIT, sin AOT): EF Core + gRPC codegen no son AOT-compatibles.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/
RUN dotnet publish src/Flit.Ict.Api/Flit.Ict.Api.csproj \
    -c Release -o /app/api /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/api ./api
EXPOSE 4020
# Http1AndHttp2 en claro (h2c) para servir REST + el callback gRPC en el mismo puerto.
ENV ASPNETCORE_URLS=http://+:4020
ENV Kestrel__EndpointDefaults__Protocols=Http1AndHttp2
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD wget -qO- http://localhost:4020/health || exit 1
CMD ["dotnet", "/app/api/Flit.Ict.Api.dll"]
