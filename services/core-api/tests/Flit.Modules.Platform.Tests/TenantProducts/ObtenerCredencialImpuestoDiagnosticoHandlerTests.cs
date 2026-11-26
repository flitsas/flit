using System.Security.Cryptography;
using System.Text;
using Flit.Modules.Platform.Application.Companies;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// HU #14711 (AC-DC-01, AC-DC-02, AC-DC-04, AC-DC-05, AC-DC-12; plan §3.1 y §3.6) — obtener del proveedor la credencial del
/// impuesto de Diagnóstico con el alta idempotente: NIT base sin DV, una sola escritura atómica de la llave y el secreto (el
/// secreto viejo pasa a <c>impuesto_webhook_secret_anterior</c>), nada si los valores son los mismos, trabajo para Impuestos
/// solo si cambió la llave y ningún valor en logs ni errores.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <c>var r = await handler.HandleAsync(tenantId, superAdminId, ct); // { ApiKeyPrefix, Created, Rotated, HasWebhookSecret, Changed, UpdatedAt }</c>.
/// </remarks>
public sealed class ObtenerCredencialImpuestoDiagnosticoHandlerTests
{
    private const string Llave = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";
    private const string LlaveVieja = "ViEjAaaaIjKlMnOpQrStUvWxYz0123456789-_zzzzz";
    private const string Secreto = "whsec_CENTINELA_secreto_nuevo_0123456789";
    private const string SecretoViejo = "whsec_CENTINELA_secreto_viejo_9876543210";
    private const string Diagnostico = "diagnostico";
    private const string CodigoLlave = "impuesto_api_key";
    private const string CodigoSecreto = "impuesto_webhook_secret";
    private const string CodigoAnterior = "impuesto_webhook_secret_anterior";

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SuperAdmin = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Antes = Ahora.AddDays(-3);

    private readonly ITenantProductRepository _products = Substitute.For<ITenantProductRepository>();
    private readonly IDatosAltaProveedorStore _datos = Substitute.For<IDatosAltaProveedorStore>();
    private readonly IClienteAltaProveedorImpuesto _proveedor = Substitute.For<IClienteAltaProveedorImpuesto>();
    private readonly ITenantProductCredentialRepository _credenciales = Substitute.For<ITenantProductCredentialRepository>();
    private readonly ITenantProductSecretProtector _protector = Substitute.For<ITenantProductSecretProtector>();
    private readonly ITenantProductChangeJobs _jobs = Substitute.For<ITenantProductChangeJobs>();
    private readonly LoggerDeCaptura<ObtenerCredencialImpuestoDiagnosticoHandler> _log = new();
    private readonly List<IReadOnlyList<EscrituraCredencialAlta>> _guardados = [];
    private readonly List<string> _motivos = [];
    private bool _dentroDelGuardado;

