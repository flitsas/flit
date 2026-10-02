using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13194 P3 — interruptor «Validar SOAT ante el RUNT» por compañía, con el validador REAL
/// (<see cref="ValidateSoatViaRuntHandler"/>) y el proveedor RUNT simulado.
///
/// <para>Semántica vigente (no cambia): opción ACTIVA ⇒ se consulta el RUNT y, sin SOAT vigente, se
/// continúa con advertencia; APAGADA ⇒ un RUNT que responde sin SOAT vigente bloquea.</para>
///
/// <para>Desvíos corregidos: (a) un RUNT que no reporta el SOAT (<c>unknown</c>) ya no degrada el
/// <c>soat_estado=vigente</c> de un PDF cargado a mano; un vencido explícito del RUNT sí manda.
/// (b) una excepción del proveedor es «consulta sin respuesta»: el envío continúa.</para>
///
/// <para>Review 2 (SEC-High): el soporte manual exige un ADJUNTO de SOAT no histórico y una fecha de
/// vencimiento legible que no haya pasado en el día de Colombia — sin adjunto, sin fecha o con fecha
/// ilegible NO hay soporte (fail-closed). Un <c>soat_estado=vigente</c> con origen <c>user</c> por sí solo
/// (PATCH forjado) ya no salta el gate.</para>
///
/// Uso de ejemplo:
/// <code>
/// var sut = new EnviarAlOtHandler(repo, lifecycle, new ValidateSoatViaRuntHandler(repo, catalog, registry), policy);
/// var (result, error, warning) = await sut.HandleAsync(id, tenantId, userId, new EnviarAlOtRequest(), ct);
/// </code>
/// </summary>
public sealed class EnviarAlOtSoatSoporteManualTests
{
    private const string ProviderKey = "runt-test";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly ITramiteLifecycleService _lifecycle = Substitute.For<ITramiteLifecycleService>();
    private readonly ICatalogRepository _catalog = Substitute.For<ICatalogRepository>();
    private readonly IConsultationProviderRegistry _registry = Substitute.For<IConsultationProviderRegistry>();
    private readonly IConsultationProvider _provider = Substitute.For<IConsultationProvider>();
    private readonly ISoatRuntValidationPolicy _policy = Substitute.For<ISoatRuntValidationPolicy>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Futuro => DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
    private static string Pasado => DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd");

    public EnviarAlOtSoatSoporteManualTests()
    {
        _catalog.GetConsultationTemplateByCodeAsync("RUNT_VEHICLE", Arg.Any<CancellationToken>())
            .Returns(new ConsultationTemplate { Code = "RUNT_VEHICLE", ExternalRefs = $"{{\"provider\":\"{ProviderKey}\"}}" });
        _registry.Resolve(ProviderKey).Returns(_provider);
        _provider.Key.Returns(ProviderKey);
    }

    private EnviarAlOtHandler Sut(TimeProvider? clock = null) =>
        new(_repo, _lifecycle, new ValidateSoatViaRuntHandler(_repo, _catalog, _registry, clock: clock), _policy);

    /// <summary>Reloj fijo: el «hoy» del validador se calcula en hora de Colombia a partir de este instante.</summary>
    private sealed class RelojFijo(DateTimeOffset utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc;
    }

    private void Opcion(bool activa) =>
        _policy.IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(activa);

