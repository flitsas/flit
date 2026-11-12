using Flit.Api.Derivacion;
using Flit.Api.Grpc;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Admin.Tests.Derivacion;

/// <summary>
/// ADR-0084 / diseño §3 reglas 1 y 6 — el adaptador gRPC: solo <c>svc-diagnostico</c>, con la empresa de la metadata; el
/// limitador de concurrencia responde <c>RESOURCE_EXHAUSTED</c> inmediato en vez de encolar; la lectura por referencia
/// acepta como máximo 500 referencias.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await servicio.CrearBorradorDesdeDiagnostico(request, contexto)</c>.</remarks>
public sealed class DerivacionTramitesGrpcServiceTests
{
    private static readonly Guid Empresa = Guid.Parse("0b0e0000-0000-4000-8000-000000000002");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DerivacionTramitesGrpcService Servicio(LimitadorDerivacion limitador, AlmacenMinimo? almacen = null) =>
        new(new CreadorBorradorDiagnostico(almacen ?? new AlmacenMinimo(), new AutorQuePuede(), new OficinaUnica()), limitador,
            NullLogger<DerivacionTramitesGrpcService>.Instance);

    private static CrearBorradorDesdeDiagnosticoRequest Solicitud() => new()
    {
        ExternalRef = Guid.NewGuid().ToString(),
        UsuarioId = Guid.NewGuid().ToString(),
        TipoTramiteCodigo = "TRASPASO_STANDARD",
        Placa = "ABC123",
        OrganismoNombreRunt = "ORGANISMO",
    };

    [Fact]
    public async Task ConPermisoDelLimitador_CreaElBorrador()
    {
        using var limitador = new LimitadorDerivacion(2);

        var respuesta = await Servicio(limitador).CrearBorradorDesdeDiagnostico(Solicitud(), new ContextoFalso("svc-diagnostico", Empresa, Ct));

        respuesta.ResultadoCase.Should().Be(CrearBorradorDesdeDiagnosticoResponse.ResultadoOneofCase.Creado);
    }

    [Fact]
    public async Task ConElLimitadorLleno_RespondeResourceExhausted_SinEncolarNiCrear()
    {
        using var limitador = new LimitadorDerivacion(1);
        using var ocupado = limitador.Intentar();
        ocupado.IsAcquired.Should().BeTrue();
        var almacen = new AlmacenMinimo();

        var llamada = () => Servicio(limitador, almacen).CrearBorradorDesdeDiagnostico(Solicitud(), new ContextoFalso("svc-diagnostico", Empresa, Ct));

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.ResourceExhausted);
        almacen.Creaciones.Should().Be(0);
    }

    [Fact]
    public async Task AlTerminarUnaLlamada_ElPermisoSeDevuelve()
    {
        using var limitador = new LimitadorDerivacion(1);
        var servicio = Servicio(limitador);

        await servicio.CrearBorradorDesdeDiagnostico(Solicitud(), new ContextoFalso("svc-diagnostico", Empresa, Ct));
        var segunda = await servicio.CrearBorradorDesdeDiagnostico(Solicitud(), new ContextoFalso("svc-diagnostico", Empresa, Ct));

        segunda.ResultadoCase.Should().Be(CrearBorradorDesdeDiagnosticoResponse.ResultadoOneofCase.Creado);
    }

    [Fact]
    public async Task OtroClienteDeServicio_RecibePermissionDenied()
    {
        using var limitador = new LimitadorDerivacion(2);

        var llamada = () => Servicio(limitador).CrearBorradorDesdeDiagnostico(Solicitud(), new ContextoFalso("svc-ict", Empresa, Ct));

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task MasDe500Referencias_RespondeInvalidArgument()
    {
        using var limitador = new LimitadorDerivacion(2);
        var request = new ObtenerTramitesPorReferenciaRequest();
        request.ExternalRefs.AddRange(Enumerable.Range(0, 501).Select(i => $"ref-{i}"));

        var llamada = () => Servicio(limitador).ObtenerTramitesPorReferencia(request, new ContextoFalso("svc-diagnostico", Empresa, Ct));

        (await llamada.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task Hasta500Referencias_Responde()
    {
        using var limitador = new LimitadorDerivacion(2);
        var request = new ObtenerTramitesPorReferenciaRequest();
        request.ExternalRefs.AddRange(Enumerable.Range(0, 500).Select(i => $"ref-{i}"));

        var respuesta = await Servicio(limitador).ObtenerTramitesPorReferencia(request, new ContextoFalso("svc-diagnostico", Empresa, Ct));

        respuesta.Should().NotBeNull();
    }

    // ── Dobles ────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class ContextoFalso : ServerCallContext
    {
        private readonly CancellationToken _ct;

        public ContextoFalso(string cliente, Guid empresa, CancellationToken ct)
        {
            _ct = ct;
            // Lo que deja el interceptor del SDK (PlatformServiceCallInterceptor) tras validar el token.
            UserState["flit.serviceCaller"] = new PlatformServiceCaller(cliente, empresa);
        }

        protected override string MethodCore => "/flit.tramites.v1.DerivacionTramitesService/CrearBorradorDesdeDiagnostico";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:0";
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

    private sealed class AlmacenMinimo : IAlmacenBorradorDiagnostico
    {
        public int Creaciones { get; private set; }

        public Task<TramiteExistente?> BuscarPorReferenciaAsync(Guid tenantId, string externalRef, CancellationToken ct) => Task.FromResult<TramiteExistente?>(null);

        public Task<IReadOnlyList<TramiteExistente>> BuscarPorReferenciasAsync(Guid tenantId, IReadOnlyCollection<string> externalRefs, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TramiteExistente>>([]);

        public Task<TipoParaDerivar?> TipoPublicadoAsync(string codigo, CancellationToken ct) => Task.FromResult<TipoParaDerivar?>(new(codigo, "TRASPASO"));

        public Task<CreacionBorrador> CrearAsync(CreateProcedureInstanceRequest request, CancellationToken ct)
        {
            Creaciones++;
            return Task.FromResult(new CreacionBorrador(
                new ProcedureInstanceSummary(Guid.NewGuid(), "TR-1", "borrador", Guid.NewGuid(), request.TenantId, DateTimeOffset.UtcNow, null), null));
        }

        public Task<string?> SembrarCamposAsync(Guid id, Guid tenantId, Guid autor, IReadOnlyList<FieldValueInput> campos, bool soloFaltantes, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public Task<string?> SembrarSistemaAsync(Guid id, Guid tenantId, IReadOnlyList<FieldValueInput> campos, bool soloFaltantes, CancellationToken ct) =>
            Task.FromResult<string?>(null);
    }

    private sealed class AutorQuePuede : IAutorDerivacion
    {
        public Task<bool> PuedeCrearAsync(Guid tenantId, Guid usuarioId, string familia, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class OficinaUnica : ITransitOfficeResolver
    {
        private static readonly ResolvedTransitOffice Oficina = new(Guid.NewGuid(), "05001000", "ORGANISMO", "05001");

        public Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(Guid tenantId, string transitOfficeName, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedTransitOffice?>(Oficina);

        public Task<ResolvedTransitOffice?> ResolveEnabledByIdAsync(Guid tenantId, Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedTransitOffice?>(null);
    }
}
