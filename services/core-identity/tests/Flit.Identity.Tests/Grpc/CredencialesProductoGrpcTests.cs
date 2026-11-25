using System.Security.Claims;
using System.Security.Cryptography;
using Flit.Api.Identity;
using Flit.Identidad.Grpc.V1;
using Flit.Identity.Api.Grpc;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Identity.Tests.Grpc;

/// <summary>
/// ADR-0092 (diseño impuesto-real §1.7) — <c>CredencialesProductoService.ObtenerCredencialImpuesto</c>: solo
/// <c>svc-consultas</c>; responde por la empresa de <c>x-flit-tenant-id</c>; fail-closed con Diagnóstico apagado en la empresa
/// o en un ancestro (no descifra); sin fila, <c>SIN_CONFIGURAR</c>; con llave, valor + <c>row_version</c> + TTL 300; error
/// inesperado como <c>INTERNAL</c> genérico; la llave nunca aparece en los logs.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>await servicio.ObtenerCredencialImpuesto(new ObtenerCredencialImpuestoRequest(), contexto)</c>, con la
/// metadata <c>x-flit-tenant-id</c> ya validada por <see cref="PlatformServiceCallInterceptor"/>.
/// </remarks>
public sealed class CredencialesProductoGrpcTests
{
    private const string Centinela = "llave-impuesto-CENTINELA-0123456789";
    private const string LlaveNueva = "llave-rotada-CENTINELA-NUEVA-0123456789";
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cabeza = Guid.NewGuid();

    private readonly Acceso _acceso = new();
    private readonly Credenciales _credenciales = new();
    private readonly Protector _protector = new();
    private readonly LoggerCapturado _logger = new();

