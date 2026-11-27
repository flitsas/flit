using System.Security.Cryptography;
using Flit.Modules.Platform.Application.Companies;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// HU #14711 (AC-DC-03; plan §3.1 punto 1) — el alta automática que deja en cola habilitar <c>diagnostico</c>: llama al alta
/// aunque la empresa ya tenga credenciales (idempotente), deja <c>LISTO</c> o <c>LISTO_SIN_WEBHOOK</c>, reintenta «no salió»
/// con la espera escalonada de Impuestos (1, 5, 15 y 60 min, tope de 5 intentos) y no reintenta los errores definitivos.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>await automatica.ProcesarAsync(new PendienteAltaDiagnostico(tenantId, 0), ct)</c> → un
/// <c>RegistrarEstadoAltaDiagnosticoAsync</c> con el estado siguiente.
/// </remarks>
public sealed class AltaCredencialDiagnosticoAutomaticaTests
{
    private const string Llave = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";
    private const string Secreto = "whsec_secreto_de_prueba_0123456789"; // gitleaks:allow
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly ITenantProductRepository _products = Substitute.For<ITenantProductRepository>();
    private readonly IDatosAltaProveedorStore _datos = Substitute.For<IDatosAltaProveedorStore>();
    private readonly IClienteAltaProveedorImpuesto _proveedor = Substitute.For<IClienteAltaProveedorImpuesto>();
    private readonly ITenantProductCredentialRepository _credenciales = Substitute.For<ITenantProductCredentialRepository>();
    private readonly ITenantProductSecretProtector _protector = Substitute.For<ITenantProductSecretProtector>();
    private readonly ITenantProductChangeJobs _jobs = Substitute.For<ITenantProductChangeJobs>();
    private readonly ITenantProductProvisioningStore _store = Substitute.For<ITenantProductProvisioningStore>();
    private readonly List<EstadoAltaDiagnostico> _estados = [];

