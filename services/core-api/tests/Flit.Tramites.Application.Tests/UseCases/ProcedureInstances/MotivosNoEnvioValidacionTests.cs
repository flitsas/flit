using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11665 — <b>motivos tipificados de no envío de la validación de identidad.</b>
///
/// <para><b>Qué se corrige.</b> El disparador de la parte jurídica omitía el envío en silencio: ni
/// error, ni log de negocio, ni señal en la UI. El gestor veía un trámite que no avanzaba y no tenía
/// forma de saber qué le faltaba. Y las omisiones por datos incompletos eran UN SOLO <c>continue</c>
/// con condición compuesta, así que ni el código sabía cuál de ellas había ocurrido.</para>
///
/// <para><b>Qué se ejercita.</b> El ESCRITOR (<see cref="PutActorsHandler"/> real, con un logger espía)
/// y el LECTOR (<see cref="ListBiometriaHandler"/> real) sobre la MISMA instancia y en la misma prueba.
/// Que los dos salgan de <see cref="EnvioValidacionBloqueoRules"/> no se afirma leyendo el código: se
/// comprueba que ambos dicen el mismo código para la misma parte.</para>
///
/// <para>Dos de los cuatro motivos de datos (<c>rl_sin_correo</c> y <c>sujeto_no_es_representante</c>)
/// NO son alcanzables por el PUT de actores: la captura ya exige correo del representante
/// (<c>rl_email_requerido</c>). Se ejercitan por el lector sobre actores ya guardados, y la regla pura
/// los fija aparte.</para>
///
/// <para>Uso de ejemplo:
/// <c>var (motivosEscritor, motivosLector) = await EjecutarAsync(CompradorJuridico());</c>
/// ⇒ ambos traen <c>rl_sin_documento</c> si al representante le falta el documento.</para>
/// </summary>
public sealed class MotivosNoEnvioValidacionTests
{
    private const string RlTipoDocumento = "CC";
    private const string RlDocumento = "1090123456";
    private const string RlEmail = "rep@empresa.com";
    private const string Nit = "900123456";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ICatalogRepository _catalogRepo = Substitute.For<ICatalogRepository>();
    private readonly BiometricsProviderOptions _providerOptions = new() { Provider = BiometricProviders.Kyverum };
    private readonly IKyverumVerifyClient _kyverumClient = Substitute.For<IKyverumVerifyClient>();
    private readonly IPersonDataConsentRepository _consentRepo = Substitute.For<IPersonDataConsentRepository>();
    private readonly ISignatureVaultPolicy _baul = Substitute.For<ISignatureVaultPolicy>();
    private readonly LoggerEspia _logger = new();
    private readonly PutActorsHandler _put;

    public MotivosNoEnvioValidacionTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var kyverumHandler = new IniciarKyverumVerifyHandler(
            _repo,
            _kyverumClient,
            new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(),
            Substitute.For<IIdentityValidationAuditLog>(),
            _baul);

        _put = new PutActorsHandler(
            _repo, _catalogRepo, _providerOptions, kyverumHandler, _consentRepo, _baul, _logger);