    public CredencialesProductoGrpcTests()
    {
        _acceso.Cadena = [Empresa, Cabeza];
        _acceso.Encendidas = [Empresa, Cabeza];
        _credenciales.Fila = new TenantProductCredential(Empresa, "diagnostico", "impuesto_api_key", "cifrado-opaco-0123456789", RowVersion: 7, DateTimeOffset.UnixEpoch);
        _protector.Plano = Centinela;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CredencialesProductoGrpcService Servicio() =>
        new(
            new GetTenantProductCredentialHandler(_acceso, _credenciales, _protector),
            new GetTenantProductCredentialForCopyHandler(_credenciales, _protector),
            new UpdateTenantProductCredentialFromServiceHandler(_credenciales, _protector),
            _logger);

    private static Task<ActualizarCredencialImpuestoDiagnosticoResponse> Actualizar(
        CredencialesProductoGrpcService servicio, string apiKey = LlaveNueva, string cliente = "svc-impuestos", Guid? empresa = null) =>
        servicio.ActualizarCredencialImpuestoDiagnostico(
            new ActualizarCredencialImpuestoDiagnosticoRequest { ApiKey = apiKey }, new Contexto(cliente, empresa ?? Empresa, Ct));

    private static Task<ObtenerCredencialImpuestoParaCopiaResponse> Copiar(CredencialesProductoGrpcService servicio, string cliente = "svc-impuestos", Guid? empresa = null) =>
        servicio.ObtenerCredencialImpuestoParaCopia(new ObtenerCredencialImpuestoParaCopiaRequest(), new Contexto(cliente, empresa ?? Empresa, Ct));

    private static Task<ObtenerCredencialImpuestoResponse> Llamar(CredencialesProductoGrpcService servicio, string cliente = "svc-consultas", Guid? empresa = null) =>
        servicio.ObtenerCredencialImpuesto(new ObtenerCredencialImpuestoRequest(), new Contexto(cliente, empresa ?? Empresa, Ct));

    [Fact]
    public async Task ConLlaveYProductoEncendido_EntregaValorVersionYTtl300()
    {
        var r = await Llamar(Servicio());

        r.Disponible.Should().BeTrue();
        r.Motivo.Should().Be(MotivoCredencialNoDisponible.Unspecified);
        r.ApiKey.Should().Be(Centinela);
        r.Version.Should().Be(7);
        r.TtlSegundos.Should().Be(300);
        _protector.Descifrados.Should().Equal((Empresa, "impuesto_api_key", "cifrado-opaco-0123456789"));
        _credenciales.Consultas.Should().Equal((Empresa, "diagnostico", "impuesto_api_key"));
        _logger.Entradas.Should().ContainSingle(e => e.Texto.Contains("impuesto.credencial.entregada", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SinFila_SinConfigurar_Version0_Ttl60()
    {
        _credenciales.Fila = null;

        var r = await Llamar(Servicio());

        r.Disponible.Should().BeFalse();
        r.Motivo.Should().Be(MotivoCredencialNoDisponible.SinConfigurar);
        r.ApiKey.Should().BeEmpty();
        r.Version.Should().Be(0);
        r.TtlSegundos.Should().Be(60);
    }

    [Fact]
    public async Task DiagnosticoApagadoEnLaEmpresa_ProductoApagado_SinDescifrar()
    {
        _acceso.Encendidas = [Cabeza];

        var r = await Llamar(Servicio());

        r.Disponible.Should().BeFalse();
        r.Motivo.Should().Be(MotivoCredencialNoDisponible.ProductoApagado);
        r.ApiKey.Should().BeEmpty();
        r.TtlSegundos.Should().Be(60);
        _protector.Descifrados.Should().BeEmpty();
    }

    [Fact]
    public async Task DiagnosticoApagadoEnUnAncestro_ProductoApagado_SinDescifrar()
    {
        _acceso.Encendidas = [Empresa];

        var r = await Llamar(Servicio());

        r.Motivo.Should().Be(MotivoCredencialNoDisponible.ProductoApagado);
        r.ApiKey.Should().BeEmpty();
        _protector.Descifrados.Should().BeEmpty();
    }

    [Fact]
    public async Task EmpresaInexistente_CadenaVacia_ProductoApagado()
    {
        _acceso.Cadena = [];

        var r = await Llamar(Servicio());

        r.Motivo.Should().Be(MotivoCredencialNoDisponible.ProductoApagado);
        _protector.Descifrados.Should().BeEmpty();
    }

    [Theory]
    [InlineData("svc-diagnostico")]
    [InlineData("svc-tramites")]
    [InlineData("svc-impuestos")] // HU #14694: svc-impuestos solo tiene el rpc de copia
    public async Task OtroCliente_PermissionDenied_ClienteNoPermitido(string cliente)
    {
        var act = () => Llamar(Servicio(), cliente);

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.PermissionDenied);
        PlatformRpcErrors.Reason(ex).Should().Be("CLIENTE_NO_PERMITIDO");
        _credenciales.Consultas.Should().BeEmpty();
        _logger.Entradas.Should().ContainSingle(e => e.Texto.Contains("impuesto.credencial.denegada", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SinEmpresa_InvalidArgument()
    {
        var act = () => Llamar(Servicio(), empresa: Guid.Empty);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        _credenciales.Consultas.Should().BeEmpty();
    }

    [Fact]
    public void ElInterceptorDelServicio_ExigeLaEmpresa_ElScopeYLaAudiencia()
    {
        var interceptor = new PlatformServiceCallInterceptor(CredencialesProductoGrpcService.Scope, ServiceAudiences.Plataforma);
        var user = Usuario("svc-consultas", ServiceAudiences.Plataforma, CredencialesProductoGrpcService.Scope);

        var sinEmpresa = () => interceptor.Validate(user, null);
        sinEmpresa.Should().Throw<RpcException>().Which.StatusCode.Should().Be(StatusCode.InvalidArgument);

        var sinScope = () => interceptor.Validate(Usuario("svc-consultas", ServiceAudiences.Plataforma, "platform.identidad.read"), Empresa.ToString());
        sinScope.Should().Throw<RpcException>().Which.StatusCode.Should().Be(StatusCode.PermissionDenied);

        interceptor.Validate(user, Empresa.ToString()).Should().Be(new PlatformServiceCaller("svc-consultas", Empresa));
        CredencialesProductoGrpcService.Scope.Should().Be("platform.identidad.credenciales.impuesto");
    }

    [Fact]
    public async Task ErrorInesperado_InternalGenerico_YElLogSoloLlevaElTipo()
    {
        _protector.Falla = new CryptographicException($"detalle interno {Centinela}");

        var act = () => Llamar(Servicio());

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.Internal);
        ex.Status.Detail.Should().NotContain(Centinela).And.NotContain("detalle interno");
        _logger.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Error && e.Texto.Contains(nameof(CryptographicException), StringComparison.Ordinal));
        _logger.Entradas.Should().OnlyContain(e => !e.Texto.Contains(Centinela, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaLlaveNuncaApareceEnLosLogs()
    {
        await Llamar(Servicio());
        _credenciales.Fila = null;
        await Llamar(Servicio());
        await FluentActions.Awaiting(() => Llamar(Servicio(), "svc-diagnostico")).Should().ThrowAsync<RpcException>();

        _logger.Entradas.Should().HaveCount(3);
        _logger.Entradas.Should().OnlyContain(e => !e.Texto.Contains(Centinela, StringComparison.Ordinal)
            && e.Campos.All(c => c.Value == null || !c.Value.Contains(Centinela, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LaEntregaDelHandler_NoMuestraLaLlaveEnToString()
    {
        var entrega = await new GetTenantProductCredentialHandler(_acceso, _credenciales, _protector).HandleAsync(Empresa, Ct);

        entrega.Value.Should().Be(Centinela);
        entrega.ToString().Should().NotContain(Centinela);
        _credenciales.Fila!.ToString().Should().NotContain("cifrado-opaco");
    }

    // ── HU #14694 — ObtenerCredencialImpuestoParaCopia (D-CRED-DIAG, DA-3 residual) ───────────────────────────────

    [Fact]
    public async Task Copia_SvcImpuestos_RecibeLaLlaveAunqueDiagnosticoEsteApagado_YSeAudita()
    {
        _acceso.Encendidas = [];

        var r = await Copiar(Servicio());

        r.Existe.Should().BeTrue();
        r.ApiKey.Should().Be(Centinela);
        r.Version.Should().Be(7);
        _credenciales.LecturasParaCopia.Should().Equal((Empresa, "diagnostico", "impuesto_api_key", "svc-impuestos", true));
        _logger.Entradas.Should().ContainSingle(e => e.Texto.Contains("impuesto.credencial.copia.entregada", StringComparison.Ordinal));
        _logger.Entradas.Should().OnlyContain(e => !e.Texto.Contains(Centinela, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Copia_SinFila_ExisteFalse_Version0_SinDescifrar_YTambienSeAudita()
    {
        _credenciales.Fila = null;

        var r = await Copiar(Servicio());

        r.Existe.Should().BeFalse();
        r.ApiKey.Should().BeEmpty();
        r.Version.Should().Be(0);
        _protector.Descifrados.Should().BeEmpty();
        _credenciales.LecturasParaCopia.Should().Equal((Empresa, "diagnostico", "impuesto_api_key", "svc-impuestos", false));
    }

    [Theory]
    [InlineData("svc-consultas")]
    [InlineData("svc-diagnostico")]
    public async Task Copia_OtroCliente_PermissionDenied_ClienteNoPermitido_SinLeer(string cliente)
    {
        var act = () => Copiar(Servicio(), cliente);

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.PermissionDenied);
        PlatformRpcErrors.Reason(ex).Should().Be("CLIENTE_NO_PERMITIDO");
        _credenciales.Consultas.Should().BeEmpty();
        _credenciales.LecturasParaCopia.Should().BeEmpty();
    }

    [Fact]
    public async Task Copia_SinEmpresa_InvalidArgument()
    {
        var act = () => Copiar(Servicio(), empresa: Guid.Empty);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        _credenciales.LecturasParaCopia.Should().BeEmpty();
    }

    [Fact]
    public async Task Copia_ErrorInesperado_InternalGenerico_SinLaLlave()
    {
        _protector.Falla = new CryptographicException($"detalle interno {Centinela}");

        var act = () => Copiar(Servicio());

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.Internal);
        ex.Status.Detail.Should().NotContain(Centinela);
        _logger.Entradas.Should().OnlyContain(e => !e.Texto.Contains(Centinela, StringComparison.Ordinal));
    }

    [Fact]
    public void CadaRpc_ExigeSuScope_ElDeCopiaNoAbreLaEntregaAConsultas()
    {
        const string Servicio = "/flit.identidad.v1.CredencialesProductoService/";

        ServiceTokenPorMetodoInterceptor.Requisito(Servicio + "ObtenerCredencialImpuestoParaCopia", CredencialesProductoGrpcService.Scope, true)
            .Should().Be(("platform.identidad.credenciales.impuesto.copia", true));
        ServiceTokenPorMetodoInterceptor.Requisito(Servicio + "ObtenerCredencialImpuesto", CredencialesProductoGrpcService.Scope, true)
            .Should().Be((CredencialesProductoGrpcService.Scope, true));
        CredencialesProductoGrpcService.ScopeCopia.Should().Be("platform.identidad.credenciales.impuesto.copia");
    }

    // ── HU #14694 (M04b, D-ALTA-IDEM) — ActualizarCredencialImpuestoDiagnostico ────────────────────────────────────

    [Fact]
    public async Task Actualizar_SvcImpuestos_ConFila_ReemplazaCifrada_YAuditaConElCliente_SinLaLlaveEnLogs()
    {
        _acceso.Encendidas = [];

        var r = await Actualizar(Servicio());

        r.Actualizada.Should().BeTrue("hay fila diagnostico/impuesto_api_key; no depende de que Diagnóstico esté encendido");
        _protector.Cifrados.Should().Equal((Empresa, "impuesto_api_key", LlaveNueva));
        _credenciales.Reemplazos.Should().Equal((Empresa, "diagnostico", "impuesto_api_key", "cifrado:" + LlaveNueva, "svc-impuestos"));
        _logger.Entradas.Should().ContainSingle(e => e.Texto.Contains("impuesto.credencial.diagnostico.actualizada", StringComparison.Ordinal));
        _logger.Entradas.Should().OnlyContain(e => !e.Texto.Contains(LlaveNueva, StringComparison.Ordinal) && !e.Texto.Contains(Centinela, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Actualizar_SinFila_ActualizadaFalse_NoCrea()
    {
        _credenciales.Fila = null;

        var r = await Actualizar(Servicio());

        r.Actualizada.Should().BeFalse();
        _credenciales.Reemplazos.Should().ContainSingle("el repositorio decide sin fila y no crea");
        _credenciales.Creadas.Should().Be(0);
        _logger.Entradas.Should().ContainSingle(e => e.Texto.Contains("impuesto.credencial.diagnostico.sin_fila", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("svc-consultas")]
    [InlineData("svc-diagnostico")]
    public async Task Actualizar_OtroCliente_PermissionDenied_SinEscribir(string cliente)
    {
        var act = () => Actualizar(Servicio(), cliente: cliente);

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.PermissionDenied);
        PlatformRpcErrors.Reason(ex).Should().Be("CLIENTE_NO_PERMITIDO");
        _credenciales.Reemplazos.Should().BeEmpty();
        _protector.Cifrados.Should().BeEmpty();
    }

    [Fact]
    public async Task Actualizar_SinEmpresa_InvalidArgument()
    {
        var act = () => Actualizar(Servicio(), empresa: Guid.Empty);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        _credenciales.Reemplazos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("llave con espacios 0123456789")]
    public async Task Actualizar_LlaveInvalida_InvalidArgument_CredencialInvalida_SinEscribir(string llave)
    {
        var act = () => Actualizar(Servicio(), apiKey: llave);

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.InvalidArgument);
        PlatformRpcErrors.Reason(ex).Should().Be("CREDENCIAL_INVALIDA");
        _credenciales.Reemplazos.Should().BeEmpty();
    }

    [Fact]
    public async Task Actualizar_ErrorInesperado_InternalGenerico_SinLaLlave()
    {
        _credenciales.FallaReemplazo = new InvalidOperationException($"detalle interno {LlaveNueva}");

        var act = () => Actualizar(Servicio());

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.Internal);
        ex.Status.Detail.Should().NotContain(LlaveNueva);
        _logger.Entradas.Should().OnlyContain(e => !e.Texto.Contains(LlaveNueva, StringComparison.Ordinal));
    }

    [Fact]
    public void Actualizar_ExigeSuScopePropio_ConEmpresa()
    {
        ServiceTokenPorMetodoInterceptor.Requisito(
                "/flit.identidad.v1.CredencialesProductoService/ActualizarCredencialImpuestoDiagnostico", CredencialesProductoGrpcService.Scope, true)
            .Should().Be(("platform.identidad.credenciales.impuesto.actualizar", true));
        CredencialesProductoGrpcService.ScopeActualizar.Should().Be("platform.identidad.credenciales.impuesto.actualizar");
    }

    private static ClaimsPrincipal Usuario(string sub, string aud, string scope) =>
        new(new ClaimsIdentity([new Claim("sub", sub), new Claim("aud", aud), new Claim("scope", scope)], "Bearer"));

    private sealed class Acceso : IProductAccessStore
    {
        public IReadOnlyList<Guid> Cadena { get; set; } = [];

        public HashSet<Guid> Encendidas { get; set; } = [];

        public Task<IReadOnlyList<Guid>> GetTenantChainAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(Cadena);

        public Task<IReadOnlySet<Guid>> GetTenantsWithProductEnabledAsync(IReadOnlyList<Guid> tenantIds, string productCode, CancellationToken ct) =>
            Task.FromResult<IReadOnlySet<Guid>>(productCode == "diagnostico" ? tenantIds.Where(Encendidas.Contains).ToHashSet() : new HashSet<Guid>());

        public Task<UserProductGrants> GetUserGrantsAsync(Guid userId, Guid tenantId, string productCode, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class Credenciales : ITenantProductCredentialRepository
    {
        public TenantProductCredential? Fila { get; set; }

        public List<(Guid, string, string)> Consultas { get; } = [];

        public Task<TenantProductCredential?> GetAsync(Guid tenantId, string productCode, string credentialCode, CancellationToken cancellationToken = default)
        {
            Consultas.Add((tenantId, productCode, credentialCode));
            return Task.FromResult(Fila is { } f && f.TenantId == tenantId ? f : null);
        }

        public Task<bool> ExistsAsync(Guid tenantId, string productCode, string credentialCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Fila is not null);

        public Task<TenantProductCredentialWrite> SetAsync(Guid tenantId, string productCode, string credentialCode, string secretProtected, Guid? changedBy, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid tenantId, string productCode, string credentialCode, Guid? changedBy, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public List<(Guid, string, string, string, bool)> LecturasParaCopia { get; } = [];

        public Task RegistrarLecturaParaCopiaAsync(Guid tenantId, string productCode, string credentialCode, string lector, bool existe, CancellationToken cancellationToken = default)
        {
            LecturasParaCopia.Add((tenantId, productCode, credentialCode, lector, existe));
            return Task.CompletedTask;
        }

        public List<(Guid, string, string, string, string)> Reemplazos { get; } = [];

        public int Creadas { get; private set; }

        public Exception? FallaReemplazo { get; set; }

        public Task<bool> ReplaceExistingFromServiceAsync(Guid tenantId, string productCode, string credentialCode, string secretProtected, string servicio, CancellationToken cancellationToken = default)
        {
            if (FallaReemplazo is not null)
                throw FallaReemplazo;
            Reemplazos.Add((tenantId, productCode, credentialCode, secretProtected, servicio));
            return Task.FromResult(Fila is { } f && f.TenantId == tenantId);
        }

        // HU #14711: el alta del proveedor no pasa por estos rpc.
        public Task<DateTimeOffset> GuardarAltaProveedorAsync(
            Guid tenantId, string productCode, IReadOnlyList<EscrituraCredencialAlta> escrituras, string motivo, Guid? changedBy,
            Action<DateTimeOffset>? antesDeGuardar, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Protector : ITenantProductSecretProtector
    {
        public string Plano { get; set; } = string.Empty;

        public Exception? Falla { get; set; }

        public List<(Guid, string, string)> Descifrados { get; } = [];

        public List<(Guid, string, string)> Cifrados { get; } = [];

        public string Protect(Guid tenantId, string credentialCode, string value)
        {
            Cifrados.Add((tenantId, credentialCode, value));
            return "cifrado:" + value;
        }

        public string Unprotect(Guid tenantId, string credentialCode, string protectedValue)
        {
            if (Falla is not null)
                throw Falla;
            Descifrados.Add((tenantId, credentialCode, protectedValue));
            return Plano;
        }
    }

    private sealed class LoggerCapturado : ILogger<CredencialesProductoGrpcService>
    {
        public List<(LogLevel Nivel, string Texto, List<KeyValuePair<string, string?>> Campos)> Entradas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var campos = state is IEnumerable<KeyValuePair<string, object?>> kv
                ? kv.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value?.ToString())).ToList()
                : [];
            Entradas.Add((logLevel, formatter(state, exception) + (exception?.ToString() ?? string.Empty), campos));
        }
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

        protected override string MethodCore => "/flit.identidad.v1.CredencialesProductoService/ObtenerCredencialImpuesto";
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