    public AltaCredencialDiagnosticoAutomaticaTests()
    {
        _products.GetAsync(TenantId, "diagnostico", Arg.Any<CancellationToken>())
            .Returns(new TenantProduct(TenantId, "diagnostico", true, null, Ahora, null));
        _datos.ObtenerAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new DatosEmpresaParaAlta("Flota Andina SAS", "900123456", true, [], "ops@flota.example"));
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .Returns(new RespuestaAltaProveedor(Llave, Secreto, Created: false, ApiKeyRotated: false));
        _protector.Protect(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(ci => $"cif:{ci.ArgAt<string>(1)}:{ci.ArgAt<string>(2)}");
        _protector.Unprotect(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(2).Split(':', 3)[2]);
        _credenciales.GuardarAltaProveedorAsync(default, default!, default!, default!, default, default, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(Ahora);
        _store.RegistrarEstadoAltaDiagnosticoAsync(Arg.Do<EstadoAltaDiagnostico>(_estados.Add), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly LoggerDeCaptura<AltaCredencialDiagnosticoAutomatica> _log = new();

    private AltaCredencialDiagnosticoAutomatica Automatica() => new(
        new ObtenerCredencialImpuestoDiagnosticoHandler(
            _products, _datos, _proveedor, _credenciales, _protector, _jobs, NullLogger<ObtenerCredencialImpuestoDiagnosticoHandler>.Instance),
        _store,
        new RelojFijo(Ahora),
        _log);

    private void LlaveGuardada() =>
        _credenciales.GetAsync(TenantId, "diagnostico", "impuesto_api_key", Arg.Any<CancellationToken>())
            .Returns(new TenantProductCredential(TenantId, "diagnostico", "impuesto_api_key", $"cif:impuesto_api_key:{Llave}", 4, Ahora.AddDays(-1)));

    [Fact]
    public async Task Exito_QuedaListo_SinIntentosNiEspera()
    {
        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, 2), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(TenantId, "LISTO", null, null, null, 0, null, Ahora));
    }

    [Fact]
    public async Task Exito_SinSecretoDeWebhook_QuedaListoSinWebhook()
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .Returns(new RespuestaAltaProveedor(Llave, null, Created: true, ApiKeyRotated: false));

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, 0), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(
            TenantId, "LISTO_SIN_WEBHOOK", "SIN_WEBHOOK_SECRET", CodigosAltaCredencialImpuesto.Literal("SIN_WEBHOOK_SECRET"), "REINTENTAR", 0, null, Ahora));
    }

    [Fact]
    public async Task EmpresaQueYaTieneLlaveYSecreto_IgualLlamaAlAlta_SinCambios_YQuedaLista()
    {
        _credenciales.GetAsync(TenantId, "diagnostico", "impuesto_api_key", Arg.Any<CancellationToken>())
            .Returns(new TenantProductCredential(TenantId, "diagnostico", "impuesto_api_key", $"cif:impuesto_api_key:{Llave}", 4, Ahora.AddDays(-1)));
        _credenciales.GetAsync(TenantId, "diagnostico", "impuesto_webhook_secret", Arg.Any<CancellationToken>())
            .Returns(new TenantProductCredential(TenantId, "diagnostico", "impuesto_webhook_secret", $"cif:impuesto_webhook_secret:{Secreto}", 1, Ahora.AddDays(-1)));

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, 0), Ct);

        await _proveedor.Received(1).DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>());
        await _credenciales.DidNotReceiveWithAnyArgs().GuardarAltaProveedorAsync(default, default!, default!, default!, default, default, Ct);
        _jobs.DidNotReceiveWithAnyArgs().EncolarCredencialDiagnosticoActualizada(default, default, default);
        _estados.Single().Estado.Should().Be("LISTO");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 60)]
    [InlineData(4, 60)]
    public async Task NoSalio_SeReintentaConEsperaEscalonada(int intentosPrevios, int minutos)
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AltaCredencialImpuestoException(CodigosAltaCredencialImpuesto.ProveedorNoDisponible));

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, intentosPrevios), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(
            TenantId, "PENDIENTE", "PROVEEDOR_NO_DISPONIBLE", CodigosAltaCredencialImpuesto.Literal("PROVEEDOR_NO_DISPONIBLE"), null,
            intentosPrevios + 1, Ahora.AddMinutes(minutos), Ahora));
    }

    [Theory]
    [InlineData("PROVEEDOR_NO_DISPONIBLE")]
    [InlineData("PROVEEDOR_NO_CONFIGURADO")]
    public async Task AlTope_DeIntentos_QuedaEnError(string codigo)
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AltaCredencialImpuestoException(codigo));

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, AltaCredencialDiagnosticoAutomatica.MaximoIntentosAutomaticos), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(
            TenantId, "ERROR", codigo, CodigosAltaCredencialImpuesto.Literal(codigo), null, AltaCredencialDiagnosticoAutomatica.MaximoIntentosAutomaticos, null, Ahora));
    }

    [Theory]
    [InlineData("", "NIT_AUSENTE")]
    [InlineData("9001234567", "NIT_AMBIGUO")]
    public async Task NitNoValido_RequiereRevisionDelNit_SinReintento(string taxId, string codigo)
    {
        _datos.ObtenerAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new DatosEmpresaParaAlta("Flota Andina SAS", taxId, true, [], "ops@flota.example"));

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, 0), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(
            TenantId, "REQUIERE_REVISION_NIT", codigo, CodigosAltaCredencialImpuesto.Literal(codigo), "CORREGIR_NIT", 0, null, Ahora));
    }

    [Theory]
    [InlineData("PROVEEDOR_TOKEN_INVALIDO")]
    [InlineData("PROVEEDOR_INTEGRACION_DESHABILITADA")]
    [InlineData("PROVEEDOR_SOLICITUD_INVALIDA")]
    public async Task ErrorDefinitivoDelProveedor_QuedaEnError_SinReintento(string codigo)
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AltaCredencialImpuestoException(codigo));

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, 1), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(TenantId, "ERROR", codigo, CodigosAltaCredencialImpuesto.Literal(codigo), null, 1, null, Ahora));
    }

    [Fact]
    public async Task DiagnosticoApagadoAntesDeAtenderlo_QuedaSuspendido()
    {
        _products.GetAsync(TenantId, "diagnostico", Arg.Any<CancellationToken>())
            .Returns(new TenantProduct(TenantId, "diagnostico", false, null, Ahora, null));

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, 0), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(
            TenantId, "SUSPENDIDO", "PRODUCTO_NO_HABILITADO", CodigosAltaCredencialImpuesto.Literal("PRODUCTO_NO_HABILITADO"), null, 0, null, Ahora));
    }

    // ── M-1: el estado se guarda con la versión reservada; un resultado obsoleto se descarta ─────────────────

    [Fact]
    public async Task Registra_ConLaVersionReservada()
    {
        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, 0, 7), Ct);

        await _store.Received(1).RegistrarEstadoAltaDiagnosticoAsync(Arg.Any<EstadoAltaDiagnostico>(), 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResultadoObsoleto_SeDescarta_YQuedaEnElLog()
    {
        _store.RegistrarEstadoAltaDiagnosticoAsync(Arg.Any<EstadoAltaDiagnostico>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(false);

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, 0, 7), Ct);

        _log.Entradas.Should().Contain(e => e.Mensaje.Contains("descarta", StringComparison.Ordinal) && e.Mensaje.Contains(" 7", StringComparison.Ordinal));
    }

    // ── M-2: una excepción inesperada cuenta en los intentos y tiene tope ──────────────────────────────────

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 60)]
    public async Task ExcepcionInesperada_CuentaEnLosIntentos_PendienteConErrorInterno(int intentosPrevios, int minutos)
    {
        LlaveGuardada();
        _protector.Unprotect(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Throws(new CryptographicException("llave CENTINELA rota"));

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, intentosPrevios), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(
            TenantId, "PENDIENTE", "ERROR_INTERNO", "No se pudo completar el alta de la credencial del impuesto por un error interno.", null,
            intentosPrevios + 1, Ahora.AddMinutes(minutos), Ahora));
        await _proveedor.DidNotReceiveWithAnyArgs().DarDeAltaAsync(default!, Ct);
    }

    [Fact]
    public async Task ExcepcionInesperada_AlTope_QuedaEnErrorInterno_YElLogSoloLlevaElTipo()
    {
        _datos.ObtenerAsync(TenantId, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("detalle CENTINELA de la base"));

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, AltaCredencialDiagnosticoAutomatica.MaximoIntentosAutomaticos), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(
            TenantId, "ERROR", "ERROR_INTERNO", "No se pudo completar el alta de la credencial del impuesto por un error interno.", null,
            AltaCredencialDiagnosticoAutomatica.MaximoIntentosAutomaticos, null, Ahora));
        _log.Entradas.Should().Contain(e => e.Mensaje.Contains("InvalidOperationException", StringComparison.Ordinal));
        _log.Entradas.Should().NotContain(e => e.Mensaje.Contains("CENTINELA", StringComparison.Ordinal) || e.Excepcion != null);
    }

    // ── M-3: la ronda que choca con el botón se reintenta ──────────────────────────────────────────────────

    [Fact]
    public async Task ChoqueConElBoton_AltaEnCurso_SeReintentaConEspera()
    {
        _credenciales.GuardarAltaProveedorAsync(default, default!, default!, default!, default, default, Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new TenantProductCredentialConflictException());
        _credenciales.GetAsync(TenantId, "diagnostico", "impuesto_api_key", Arg.Any<CancellationToken>())
            .Returns(null, new TenantProductCredential(TenantId, "diagnostico", "impuesto_api_key", "cif:impuesto_api_key:OtraLlaveDelBotonDistinta0123456789", 0, Ahora)); // gitleaks:allow

        await Automatica().ProcesarAsync(new PendienteAltaDiagnostico(TenantId, 1), Ct);

        _estados.Should().Equal(new EstadoAltaDiagnostico(
            TenantId, "PENDIENTE", "ALTA_EN_CURSO",
            "Ya hay un alta de la credencial del impuesto en curso para la compañía. Intenta de nuevo en unos segundos.", null,
            2, Ahora.AddMinutes(5), Ahora));
    }

    [Fact]
    public void EsperaReintento_EsLaDeImpuestos()
    {
        Enumerable.Range(1, 6).Select(AltaCredencialDiagnosticoAutomatica.EsperaReintento).Select(t => t.TotalMinutes)
            .Should().Equal(1, 5, 15, 60, 60, 60);
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