        _catalogRepo.GetProcedureEntityByCodeAsync("BUYER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = Guid.NewGuid(), Code = "BUYER", Name = "Comprador" });
        _catalogRepo.GetProcedureEntityByCodeAsync("OWNER", Arg.Any<CancellationToken>())
            .Returns(new ProcedureEntity { Id = Guid.NewGuid(), Code = "OWNER", Name = "Propietario" });

        _kyverumClient.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult("kyv_1", "https://capture/kyv_1", "whsec_x", "pending", "{}"));
    }

    // ── Un caso por motivo, con escritor y lector de acuerdo ──────────────────────────────────

    [Fact]
    public async Task ProveedorMock_ReportaProveedorNoEnvia()
    {
        // En mock no se envía nada, nunca. Antes era un `return` del método entero: ni siquiera se
        // miraban las partes siguientes, y desde fuera era indistinguible de "todo está bien".
        _providerOptions.Provider = BiometricProviders.Mock;

        var (escritor, lector) = await EjecutarAsync(CompradorJuridico());

        escritor.Should().ContainSingle().Which.Should().Be(EnvioValidacionMotivos.ProveedorNoEnvia);
        MotivoDe(lector, "comprador").Should().Be(EnvioValidacionMotivos.ProveedorNoEnvia);
        lector.Single().Informativo.Should().BeFalse("es un fallo de configuración, no una cobertura");
    }

    [Fact]
    public async Task RepresentanteSinDocumento_ReportaRlSinDocumento()
    {
        // El NIT no es validable biométricamente: sin documento del RL no hay a quién validar.
        var (escritor, lector) = await EjecutarAsync(CompradorJuridico(
            rl: new ActorRepresentanteLegal(null, null, "Ana Representante", RlEmail, null)));

        escritor.Should().ContainSingle().Which.Should().Be(EnvioValidacionMotivos.RlSinDocumento);
        MotivoDe(lector, "comprador").Should().Be(EnvioValidacionMotivos.RlSinDocumento);
    }

    [Fact]
    public async Task ActorJuridicoSinRepresentanteDeclarado_ReportaSujetoNoEsRepresentante()
    {
        // Sin bloque de representante en el metadata, el sujeto de identidad cae al actor (la empresa):
        // no es un RL al que le falte un dato, es que no hay RL. Se nombra distinto a propósito.
        //
        // Por el PUT de actores este caso NO se alcanza: la captura exige correo del RL
        // (`rl_email_requerido`) y sin bloque de RL no hay correo. Se ejercita por el LECTOR, que sí ve
        // actores guardados por otras vías (datos previos a esa validación, integraciones).
        var lector = await ListarAsync(ActorJuridico());

        MotivoDe(lector, "comprador").Should().Be(EnvioValidacionMotivos.SujetoNoEsRepresentante);
        lector.Single().Informativo.Should().BeFalse();
    }

    [Fact]
    public async Task RepresentanteSinCorreo_ReportaRlSinCorreo()
    {
        // El correo de la empresa no sirve: la validación la recibe quien puede biometrizarse. Mismo
        // caso que el anterior: el PUT lo rechaza antes (`rl_email_requerido`), así que el motivo existe
        // para los actores ya guardados; se ejercita por el lector.
        var metadata =
            $$$"""{"representanteLegal":{"tipoDocumento":"{{{RlTipoDocumento}}}","numeroDocumento":"{{{RlDocumento}}}","nombreCompleto":"Ana Representante"}}""";
        var lector = await ListarAsync(ActorJuridico(metadata));

        MotivoDe(lector, "comprador").Should().Be(EnvioValidacionMotivos.RlSinCorreo);
    }

    [Fact]
    public void LaReglaPura_DistingueLosCuatroMotivosDeDatos()
    {
        // El escritor solo puede producir dos de los cuatro (el PUT rechaza antes los otros dos), así
        // que la regla se fija también aquí: es la fuente única y debe saber nombrarlos todos.
        Motivo(new EnvioValidacionEstado(false, true, true, true, true, true))
            .Should().Be(EnvioValidacionMotivos.ProveedorNoEnvia);
        Motivo(new EnvioValidacionEstado(true, true, false, false, false, true))
            .Should().Be(EnvioValidacionMotivos.SujetoNoEsRepresentante);
        Motivo(new EnvioValidacionEstado(true, true, false, true, false, true))
            .Should().Be(EnvioValidacionMotivos.RlSinDocumento);
        Motivo(new EnvioValidacionEstado(true, true, true, true, true, false))
            .Should().Be(EnvioValidacionMotivos.RlSinCorreo);
        Motivo(new EnvioValidacionEstado(true, true, true, true, true, true)).Should().BeNull();
        Motivo(new EnvioValidacionEstado(false, false, false, false, false, false))
            .Should().BeNull("una persona natural no reporta motivos ni con el proveedor mock");

        static string? Motivo(EnvioValidacionEstado estado) =>
            EnvioValidacionBloqueoRules.Evaluar(estado)?.Codigo;
    }

    // ── Motivos informativos: no son fallos ───────────────────────────────────────────────────

    [Fact]
    public async Task CubiertoPorElBaul_ReportaMotivoInformativo()
    {
        // Tras la HU #11662 el disparador ya no pre-chequea el baúl: el motivo se deriva de la decisión
        // del evaluador (NoEnviar + cobertura_baul), no de una copia local de la regla.
        ConFirmaDelBaul();

        var (escritor, lector) = await EjecutarAsync(CompradorJuridico());

        escritor.Should().ContainSingle().Which.Should().Be(EnvioValidacionMotivos.CubiertoPorBaul);
        var motivo = lector.Should().ContainSingle().Subject;
        motivo.Codigo.Should().Be(EnvioValidacionMotivos.CubiertoPorBaul);
        motivo.Informativo.Should().BeTrue("la parte está cubierta: no hay nada que corregir");
    }

    [Fact]
    public async Task ConIdentidadVigente_ReportaRepresentanteUtilizableComoInformativo()
    {
        ConIdentidadVigente();

        var (escritor, lector) = await EjecutarAsync(CompradorJuridico());

        escritor.Should().ContainSingle().Which.Should().Be(EnvioValidacionMotivos.RepresentanteUtilizable);
        var motivo = lector.Should().ContainSingle().Subject;
        motivo.Codigo.Should().Be(EnvioValidacionMotivos.RepresentanteUtilizable);
        motivo.Informativo.Should().BeTrue();
    }

    // ── Lo que NO debe reportar motivo ────────────────────────────────────────────────────────

    [Fact]
    public async Task ActorJuridicoCompletoSinCobertura_EnviaYNoReportaMotivo()
    {
        var (escritor, lector) = await EjecutarAsync(CompradorJuridico());

        await _kyverumClient.Received(1).StartVerificationAsync(
            Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
        escritor.Should().BeEmpty();
        lector.Should().BeEmpty("si se envió, no hay nada que explicar");
    }

    [Fact]
    public async Task PersonaNatural_NoReportaMotivos()
    {
        // Las personas naturales no entran al disparador: reportarles un motivo sería ruido.
        _providerOptions.Provider = BiometricProviders.Mock; // el peor caso: ni así aparece motivo.

        var (escritor, lector) = await EjecutarAsync(CompradorNatural());

        escritor.Should().BeEmpty();
        lector.Should().BeEmpty();
    }

    // ── Traza sin PII ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElLogNoLlevaPii_NiCorreoNiDocumentoNiNombre()
    {
        // Ley 1581: la traza explica el bloqueo con el rol y el código; identificar a la persona es
        // trabajo del trámite, que ya se nombra en el mensaje.
        await EjecutarAsync(CompradorJuridico(
            rl: new ActorRepresentanteLegal(null, null, "Ana Representante", RlEmail, null)));

        _logger.Mensajes.Should().ContainSingle();
        var mensaje = _logger.Mensajes[0];
        mensaje.Should().Contain("comprador").And.Contain(EnvioValidacionMotivos.RlSinDocumento);
        mensaje.Should().NotContain(RlEmail).And.NotContain(RlDocumento)
            .And.NotContain("Ana Representante").And.NotContain(Nit);
        _logger.Niveles.Should().AllBeEquivalentTo(LogLevel.Warning);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Guarda los actores (escritor) y lista la biometría (lector) sobre la MISMA instancia. Devuelve
    /// los códigos que reportó cada uno para poder compararlos.
    /// </summary>
    private async Task<(IReadOnlyList<string> Escritor, IReadOnlyList<EnvioValidacionMotivoDto> Lector)>
        EjecutarAsync(ActorInput actor)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000001",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(instance);

        var (_, error) = await _put.HandleAsync(id, tenant, new PutActorsRequest([actor]), ct);
        error.Should().BeNull();

        var lista = new ListBiometriaHandler(_repo, _providerOptions, _baul);
        var (respuesta, listError) = await lista.HandleAsync(id, tenant, ct);
        listError.Should().BeNull();

        return (_logger.Motivos, respuesta!.MotivosNoEnvio ?? []);
    }

    /// <summary>
    /// Lista la biometría de un trámite cuyo actor YA está guardado (sin pasar por el PUT). Hace falta
    /// para los dos motivos que la captura rechaza antes de persistir nada.
    /// </summary>
    private async Task<IReadOnlyList<EnvioValidacionMotivoDto>> ListarAsync(ProcedureInstanceActor actor)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000002",
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = "matricula_inicial",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Actors.Add(actor);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, Arg.Any<CancellationToken>()).Returns(instance);

        var (respuesta, error) = await new ListBiometriaHandler(_repo, _providerOptions, _baul)
            .HandleAsync(id, tenant, ct);
        error.Should().BeNull();
        return respuesta!.MotivosNoEnvio ?? [];
    }

    private static ProcedureInstanceActor ActorJuridico(string metadata = "{}") =>
        new()
        {
            Id = Guid.NewGuid(),
            ActorType = "comprador",
            PersonType = "juridical",
            DocumentType = "NIT",
            DocumentNumber = Nit,
            FullName = "Empresa Compradora SAS",
            Email = "contacto@empresa.com",
            Metadata = metadata,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static string? MotivoDe(IReadOnlyList<EnvioValidacionMotivoDto> motivos, string parte) =>
        motivos.FirstOrDefault(m => m.Parte == parte)?.Codigo;

    private void ConFirmaDelBaul() =>
        _baul.ResolveAsync(Arg.Any<Guid>(), RlTipoDocumento, RlDocumento, Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Ana Representante", "sha", "path", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), RlDocumento));

    private void ConIdentidadVigente()
    {
        var now = DateTimeOffset.UtcNow;
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), RlTipoDocumento, RlDocumento, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = callInfo.ArgAt<Guid>(0),
                ProcedureInstanceId = Guid.NewGuid(),
                DocumentType = RlTipoDocumento,
                DocumentNumber = RlDocumento,
                Status = BiometricEstados.Aprobado,
                ValidatedAt = now.AddDays(-1),
                ValidUntil = now.AddDays(29),
                TokenHash = "h",
                ExpiresAt = now.AddHours(1),
                CreatedAt = now.AddDays(-1),
            });
    }

    private static ActorInput CompradorJuridico(ActorRepresentanteLegal? rl = null) =>
        new(
            "comprador",
            "NIT",
            Nit,
            "Empresa Compradora SAS",
            "contacto@empresa.com",
            null,
            PersonType: "juridical",
            RepresentanteLegal: rl ?? new ActorRepresentanteLegal(
                RlTipoDocumento, RlDocumento, "Ana Representante", RlEmail, null));

    private static ActorInput CompradorNatural() =>
        new(
            "comprador",
            "CC",
            "1020304050",
            "Pedro Natural",
            "pedro@correo.com",
            null,
            PersonType: "natural");

    /// <summary>Logger espía: guarda el nivel y el mensaje YA formateado (para poder buscar PII en él).</summary>
    private sealed class LoggerEspia : ILogger<PutActorsHandler>
    {
        public List<LogLevel> Niveles { get; } = [];

        public List<string> Mensajes { get; } = [];

        /// <summary>Códigos de motivo extraídos del mensaje, en orden.</summary>
        public IReadOnlyList<string> Motivos =>
            [.. Mensajes.Select(m => m[(m.LastIndexOf(": ", StringComparison.Ordinal) + 2)..].TrimEnd('.'))];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Niveles.Add(logLevel);
            Mensajes.Add(formatter(state, exception));
        }
    }
}
