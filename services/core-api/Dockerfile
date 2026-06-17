# core-api Dockerfile — una sola imagen con Flit.Api y Flit.Gateway.
# Build context: services/core-api/
#
# Framework-dependent (NO AOT): QuestPDF en Flit.Modules.Receipts aun no es
# AOT-compatible (ADR-0002). Cada servicio del compose elige su DLL via `command`:
#   core-api -> dotnet /app/api/Flit.Api.dll        (default CMD)
#   gateway  -> dotnet /app/gateway/Flit.Gateway.dll

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/
# Framework-dependent (JIT): desactivar AOT y sus analizadores (IsAotCompatible),
# que estan en true por defecto en Directory.Build.props y rompen el publish con
# TreatWarningsAsErrors (IL2026/IL3050) al no ser un build AOT real.
RUN dotnet publish src/Flit.Api/Flit.Api.csproj \
    -c Release -o /app/api /p:PublishAot=false /p:IsAotCompatible=false
RUN dotnet publish src/Flit.Gateway/Flit.Gateway.csproj \
    -c Release -o /app/gateway /p:PublishAot=false /p:IsAotCompatible=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/api ./api
COPY --from=build /app/gateway ./gateway
# Puerto interno por defecto (Flit.Api). El gateway lo sobreescribe via ASPNETCORE_URLS.
EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8081
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD wget -qO- http://localhost:8081/api/v1/health || exit 1
# Default: Flit.Api. El servicio gateway sobreescribe `command` en el compose.
CMD ["dotnet", "/app/api/Flit.Api.dll"]