    private void RuntResponde(string soatStatus) =>
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultationResult(
                ProviderKey, "ok", [new ConsultationCheck("soat", "SOAT", soatStatus, "runt", null)], []));

    private ProcedureInstance Asignado(params (string Key, string Value, string Source)[] campos) =>
        Asignado(adjuntoSoat: null, campos);

    /// <param name="adjuntoSoat">Tipo del adjunto cargado (p. ej. <c>soat</c>); <c>null</c> = sin adjunto.</param>
    /// <param name="campos">Field values iniciales (clave, valor, origen).</param>
    /// <param name="historico">Marca el adjunto como histórico (revocación): ya no es soporte vigente.</param>
    private ProcedureInstance Asignado(
        string? adjuntoSoat, (string Key, string Value, string Source)[] campos, bool historico = false)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013194",
            Status = TramiteEstado.Asignado,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        foreach (var (key, value, source) in campos)
        {
            instance.FieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = instance.TenantId,
                ProcedureInstanceId = instance.Id,
                FieldKey = key,
                ValueText = value,
                Source = source,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        if (adjuntoSoat is not null)
        {
            instance.Attachments.Add(new ProcedureInstanceAttachment
            {
                Id = Guid.NewGuid(),
                TenantId = instance.TenantId,
                ProcedureInstanceId = instance.Id,
                Tipo = adjuntoSoat,
                Filename = "soat.pdf",
                Mimetype = "application/pdf",
                Source = "user",
                IsHistorico = historico,
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }

        _repo.GetByIdWithDetailsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
        _lifecycle.TransitionAsync(Arg.Any<TramiteTransitionCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                instance.Status = call.Arg<TramiteTransitionCommand>().ToStatus;
                return TramiteTransitionOutcome.Ok(instance);
            });
        return instance;
    }

    private static ProcedureInstanceFieldValue Soat(ProcedureInstance i) =>
        i.FieldValues.Single(f => f.FieldKey == SoatGate.FieldKey);

    /// <summary>(1) Opción APAGADA + PDF manual vigente + RUNT sin SOAT ⇒ no bloquea y el estado sigue vigente.</summary>
    [Theory]
    [InlineData("ocr")]
    [InlineData("user")]
    public async Task Apagada_conPdfManualVigente_yRuntSinSoat_noBloqueaYConservaVigente(string origen)
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado("soat",
            [(SoatGate.FieldKey, SoatGate.Vigente, origen), ("soat_vencimiento", Futuro, origen)]);

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        Soat(instance).ValueText.Should().Be(SoatGate.Vigente);
        Soat(instance).Source.Should().Be(origen);
    }

    /// <summary>(2) Regresión de la regla: APAGADA + RUNT sin SOAT y sin PDF ⇒ bloquea.</summary>
    [Fact]
    public async Task Apagada_sinPdf_yRuntSinSoat_bloquea()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado();

        var (result, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        result.Should().BeNull();
        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
        Soat(instance).ValueText.Should().Be(SoatGate.Unknown);
        await _lifecycle.DidNotReceiveWithAnyArgs().TransitionAsync(default!, Ct);
    }

    /// <summary>Edge: el soporte manual con vencimiento pasado no cuenta como vigente ⇒ con APAGADA bloquea.</summary>
    [Fact]
    public async Task Apagada_conPdfManualVencidoPorFecha_yRuntSinSoat_bloquea()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado("soat",
            [(SoatGate.FieldKey, SoatGate.Vigente, "ocr"), ("soat_vencimiento", Pasado, "ocr")]);

        var (_, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
    }

    /// <summary>Edge: un vigente que vino de una consulta anterior no es soporte manual: el RUNT actual manda.</summary>
    [Fact]
    public async Task Apagada_conVigenteDeConsultaAnterior_yRuntSinSoat_bloquea()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado((SoatGate.FieldKey, SoatGate.Vigente, "consultation"));

        var (_, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
    }

    /// <summary>Contrato: un vencido EXPLÍCITO del RUNT (fail) manda sobre el PDF manual ⇒ con APAGADA bloquea.</summary>
    [Fact]
    public async Task Apagada_conPdfManualVigente_yRuntVencido_bloqueaYRegistraVencido()
    {
        Opcion(activa: false);
        RuntResponde("fail");
        var instance = Asignado("soat",
            [(SoatGate.FieldKey, SoatGate.Vigente, "ocr"), ("soat_vencimiento", Futuro, "ocr")]);

        var (_, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
        Soat(instance).ValueText.Should().Be(SoatGate.Vencido);
    }

    /// <summary>(3) Opción ACTIVA + RUNT sin SOAT ⇒ continúa con advertencia.</summary>
    [Fact]
    public async Task Activa_runtSinSoat_continuaConAdvertencia()
    {
        Opcion(activa: true);
        RuntResponde("unknown");
        var instance = Asignado();

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().Be(EnviarAlOtHandler.SoatNoVigenteAdvertencia);
        result!.Status.Should().Be(TramiteEstado.Entregado);
    }

    /// <summary>(4) El proveedor RUNT lanza ⇒ consulta sin respuesta: el envío continúa (aun con la opción APAGADA).</summary>
    [Fact]
    public async Task ProveedorLanza_elEnvioContinuaSinBloquearNiAdvertir()
    {
        Opcion(activa: false);
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("RUNT caído"));
        var instance = Asignado();

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        instance.FieldValues.Should().NotContain(f => f.FieldKey == SoatGate.FieldKey);
    }

    /// <summary>Contrato del validador: la excepción del proveedor se traduce a <c>provider_error</c>.</summary>
    [Fact]
    public async Task Validador_proveedorLanza_devuelveProviderError()
    {
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("payload inesperado"));
        var instance = Asignado();

        var (result, error) = await new ValidateSoatViaRuntHandler(_repo, _catalog, _registry)
            .HandleAsync(instance.Id, instance.TenantId, Ct);

        result.Should().BeNull();
        error.Should().Be("provider_error");
    }

    /// <summary>Edge: la cancelación DEL LLAMADOR no se traga — se propaga.</summary>
    [Fact]
    public async Task Validador_cancelacion_sePropaga()
    {
        using var cts = new CancellationTokenSource();
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Task.FromException<ConsultationResult>(new OperationCanceledException(cts.Token));
            });
        var instance = Asignado();

        await new ValidateSoatViaRuntHandler(_repo, _catalog, _registry)
            .Invoking(h => h.HandleAsync(instance.Id, instance.TenantId, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Review 2 (SEC-High): el soporte manual es fail-closed ───────────────────────────────────────

    /// <summary>
    /// SEC: PATCH forjado — <c>soat_estado=vigente</c> con origen <c>user</c> y fecha futura, SIN adjunto de
    /// SOAT + RUNT <c>unknown</c> + opción APAGADA ⇒ bloquea y el estado queda en <c>unknown</c>.
    /// </summary>
    [Fact]
    public async Task Apagada_patchForjadoSinAdjunto_yRuntSinSoat_bloquea()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado(
            (SoatGate.FieldKey, SoatGate.Vigente, "user"),
            ("soat_vencimiento", Futuro, "user"));

        var (result, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        result.Should().BeNull();
        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
        Soat(instance).ValueText.Should().Be(SoatGate.Unknown);
        await _lifecycle.DidNotReceiveWithAnyArgs().TransitionAsync(default!, Ct);
    }

    /// <summary>Happy path: adjunto <c>soat_manual</c> + OCR vigente + fecha futura ⇒ continúa y conserva el vigente.</summary>
    [Fact]
    public async Task Apagada_conAdjuntoSoatManual_yOcrVigente_continua()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado("soat_manual",
            [(SoatGate.FieldKey, SoatGate.Vigente, "ocr"), ("soat_vencimiento", Futuro, "ocr")]);

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        Soat(instance).ValueText.Should().Be(SoatGate.Vigente);
    }

    /// <summary>Edge: el adjunto de SOAT histórico (revocación) ya no es soporte vigente ⇒ bloquea.</summary>
    [Fact]
    public async Task Apagada_conAdjuntoSoatHistorico_bloquea()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado("soat",
            [(SoatGate.FieldKey, SoatGate.Vigente, "ocr"), ("soat_vencimiento", Futuro, "ocr")], historico: true);

        var (_, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
    }

    /// <summary>Fail-closed: con adjunto pero SIN fecha de vencimiento ⇒ no hay soporte ⇒ bloquea.</summary>
    [Fact]
    public async Task Apagada_conAdjunto_sinFechaVencimiento_bloquea()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado("soat", [(SoatGate.FieldKey, SoatGate.Vigente, "ocr")]);

        var (_, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
        Soat(instance).ValueText.Should().Be(SoatGate.Unknown);
    }

    /// <summary>Fail-closed: con adjunto pero fecha ilegible ⇒ no hay soporte ⇒ bloquea.</summary>
    [Theory]
    [InlineData("pronto")]
    [InlineData("31/13/2027")]
    [InlineData("   ")]
    public async Task Apagada_conAdjunto_fechaIlegible_bloquea(string fecha)
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var instance = Asignado("soat",
            [(SoatGate.FieldKey, SoatGate.Vigente, "ocr"), ("soat_vencimiento", fecha, "ocr")]);

        var (_, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().Be(EnviarAlOtHandler.SoatNoVigente);
    }

    /// <summary>
    /// O4: vence HOY en Colombia y son las 20:00 en Bogotá (01:00 UTC del día siguiente) ⇒ sigue vigente.
    /// Con <c>DateTime.UtcNow</c> el «hoy» ya era mañana y el SOAT salía vencido.
    /// </summary>
    [Fact]
    public async Task Apagada_venceHoyEnBogota_a_las_20h_esVigente()
    {
        Opcion(activa: false);
        RuntResponde("unknown");
        var reloj = new RelojFijo(new DateTimeOffset(2026, 10, 2, 1, 0, 0, TimeSpan.Zero)); // 2026-10-01 20:00 -05:00
        var instance = Asignado("soat",
            [(SoatGate.FieldKey, SoatGate.Vigente, "ocr"), ("soat_vencimiento", "2026-10-01", "ocr")]);

        var (result, error, _) = await Sut(reloj).HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
    }

    /// <summary>
    /// O1: un <see cref="OperationCanceledException"/> INTERNO del proveedor (p. ej. timeout de HttpClient)
    /// sin cancelación del llamador es «el RUNT no respondió» ⇒ <c>provider_error</c>, no una excepción.
    /// </summary>
    [Fact]
    public async Task Validador_cancelacionInternaSinCancelarElLlamador_devuelveProviderError()
    {
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("HttpClient.Timeout"));
        var instance = Asignado();

        var (result, error) = await new ValidateSoatViaRuntHandler(_repo, _catalog, _registry)
            .HandleAsync(instance.Id, instance.TenantId, Ct);

        result.Should().BeNull();
        error.Should().Be(ValidateSoatViaRuntHandler.ProviderError);
    }

    // ── P3-09: el RUNT no responde (5xx del gateway, timeout, red) SIN lanzar ───────────────────────

    /// <summary>
    /// Lo que devuelven los proveedores ante un 5xx/timeout/red: check <c>provider</c> en <c>error</c> y
    /// ningún check de SOAT (ver <c>VerifikConsultationProvider.ProviderUnavailable</c>).
    /// </summary>
    private void RuntNoDisponible() =>
        _provider.ConsultAsync(Arg.Any<ConsultationContext>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultationResult(
                ProviderKey, "red",
                [new ConsultationCheck("provider", "Consulta de vehículo", "error", ProviderKey, "no disponible")], []));

    /// <summary>P3-09: APAGADA + RUNT caído + sin soporte ⇒ el envío continúa (no es «respondió sin SOAT»).</summary>
    [Fact]
    public async Task P3_09_Apagada_runtNoDisponible_sinSoporte_continua()
    {
        Opcion(activa: false);
        RuntNoDisponible();
        var instance = Asignado();

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        instance.FieldValues.Should().NotContain(f => f.FieldKey == SoatGate.FieldKey);
    }

    /// <summary>P3-09 edge: RUNT caído no degrada el soporte manual vigente (sigue vigente/ocr).</summary>
    [Fact]
    public async Task P3_09_Apagada_runtNoDisponible_conSoporteManual_noLoDegrada()
    {
        Opcion(activa: false);
        RuntNoDisponible();
        var instance = Asignado("soat",
            [(SoatGate.FieldKey, SoatGate.Vigente, "ocr"), ("soat_vencimiento", Futuro, "ocr")]);

        var (result, error, _) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
        Soat(instance).ValueText.Should().Be(SoatGate.Vigente);
        Soat(instance).Source.Should().Be("ocr");
    }

    /// <summary>P3-09 con la opción ACTIVA: RUNT caído ⇒ continúa SIN advertencia (no se sabe si hay SOAT).</summary>
    [Fact]
    public async Task P3_09_Activa_runtNoDisponible_continuaSinAdvertencia()
    {
        Opcion(activa: true);
        RuntNoDisponible();
        var instance = Asignado();

        var (result, error, warning) = await Sut().HandleAsync(
            instance.Id, instance.TenantId, Guid.NewGuid(), new EnviarAlOtRequest(), Ct);

        error.Should().BeNull();
        warning.Should().BeNull();
        result!.Status.Should().Be(TramiteEstado.Entregado);
    }

    /// <summary>Contrato del validador (validate-runt ⇒ 502): RUNT caído ⇒ <c>provider_error</c>, sin escribir.</summary>
    [Fact]
    public async Task P3_09_Validador_runtNoDisponible_devuelveProviderErrorSinEscribir()
    {
        RuntNoDisponible();
        var instance = Asignado((SoatGate.FieldKey, SoatGate.Vigente, "consultation"));

        var (result, error) = await new ValidateSoatViaRuntHandler(_repo, _catalog, _registry)
            .HandleAsync(instance.Id, instance.TenantId, Ct);

        result.Should().BeNull();
        error.Should().Be(ValidateSoatViaRuntHandler.ProviderError);
        Soat(instance).ValueText.Should().Be(SoatGate.Vigente);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