    public ObtenerCredencialImpuestoDiagnosticoHandlerTests()
    {
        _products.GetAsync(TenantId, Diagnostico, Arg.Any<CancellationToken>())
            .Returns(new TenantProduct(TenantId, Diagnostico, true, null, Antes, null));
        _datos.ObtenerAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new DatosEmpresaParaAlta("Flota Andina SAS", "900.123.456-8", true, [], "ops@flota.example"));
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .Returns(new RespuestaAltaProveedor(Llave, Secreto, Created: true, ApiKeyRotated: false));
        _protector.Protect(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => Cifrar(ci.ArgAt<string>(1), ci.ArgAt<string>(2)));
        _protector.Unprotect(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => Descifrar(ci.ArgAt<string>(1), ci.ArgAt<string>(2)));
        _credenciales.GuardarAltaProveedorAsync(
                default, default!, default!, default!, default, default, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                _guardados.Add(ci.ArgAt<IReadOnlyList<EscrituraCredencialAlta>>(2));
                _motivos.Add(ci.ArgAt<string>(3));
                _dentroDelGuardado = true;
                ci.ArgAt<Action<DateTimeOffset>?>(5)?.Invoke(Ahora);
                _dentroDelGuardado = false;
                return Ahora;
            });
        _jobs.When(j => j.EncolarCredencialDiagnosticoActualizada(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>()))
            .Do(_ => _dentroDelGuardado.Should().BeTrue("el trabajo viaja en el mismo guardado de las credenciales"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ObtenerCredencialImpuestoDiagnosticoHandler Handler() =>
        new(_products, _datos, _proveedor, _credenciales, _protector, _jobs, _log);

    private static string Cifrar(string codigo, string valor) =>
        $"cif:{codigo}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(valor))}";

    private static string Descifrar(string codigo, string cifrado)
    {
        var prefijo = $"cif:{codigo}:";
        cifrado.Should().StartWith(prefijo, "cada valor se descifra con su propio código");
        return Encoding.UTF8.GetString(Convert.FromBase64String(cifrado[prefijo.Length..]));
    }

    private void Guardada(string codigo, string valor, long rowVersion = 3, DateTimeOffset? updatedAt = null) =>
        _credenciales.GetAsync(TenantId, Diagnostico, codigo, Arg.Any<CancellationToken>())
            .Returns(new TenantProductCredential(TenantId, Diagnostico, codigo, Cifrar(codigo, valor), rowVersion, updatedAt ?? Antes));

    // ── AC-DC-01: alta 201, una sola escritura atómica ─────────────────────────────────────────────────────

    [Fact]
    public async Task Alta201_GuardaLlaveYSecreto_EnUnSoloGuardado_YEncolaElTrabajoDeImpuestos()
    {
        var r = await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        r.Should().Be(new ResultadoObtencionCredencialImpuesto("AbCdEfGh", Created: true, Rotated: false, HasWebhookSecret: true, Changed: true, Ahora));
        _guardados.Should().ContainSingle();
        _guardados[0].Should().BeEquivalentTo(
            [new EscrituraCredencialAlta(CodigoLlave, Cifrar(CodigoLlave, Llave)), new EscrituraCredencialAlta(CodigoSecreto, Cifrar(CodigoSecreto, Secreto))]);
        _motivos.Should().Equal(CodigosAltaCredencialImpuesto.AuditoriaCredencialObtenida);
        await _credenciales.Received(1).GuardarAltaProveedorAsync(
            TenantId, Diagnostico, Arg.Any<IReadOnlyList<EscrituraCredencialAlta>>(), Arg.Any<string>(), SuperAdmin, Arg.Any<Action<DateTimeOffset>?>(), Arg.Any<CancellationToken>());
        _jobs.Received(1).EncolarCredencialDiagnosticoActualizada(TenantId, 0, Ahora);
    }

    [Fact]
    public async Task EnviaElNitBaseSinDv_LaRazonSocialYElCorreoDelAdmin()
    {
        await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        await _proveedor.Received(1).DarDeAltaAsync(
            new PeticionAltaProveedor("Flota Andina SAS", "900123456", "ops@flota.example"), Arg.Any<CancellationToken>());
    }

    // ── AC-DC-02: 200 con los mismos valores no cambia nada ────────────────────────────────────────────────

    [Fact]
    public async Task Alta200_ConLosMismosValores_NoEscribeNiSubeVersionNiAuditaNiEncola()
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .Returns(new RespuestaAltaProveedor(Llave, Secreto, Created: false, ApiKeyRotated: false));
        Guardada(CodigoLlave, Llave, updatedAt: Antes);
        Guardada(CodigoSecreto, Secreto);

        var r = await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        r.Should().Be(new ResultadoObtencionCredencialImpuesto("AbCdEfGh", Created: false, Rotated: false, HasWebhookSecret: true, Changed: false, Antes));
        await _credenciales.DidNotReceiveWithAnyArgs().GuardarAltaProveedorAsync(default, default!, default!, default!, default, default, Ct);
        _jobs.DidNotReceiveWithAnyArgs().EncolarCredencialDiagnosticoActualizada(default, default, default);
    }

    // ── AC-DC-04: rotación de la llave y del secreto ───────────────────────────────────────────────────────

    [Fact]
    public async Task LlaveDistinta_EsClaveRotada_ReemplazaYEncolaConLaVersionSiguiente()
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .Returns(new RespuestaAltaProveedor(Llave, Secreto, Created: false, ApiKeyRotated: true));
        Guardada(CodigoLlave, LlaveVieja, rowVersion: 7);
        Guardada(CodigoSecreto, Secreto);

        var r = await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        r.Rotated.Should().BeTrue();
        r.Changed.Should().BeTrue();
        _guardados.Single().Should().BeEquivalentTo([new EscrituraCredencialAlta(CodigoLlave, Cifrar(CodigoLlave, Llave))]);
        _motivos.Should().Equal(CodigosAltaCredencialImpuesto.AuditoriaClaveRotada);
        _jobs.Received(1).EncolarCredencialDiagnosticoActualizada(TenantId, 8, Ahora);
    }

    [Fact]
    public async Task SecretoDistinto_ElAnteriorPasaAImpuestoWebhookSecretAnterior_YSinCambioDeLlaveNoEncola()
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .Returns(new RespuestaAltaProveedor(Llave, Secreto, Created: false, ApiKeyRotated: false));
        Guardada(CodigoLlave, Llave);
        Guardada(CodigoSecreto, SecretoViejo);

        var r = await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        r.Should().Be(new ResultadoObtencionCredencialImpuesto("AbCdEfGh", Created: false, Rotated: false, HasWebhookSecret: true, Changed: true, Antes));
        _guardados.Single().Should().BeEquivalentTo(
        [
            new EscrituraCredencialAlta(CodigoSecreto, Cifrar(CodigoSecreto, Secreto)),
            new EscrituraCredencialAlta(CodigoAnterior, Cifrar(CodigoAnterior, SecretoViejo)),
        ]);
        _motivos.Should().Equal(CodigosAltaCredencialImpuesto.AuditoriaCredencialObtenida);
        _jobs.DidNotReceiveWithAnyArgs().EncolarCredencialDiagnosticoActualizada(default, default, default);
    }

    [Fact]
    public async Task ProveedorSinSecreto_SoloGuardaLaLlave_YConservaElSecretoQueHabia()
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .Returns(new RespuestaAltaProveedor(Llave, null, Created: false, ApiKeyRotated: false));
        Guardada(CodigoSecreto, SecretoViejo);

        var r = await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        r.HasWebhookSecret.Should().BeTrue();
        _guardados.Single().Should().BeEquivalentTo([new EscrituraCredencialAlta(CodigoLlave, Cifrar(CodigoLlave, Llave))]);
    }

    // ── AC-DC-05: errores funcionales antes del proveedor ──────────────────────────────────────────────────

    [Theory]
    [InlineData("", null, "NIT_AUSENTE")]
    [InlineData("9001234567", null, "NIT_AMBIGUO")]
    [InlineData("900123456-8", "900123456", "NIT_IGUAL_A_CABEZA")]
    public async Task NitNoValido_ErrorFuncional_SinLlamarAlProveedorNiGuardar(string taxId, string? cabeza, string codigo)
    {
        _datos.ObtenerAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new DatosEmpresaParaAlta("Flota Andina SAS", taxId, true, cabeza is null ? [] : [cabeza], "ops@flota.example"));

        var act = () => Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(codigo);
        await _proveedor.DidNotReceiveWithAnyArgs().DarDeAltaAsync(default!, Ct);
        await _credenciales.DidNotReceiveWithAnyArgs().GuardarAltaProveedorAsync(default, default!, default!, default!, default, default, Ct);
    }

    [Fact]
    public async Task SinAdminCompanyActivo_SinAdmin_SinLlamarAlProveedor()
    {
        _datos.ObtenerAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new DatosEmpresaParaAlta("Flota Andina SAS", "900123456", true, [], null));

        var act = () => Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(CodigosAltaCredencialImpuesto.SinAdmin);
        await _proveedor.DidNotReceiveWithAnyArgs().DarDeAltaAsync(default!, Ct);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DiagnosticoApagadoOSinFila_ProductoNoHabilitado(bool conFila)
    {
        _products.GetAsync(TenantId, Diagnostico, Arg.Any<CancellationToken>())
            .Returns(conFila ? new TenantProduct(TenantId, Diagnostico, false, null, Antes, null) : null);

        var act = () => Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(CodigosAltaCredencialImpuesto.ProductoNoHabilitado);
        await _proveedor.DidNotReceiveWithAnyArgs().DarDeAltaAsync(default!, Ct);
    }

    [Fact]
    public async Task EmpresaInexistente_TenantNotFound()
    {
        _datos.ObtenerAsync(TenantId, Arg.Any<CancellationToken>()).Returns((DatosEmpresaParaAlta?)null);

        var act = () => Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(CodigosAltaCredencialImpuesto.TenantNotFound);
    }

    // ── AC-DC-05: errores del proveedor (401, 404, 5xx tras el reintento, sin token) ───────────────────────

    [Theory]
    [InlineData("PROVEEDOR_TOKEN_INVALIDO")]
    [InlineData("PROVEEDOR_INTEGRACION_DESHABILITADA")]
    [InlineData("PROVEEDOR_SOLICITUD_INVALIDA")]
    [InlineData("PROVEEDOR_NO_DISPONIBLE")]
    [InlineData("PROVEEDOR_NO_CONFIGURADO")]
    public async Task ErrorDelProveedor_SePropagaConSuCodigo_SinGuardarNiEncolar(string codigo)
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AltaCredencialImpuestoException(codigo));

        var act = () => Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(codigo);
        await _credenciales.DidNotReceiveWithAnyArgs().GuardarAltaProveedorAsync(default, default!, default!, default!, default, default, Ct);
        _jobs.DidNotReceiveWithAnyArgs().EncolarCredencialDiagnosticoActualizada(default, default, default);
    }

    // ── AC-DC-01: atomicidad ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FallaElGuardado_NoQuedaTrabajoFueraDeLaTransaccion()
    {
        _credenciales.GuardarAltaProveedorAsync(default, default!, default!, default!, default, default, Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("caída de la base de prueba"));

        var act = () => Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _jobs.DidNotReceiveWithAnyArgs().EncolarCredencialDiagnosticoActualizada(default, default, default);
    }

    // ── M-2: un paso previo que falla no llama al proveedor ────────────────────────────────────────────────

    [Fact]
    public async Task DescifrarLoGuardadoFalla_NoLlamaAlProveedor()
    {
        Guardada(CodigoLlave, LlaveVieja);
        _protector.Unprotect(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Throws(new CryptographicException("rota"));

        var act = () => Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        await act.Should().ThrowAsync<CryptographicException>();
        await _proveedor.DidNotReceiveWithAnyArgs().DarDeAltaAsync(default!, Ct);
    }

    // ── M-3: el botón y la ronda guardan a la vez ──────────────────────────────────────────────────────────

    private void ChocaAlGuardar() =>
        _credenciales.GuardarAltaProveedorAsync(default, default!, default!, default!, default, default, Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new TenantProductCredentialConflictException());

    [Fact]
    public async Task ChoqueAlGuardar_LaOtraEscrituraDejoLosMismosValores_SinCambios()
    {
        ChocaAlGuardar();
        _credenciales.GetAsync(TenantId, Diagnostico, CodigoLlave, Arg.Any<CancellationToken>())
            .Returns(null, new TenantProductCredential(TenantId, Diagnostico, CodigoLlave, Cifrar(CodigoLlave, Llave), 0, Ahora));
        _credenciales.GetAsync(TenantId, Diagnostico, CodigoSecreto, Arg.Any<CancellationToken>())
            .Returns(null, new TenantProductCredential(TenantId, Diagnostico, CodigoSecreto, Cifrar(CodigoSecreto, Secreto), 0, Ahora));

        var r = await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        r.Should().Be(new ResultadoObtencionCredencialImpuesto("AbCdEfGh", Created: true, Rotated: false, HasWebhookSecret: true, Changed: false, Ahora));
        await _credenciales.ReceivedWithAnyArgs(1).GuardarAltaProveedorAsync(default, default!, default!, default!, default, default, Ct);
    }

    [Fact]
    public async Task ChoqueAlGuardar_SigueDistinto_AltaEnCurso_ConSuLiteral()
    {
        ChocaAlGuardar();
        _credenciales.GetAsync(TenantId, Diagnostico, CodigoLlave, Arg.Any<CancellationToken>())
            .Returns(null, new TenantProductCredential(TenantId, Diagnostico, CodigoLlave, Cifrar(CodigoLlave, LlaveVieja), 0, Ahora));

        var act = () => Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        var ex = (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which;
        ex.Code.Should().Be("ALTA_EN_CURSO");
        ex.Message.Should().Be("Ya hay un alta de la credencial del impuesto en curso para la compañía. Intenta de nuevo en unos segundos.");
        await _credenciales.Received(1).RegistrarAuditoriaAltaProveedorAsync(TenantId, Diagnostico, "ALTA_EN_CURSO", false, SuperAdmin, Arg.Any<CancellationToken>());
    }

    // ── L-3: la fila resumen del alta también sin cambios y en los fallos ──────────────────────────────────

    [Fact]
    public async Task SinCambios_AuditaLaFilaResumenConSinCambios()
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .Returns(new RespuestaAltaProveedor(Llave, Secreto, Created: false, ApiKeyRotated: false));
        Guardada(CodigoLlave, Llave);
        Guardada(CodigoSecreto, Secreto);

        await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        await _credenciales.Received(1).RegistrarAuditoriaAltaProveedorAsync(TenantId, Diagnostico, "SIN_CAMBIOS", true, SuperAdmin, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConCambios_NoDuplicaLaFilaResumen()
    {
        await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        await _credenciales.DidNotReceiveWithAnyArgs().RegistrarAuditoriaAltaProveedorAsync(default, default!, default!, default, default, Ct);
    }

    [Theory]
    [InlineData("PROVEEDOR_TOKEN_INVALIDO")]
    [InlineData("PROVEEDOR_NO_DISPONIBLE")]
    public async Task FallaDelProveedor_AuditaElCodigo(string codigo)
    {
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>()).ThrowsAsync(new AltaCredencialImpuestoException(codigo));

        await FluentActions.Awaiting(() => Handler().HandleAsync(TenantId, SuperAdmin, Ct)).Should().ThrowAsync<AltaCredencialImpuestoException>();

        await _credenciales.Received(1).RegistrarAuditoriaAltaProveedorAsync(TenantId, Diagnostico, codigo, false, SuperAdmin, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FallaDeValidacion_AuditaElCodigo_YLaEmpresaInexistenteNo()
    {
        _datos.ObtenerAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new DatosEmpresaParaAlta("Flota Andina SAS", "9001234567", true, [], "ops@flota.example"));
        await FluentActions.Awaiting(() => Handler().HandleAsync(TenantId, SuperAdmin, Ct)).Should().ThrowAsync<AltaCredencialImpuestoException>();
        var otra = Guid.NewGuid();
        await FluentActions.Awaiting(() => Handler().HandleAsync(otra, SuperAdmin, Ct)).Should().ThrowAsync<AltaCredencialImpuestoException>();

        await _credenciales.Received(1).RegistrarAuditoriaAltaProveedorAsync(TenantId, Diagnostico, "NIT_AMBIGUO", false, SuperAdmin, Arg.Any<CancellationToken>());
        await _credenciales.DidNotReceive().RegistrarAuditoriaAltaProveedorAsync(otra, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // ── NIT enmascarado en la petición ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void PeticionAltaProveedor_ToString_SoloLosTresUltimosDigitosDelNit()
    {
        var texto = new PeticionAltaProveedor("Flota Andina SAS", "900123456", "ops@flota.example").ToString();

        texto.Should().Contain("***456").And.NotContain("900123456").And.NotContain("123").And.NotContain("ops@flota.example");
    }

    // ── AC-DC-12: nada sensible en logs ni en errores ──────────────────────────────────────────────────────

    [Fact]
    public async Task LogsYErrores_NuncaLlevanLaLlaveNiElSecreto()
    {
        Guardada(CodigoLlave, LlaveVieja);
        Guardada(CodigoSecreto, SecretoViejo);
        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .Returns(new RespuestaAltaProveedor(Llave, Secreto, Created: false, ApiKeyRotated: true));
        var r = await Handler().HandleAsync(TenantId, SuperAdmin, Ct);

        _proveedor.DarDeAltaAsync(Arg.Any<PeticionAltaProveedor>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AltaCredencialImpuestoException(CodigosAltaCredencialImpuesto.ProveedorTokenInvalido));
        var error = await FluentActions.Awaiting(() => Handler().HandleAsync(TenantId, SuperAdmin, Ct))
            .Should().ThrowAsync<AltaCredencialImpuestoException>();

        _log.Entradas.Should().NotBeEmpty("el alta deja rastro (sin valores)");
        var textos = _log.Entradas.Select(e => e.Mensaje + " " + e.Excepcion).Append(error.Which.ToString()).Append(r.ToString());
        foreach (var texto in textos)
        {
            foreach (var sensible in new[] { Llave, LlaveVieja, Secreto, SecretoViejo, "ops@flota.example" })
                texto.Should().NotContain(sensible);
        }
    }
}

/// <summary>Logger que guarda cada entrada con su mensaje ya formateado.</summary>
internal sealed class LoggerDeCaptura<T> : ILogger<T>
{
    public List<(LogLevel Nivel, string Mensaje, string? Excepcion)> Entradas { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entradas.Add((logLevel, formatter(state, exception), exception?.ToString()));
}
