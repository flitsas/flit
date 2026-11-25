using System.Security.Claims;
using Flit.Api.Identity;
using Flit.Identidad.Grpc.V1;
using Flit.Identity.Api.Grpc;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Identity.Tests.Grpc;

/// <summary>
/// HU #14694 (plan §1.2 decisión R1; contracts/proto/flit/identidad/v1/aprovisionamiento_producto.proto) —
/// <c>AprovisionamientoProductoService.ReportarEstadoAprovisionamiento</c>: solo <c>svc-impuestos</c> (el producto sale del
/// token, nunca del mensaje), empresa obligatoria, idempotente por versión (solo guarda una mayor; igual o menor responde
/// <c>aplicado=false</c> con la versión vigente) y errores del contrato.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>await servicio.ReportarEstadoAprovisionamiento(new() { Estado = "LISTO", Version = 3 }, contexto)</c>,
/// con la metadata ya validada por el interceptor de token de servicio.
/// </remarks>
public sealed class AprovisionamientoProductoGrpcTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Almacen _almacen = new();

    private AprovisionamientoProductoGrpcService Servicio() =>
        new(new ReportProvisioningStatusHandler(_almacen), NullLogger<AprovisionamientoProductoGrpcService>.Instance);

    private Task<ReportarEstadoAprovisionamientoResponse> Reportar(long version, string estado = "LISTO", string cliente = "svc-impuestos", Guid? empresa = null, string accion = "") =>
        Servicio().ReportarEstadoAprovisionamiento(
            new ReportarEstadoAprovisionamientoRequest { Estado = estado, Version = version, Accion = accion, FirmaConfirmada = false },
            new Contexto(cliente, empresa ?? Empresa, Ct));

    [Fact]
    public async Task SvcImpuestos_PrimerReporte_SeAplica_ConElProductoDelToken()
    {
        var r = await Reportar(1, "PENDIENTE");

        r.Aplicado.Should().BeTrue();
        r.VersionGuardada.Should().Be(1);
        _almacen.Filas.Should().ContainSingle().Which.Should().Match<TenantProductProvisioning>(f =>
            f.TenantId == Empresa && f.ProductCode == "impuestos" && f.Estado == "PENDIENTE" && f.Version == 1);
    }

    [Fact]
    public async Task VersionMonotonica_SoloGuardaUnaMayor_IgualOMenorNoCambiaNada()
    {
        await Reportar(5, "LISTO");

        var igual = await Reportar(5, "ERROR");
        var menor = await Reportar(2, "ERROR");
        var mayor = await Reportar(6, "REQUIERE_CREDENCIALES", accion: "CARGAR_CREDENCIALES");

        igual.Should().Match<ReportarEstadoAprovisionamientoResponse>(r => !r.Aplicado && r.VersionGuardada == 5);
        menor.Should().Match<ReportarEstadoAprovisionamientoResponse>(r => !r.Aplicado && r.VersionGuardada == 5);
        mayor.Should().Match<ReportarEstadoAprovisionamientoResponse>(r => r.Aplicado && r.VersionGuardada == 6);
        _almacen.Filas.Single().Estado.Should().Be("REQUIERE_CREDENCIALES");
    }

    [Theory]
    [InlineData("svc-diagnostico")]
    [InlineData("svc-consultas")]
    [InlineData("svc-tramites")]
    public async Task OtroCliente_PermissionDenied_ClienteNoPermitido(string cliente)
    {
        var act = () => Reportar(1, cliente: cliente);

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.PermissionDenied);
        PlatformRpcErrors.Reason(ex).Should().Be("CLIENTE_NO_PERMITIDO");
        _almacen.Filas.Should().BeEmpty();
    }

    [Fact]
    public async Task SinEmpresa_InvalidArgument()
    {
        var act = () => Reportar(1, empresa: Guid.Empty);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        _almacen.Filas.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ACTIVO", 1)]
    [InlineData("", 1)]
    [InlineData("LISTO", 0)]
    public async Task ReporteFueraDelContrato_InvalidArgument(string estado, long version)
    {
        var act = () => Reportar(version, estado);

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.InvalidArgument);
        PlatformRpcErrors.Reason(ex).Should().Be(TenantProductException.ProvisioningReportInvalid);
    }

    [Fact]
    public async Task EmpresaInexistente_NotFound()
    {
        _almacen.EmpresaExiste = false;

        var act = () => Reportar(1);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public void ElInterceptorDelServicio_ExigeElScopeLaAudienciaYLaEmpresa()
    {
        var interceptor = new PlatformServiceCallInterceptor(AprovisionamientoProductoGrpcService.Scope, ServiceAudiences.Plataforma);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "svc-impuestos"), new Claim("aud", ServiceAudiences.Plataforma), new Claim("scope", AprovisionamientoProductoGrpcService.Scope)], "Bearer"));

        AprovisionamientoProductoGrpcService.Scope.Should().Be("platform.aprovisionamiento.reportar");
        interceptor.Validate(user, Empresa.ToString()).Should().Be(new PlatformServiceCaller("svc-impuestos", Empresa));
        FluentActions.Invoking(() => interceptor.Validate(user, null)).Should().Throw<RpcException>()
            .Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    private sealed class Almacen : ITenantProductProvisioningStore
    {
        public List<TenantProductProvisioning> Filas { get; } = [];

        public bool EmpresaExiste { get; set; } = true;

        public Task<ProvisioningGuardado> GuardarSiEsMasNuevoAsync(TenantProductProvisioning reporte, CancellationToken cancellationToken = default)
        {
            if (!EmpresaExiste)
                return Task.FromResult(new ProvisioningGuardado(false, false, 0));
            var actual = Filas.SingleOrDefault(f => f.TenantId == reporte.TenantId && f.ProductCode == reporte.ProductCode);
            if (actual is not null && actual.Version >= reporte.Version)
                return Task.FromResult(new ProvisioningGuardado(true, false, actual.Version));
            if (actual is not null)
                Filas.Remove(actual);
            Filas.Add(reporte);
            return Task.FromResult(new ProvisioningGuardado(true, true, reporte.Version));
        }

        public Task<IReadOnlyList<TenantProductProvisioning>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantProductProvisioning>>(Filas.Where(f => f.TenantId == tenantId).ToList());

        // HU #14711: el alta automática de diagnostico no pasa por este rpc.
        public Task<Action<DateTimeOffset>> PrepararPendienteAltaDiagnosticoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PendienteAltaDiagnostico>> ReservarPendientesAltaDiagnosticoAsync(
            DateTimeOffset ahora, TimeSpan reserva, int lote, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RegistrarEstadoAltaDiagnosticoAsync(EstadoAltaDiagnostico estado, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Contexto : ServerCallContext
    {
        private readonly CancellationToken _ct;

        public Contexto(string cliente, Guid empresa, CancellationToken ct)
        {
            _ct = ct;
            UserState["flit.serviceCaller"] = new PlatformServiceCaller(cliente, empresa);
            UserState["__HttpContext"] = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", cliente), new Claim("jti", "jti-de-prueba")], "Bearer")),
            };
        }

        protected override string MethodCore => "/flit.identidad.v1.AprovisionamientoProductoService/ReportarEstadoAprovisionamiento";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:10.0.0.7:51000";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => _ct;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, []);

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
