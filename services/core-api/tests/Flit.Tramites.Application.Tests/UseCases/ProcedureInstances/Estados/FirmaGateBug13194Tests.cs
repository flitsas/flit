using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Bug #13194 (P4) — «NO SE PERMITE ENVIAR AL OT TRÁMITES SIN FIRMAR», SIEMPRE.
///
/// <para>D2: el gate de firma corre en TODA llegada a preparado / preasignacion / entregado del gestor o
/// el sistema (radicar, re-radicar, «Enviar al OT»), también en un OT con la validación de identidad
/// deshabilitada. D4: una validación de OTRA persona del mismo tenant no aprueba (fail-closed), y la
/// identidad de otro tenant tampoco. D1 (baúl): el baúl solo cuenta si la política lo resuelve —con el
/// interruptor «Baúl de firmas activo» apagado, <c>SignatureVaultPolicy.ResolveAsync</c> devuelve null
/// (cubierto en <c>Flit.Admin.Tests/OtRequirements/SignatureVaultPolicyTests</c>); aquí se simula con el
/// stub en null.</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var outcome = await lifecycle.TransitionAsync(
///     new TramiteTransitionCommand(id, tenant, TramiteEstado.Entregado, null, userId), ct);
/// // outcome.ErrorCode == "firma_pendiente" si alguna parte que firma no tiene identidad vigente.
/// </code>
/// Datos ficticios: documentos 9000000xxx, correos @example.test.
/// </remarks>
public sealed class FirmaGateBug13194Tests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IProcedureTypeRepository _typeRepo = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitOfficeGrantGate _grantGate = Substitute.For<ITransitOfficeGrantGate>();
    private readonly IOtOperabilityGate _operabilityGate = Substitute.For<IOtOperabilityGate>();
    private readonly RecordingTransitionRecorder _recorder = new();
    private readonly RecordingTransitionPublisher _publisher = new();

    public FirmaGateBug13194Tests()
    {
        _grantGate.IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _operabilityGate.IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    /// <summary>OT con la validación de identidad DESHABILITADA (HU #10548): ya no relaja nada.</summary>
    private static IIdentityValidationPolicy OtSinVid()
    {
        var policy = Substitute.For<IIdentityValidationPolicy>();
        policy.IsIdentityValidationRequiredAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(false);
        return policy;
    }

    private TramiteLifecycleService Sut(ISignatureVaultPolicy? vault = null, IFirmaPendienteNotifier? notifier = null) =>
        new(_repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            identityPolicy: OtSinVid(),
            vaultPolicy: vault,
            firmaNotifier: notifier);

    private ProcedureInstance Wire(string status, string tipo = "matricula_inicial", bool subsanacion = false)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(tipo),
            Id = id,
            TenantId = tenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013194",
            Status = status,
            SubsanacionActiva = subsanacion,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        i.FieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            FieldKey = "plate",
            ValueText = "ABC123",
            Source = "consultation",
        });
        _repo.GetByIdWithWizardGraphAsync(id, tenantId, Arg.Any<CancellationToken>()).Returns(i);
        _typeRepo.GetByIdAsync(i.ProcedureTypeId, Arg.Any<CancellationToken>()).Returns(new ProcedureType
        {
            Id = i.ProcedureTypeId,
            Code = "X",
            Name = "X",
            Family = "matriculas",
            PublicationStatus = PublicationStatus.Published,
            WizardEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return i;
    }

    private static ProcedureInstanceActor Natural(ProcedureInstance i, string parte, string? documento) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = i.TenantId,
        ProcedureInstanceId = i.Id,
        ActorType = parte,
        DocumentType = "CC",
        DocumentNumber = documento ?? string.Empty,
        FullName = $"Persona {parte}",
        PersonType = "natural",
    };

    private static ProcedureInstanceActor JuridicoConRl(ProcedureInstance i, string parte) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = i.TenantId,
        ProcedureInstanceId = i.Id,
        ActorType = parte,
        DocumentType = "NIT",
        DocumentNumber = "9000000500",
        FullName = "Compañía de prueba SAS",
        PersonType = "juridical",
        Metadata = ActorMetadataReader.Serialize(
            null, null, new ActorRepresentanteLegal("CC", "9000000501", "Rep Legal", "rl@example.test", null)),
    };

    private Task<TramiteTransitionOutcome> Transition(ProcedureInstance i, string to) =>
        Sut().TransitionAsync(new TramiteTransitionCommand(i.Id, i.TenantId, to, null, null),
            TestContext.Current.CancellationToken);

    private void AssertBloqueadoSinEfectos(TramiteTransitionOutcome outcome, ProcedureInstance i, string estadoOriginal)
    {
        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.FirmaPendiente);
        i.Status.Should().Be(estadoOriginal);
        _recorder.Records.Should().BeEmpty();
        _publisher.Published.Should().BeEmpty();
    }

    // ── D2: el gate bloquea toda llegada a entregado/preasignacion/preparado, también en OT sin VID ──

    [Fact]
    public async Task EnviarAlOt_AsignadoAEntregado_SinFirma_EnOtSinVid_Bloquea()
    {
        var i = Wire(TramiteEstado.Asignado);
        i.Actors.Add(Natural(i, "comprador", "9000000101"));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Asignado);
        outcome.ErrorDetail.Should().Contain("comprador");
        await _repo.DidNotReceive().SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Radicar_PreparadoAEntregado_SinFirma_EnOtSinVid_Bloquea()
    {
        var i = Wire(TramiteEstado.Preparado);
        i.Actors.Add(Natural(i, "comprador", "9000000102"));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
        i.SubmittedAt.Should().BeNull();
    }

    [Fact]
    public async Task ReRadicar_RechazadoAEntregado_SinFirma_Bloquea()
    {
        var i = Wire(TramiteEstado.Rechazado, subsanacion: true);
        i.Actors.Add(Natural(i, "comprador", "9000000103"));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Rechazado);
    }

    [Fact]
    public async Task Preparar_BorradorAPreparado_EnOtSinVid_YaNoRelajaLaIdentidad()
    {
        // Antes (HU #10548) un OT sin VID daba la identidad por satisfecha en el gate de preparación.
        var i = Wire(TramiteEstado.Borrador);
        i.Actors.Add(Natural(i, "comprador", "9000000104"));

        var outcome = await Transition(i, TramiteEstado.Preparado);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Match(c =>
            c == TramiteEstadoErrores.DocumentosIncompletos || c == TramiteEstadoErrores.IdentidadNoAprobada);
        outcome.ErrorDetail.Should().Contain("validación de identidad");
    }

    [Fact]
    public async Task Radicar_ConFirmaVigente_Pasa()
    {
        var i = Wire(TramiteEstado.Preparado);
        FirmaFixture.Firmar(i);

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
        i.Status.Should().Be(TramiteEstado.Entregado);
    }

    [Fact]
    public async Task Traspaso_FaltaElVendedor_BloqueaNombrandoLaParte_YB12SeMantiene()
    {
        // B12 (HU #10661): la firma de la compraventa NO se exige; solo la identidad de ambas partes.
        var i = Wire(TramiteEstado.Preparado, TramiteTipologiaCatalog.CodigoTraspasoStandard ?? "traspaso");
        FirmaFixture.Firmar(i, "comprador");
        i.Actors.Add(Natural(i, "vendedor", "9000000105"));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
        outcome.ErrorDetail.Should().Contain("vendedor").And.NotContain("comprador,");

        // Con el vendedor firmado pasa, sin ninguna firma de compraventa registrada.
        i.BiometricValidations.Add(FirmaFixture.Aprobada(i, "vendedor", "CC", "9000000105"));
        i.Signatures.Should().BeEmpty();
        var ok = await Transition(i, TramiteEstado.Entregado);
        ok.Success.Should().BeTrue();
    }

    // ── D1: el baúl solo cuenta con el interruptor de la compañía encendido ──

    [Fact]
    public async Task PersonaJuridica_BaulVigenteConFlagActivo_Pasa()
    {
        var i = Wire(TramiteEstado.Preparado);
        i.Actors.Add(JuridicoConRl(i, "comprador"));
        var vault = Substitute.For<ISignatureVaultPolicy>();
        vault.ResolveAsync(i.TenantId, "CC", "9000000501", Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Rep Legal", "sig-hash", "vault/firma.png", "art-sha",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                "9000000501"));

        var outcome = await Sut(vault).TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null),
            TestContext.Current.CancellationToken);

        outcome.Success.Should().BeTrue();
    }

    [Fact]
    public async Task PersonaJuridica_BaulVigenteConFlagApagado_NoPasa()
    {
        // Con signature_vault_enabled apagado la política no resuelve firma (devuelve null) aunque la
        // firma del RL exista y esté vigente.
        var i = Wire(TramiteEstado.Preparado);
        i.Actors.Add(JuridicoConRl(i, "comprador"));
        var vault = Substitute.For<ISignatureVaultPolicy>();
        vault.ResolveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);

        var outcome = await Sut(vault).TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null),
            TestContext.Current.CancellationToken);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
    }

    // ── D4: otra persona del mismo tenant y otro tenant NO aprueban ──

    [Fact]
    public async Task OtraPersonaDelMismoTenant_ValidacionDeOtroDocumento_NoAprueba()
    {
        var i = Wire(TramiteEstado.Preparado);
        i.Actors.Add(Natural(i, "comprador", "9000000201"));
        i.BiometricValidations.Add(FirmaFixture.Aprobada(i, "comprador", "CC", "9000000299"));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
    }

    [Fact]
    public async Task ValidacionSinDocumento_NoApruebaANadie()
    {
        // Antes DocumentoCoincide hacía match abierto si faltaba un documento: una fila sin documento
        // aprobaba a cualquier comprador.
        var i = Wire(TramiteEstado.Preparado);
        i.Actors.Add(Natural(i, "comprador", "9000000202"));
        i.BiometricValidations.Add(FirmaFixture.Aprobada(i, "comprador", "CC", documento: null));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
    }

    [Fact]
    public async Task ParteSinActor_ConValidacionAprobadaDelRol_NoAprueba()
    {
        // Antes la parte sin actor se evaluaba con actor = null y cualquier validación del rol la aprobaba.
        var i = Wire(TramiteEstado.Preparado);
        i.BiometricValidations.Add(FirmaFixture.Aprobada(i, "comprador", "CC", "9000000203"));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
    }

    [Fact]
    public async Task IdentidadVigenteDeOtroTenant_NoAprueba()
    {
        var i = Wire(TramiteEstado.Preparado);
        i.Actors.Add(Natural(i, "comprador", "9000000204"));
        var otroTenant = Guid.NewGuid();
        // La persona SÍ tiene identidad vigente… pero en otro tenant. La consulta va por el tenant del trámite.
        _repo.FindVigenteApprovedByDocumentAsync(
                otroTenant, "CC", "9000000204", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(FirmaFixture.Aprobada(i, "comprador", "CC", "9000000204"));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
        await _repo.Received().FindVigenteApprovedByDocumentAsync(
            i.TenantId, "CC", "9000000204", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().FindVigenteApprovedByDocumentAsync(
            otroTenant, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IdentidadVigenteDeLaMismaPersonaEnElMismoTenant_Aprueba()
    {
        // Reuso legítimo (HU #10350): la persona validó en otro trámite del MISMO tenant.
        var i = Wire(TramiteEstado.Preparado);
        i.Actors.Add(Natural(i, "comprador", "9000000205"));
        _repo.FindVigenteApprovedByDocumentAsync(
                i.TenantId, "CC", "9000000205", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(FirmaFixture.Aprobada(i, "comprador", "CC", "9000000205"));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue();
    }

    // ── Paso 2: el bloqueo dispara el correo de validación de cada parte sin firma ──

    [Fact]
    public async Task Bloqueo_NotificaCadaParteSinFirma_YLasExponeConSuEstado()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Asignado, TramiteTipologiaCatalog.CodigoTraspasoStandard ?? "traspaso");
        i.Actors.Add(Natural(i, "comprador", "9000000601"));
        i.Actors.Add(Natural(i, "vendedor", "9000000602"));
        var notifier = Substitute.For<IFirmaPendienteNotifier>();
        notifier.NotificarAsync(i.Id, i.TenantId, "comprador", Arg.Any<CancellationToken>())
            .Returns(FirmaNotificacionEstados.Enviada);
        notifier.NotificarAsync(i.Id, i.TenantId, "vendedor", Arg.Any<CancellationToken>())
            .Returns(FirmaNotificacionEstados.YaEnCurso);

        var outcome = await Sut(notifier: notifier).TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null), ct);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Asignado);
        outcome.PartesSinFirma.Should().BeEquivalentTo(
        [
            new ParteSinFirma("comprador", FirmaNotificacionEstados.Enviada),
            new ParteSinFirma("vendedor", FirmaNotificacionEstados.YaEnCurso),
        ]);
        outcome.ErrorDetail.Should().Contain("comprador (notificación: enviada)")
            .And.Contain("vendedor (notificación: ya_en_curso)");
        await notifier.Received(2).NotificarAsync(i.Id, i.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotificadorQueFalla_NoCambiaEl409()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Preparado);
        i.Actors.Add(Natural(i, "comprador", "9000000603"));
        var notifier = Substitute.For<IFirmaPendienteNotifier>();
        notifier.NotificarAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("proveedor caído"));

        var outcome = await Sut(notifier: notifier).TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null), ct);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
        outcome.PartesSinFirma.Should().ContainSingle()
            .Which.Should().Be(new ParteSinFirma("comprador", FirmaNotificacionEstados.Fallida));
    }

    [Fact]
    public async Task Preparar_BorradorBloqueado_SinFirma_NotificaYConservaElCodigoDelGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Borrador);
        i.Actors.Add(Natural(i, "comprador", "9000000604"));
        var notifier = Substitute.For<IFirmaPendienteNotifier>();
        notifier.NotificarAsync(i.Id, i.TenantId, "comprador", Arg.Any<CancellationToken>())
            .Returns(FirmaNotificacionEstados.Enviada);

        var outcome = await Sut(notifier: notifier).TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Preparado, null, null), ct);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().NotBe(TramiteEstadoErrores.FirmaPendiente, "el gate de preparación conserva su código");
        outcome.PartesSinFirma.Should().ContainSingle()
            .Which.Should().Be(new ParteSinFirma("comprador", FirmaNotificacionEstados.Enviada));
        outcome.ErrorDetail.Should().Contain("Firma pendiente de: comprador (notificación: enviada)");
    }

    [Fact]
    public async Task ConFirmaVigente_NoNotifica()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Preparado);
        FirmaFixture.Firmar(i);
        var notifier = Substitute.For<IFirmaPendienteNotifier>();

        var outcome = await Sut(notifier: notifier).TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null), ct);

        outcome.Success.Should().BeTrue();
        outcome.PartesSinFirma.Should().BeNull();
        await notifier.DidNotReceive().NotificarAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Review PR #510 (MAYOR-3): partes que firman según el tipo ──

    [Fact]
    public async Task TraspasoUnilateral_SoloFirmaElPropietario_ElCompradorSinVidNoBloquea()
    {
        // DDL 94 / ADR-0051: en TRASPASO_UNILATERAL valida identidad y firma SOLO el propietario
        // (biometricActors/signatureActors = OWNER). El comprador no se convoca.
        var i = Wire(TramiteEstado.Preparado);
        i.ProcedureType = ProcedureTypeFixture.TraspasoUnilateral;
        FirmaFixture.Firmar(i, "vendedor");
        i.Actors.Add(Natural(i, "comprador", "9000000801"));

        var outcome = await Transition(i, TramiteEstado.Entregado);

        outcome.Success.Should().BeTrue("el comprador no firma en el traspaso unilateral");
    }

    [Fact]
    public async Task TraspasoUnilateral_PropietarioSinVid_Bloquea()
    {
        var i = Wire(TramiteEstado.Preparado);
        i.ProcedureType = ProcedureTypeFixture.TraspasoUnilateral;
        i.Actors.Add(Natural(i, "vendedor", "9000000802"));
        FirmaFixture.Firmar(i, "comprador");

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
        outcome.ErrorDetail.Should().Contain("vendedor").And.NotContain("comprador (");
    }

    [Fact]
    public async Task Copropietarios_UnCompradorFirmadoYOtroNo_BloqueaNombrandoComprador()
    {
        // ADR-0053 — "todos firman": basta un copropietario sin identidad para que la parte quede pendiente.
        var i = Wire(TramiteEstado.Preparado);
        FirmaFixture.Firmar(i, "comprador");
        var segundo = Natural(i, "comprador", "9000000803");
        segundo.Ordinal = 2;
        i.Actors.Add(segundo);

        var outcome = await Transition(i, TramiteEstado.Entregado);

        AssertBloqueadoSinEfectos(outcome, i, TramiteEstado.Preparado);
        outcome.ErrorDetail.Should().Contain("comprador");

        // Firmado también el segundo copropietario, pasa.
        i.BiometricValidations.Add(FirmaFixture.Aprobada(i, "comprador", "CC", "9000000803"));
        (await Transition(i, TramiteEstado.Entregado)).Success.Should().BeTrue();
    }

    private static ProcedureType TipoLeasing(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Family = ProcedureFamilyCodes.Matriculas,
        // DDL 88: el locatario es parte propia (requiresLessee) pero NO valida identidad ni firma.
        GateProfile = """{"entryMode":"VIN","requiresBuyer":true,"requiresLessee":true,"requiresBiometrics":true,"biometricActors":["BUYER"],"requiresSignature":true}""",
        PublicationStatus = PublicationStatus.Published,
        WizardEnabled = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData("MATRICULA_LEASING")]
    [InlineData("CAMBIO_LOCATARIO")]
    public async Task Leasing_FirmaLaEntidadCompradora_NuncaElLocatario(string code)
    {
        var i = Wire(TramiteEstado.Preparado);
        i.ProcedureType = TipoLeasing(code);
        i.Actors.Add(JuridicoConRl(i, "comprador"));
        i.Actors.Add(Natural(i, "locatario", "9000000804")); // sin VID: no debe contar

        // La entidad compradora sin firma bloquea, y el detalle no menciona al locatario.
        var bloqueado = await Transition(i, TramiteEstado.Entregado);
        AssertBloqueadoSinEfectos(bloqueado, i, TramiteEstado.Preparado);
        bloqueado.ErrorDetail.Should().Contain("comprador").And.NotContain("locatario");

        // Con el RL de la entidad compradora validado, pasa aunque el locatario no tenga VID.
        i.BiometricValidations.Add(FirmaFixture.Aprobada(i, "comprador", "CC", "9000000501"));
        (await Transition(i, TramiteEstado.Entregado)).Success.Should().BeTrue();
    }

    // ── Review PR #510 (MAYOR-1): el bloqueo queda en el accesor scoped para la extensión del 409 ──

    [Fact]
    public async Task Bloqueo_DejaLasPartesEnUltimoBloqueoFirma()
    {
        var ct = TestContext.Current.CancellationToken;
        var i = Wire(TramiteEstado.Preparado);
        i.Actors.Add(Natural(i, "comprador", "9000000901"));
        var notifier = Substitute.For<IFirmaPendienteNotifier>();
        notifier.NotificarAsync(i.Id, i.TenantId, "comprador", Arg.Any<CancellationToken>())
            .Returns(FirmaNotificacionEstados.Enviada);
        var bloqueo = new UltimoBloqueoFirma();
        var sut = new TramiteLifecycleService(
            _repo, _typeRepo, _grantGate, _operabilityGate, NullOtRuleGate.Instance, _recorder, _publisher,
            identityPolicy: OtSinVid(), firmaNotifier: notifier, ultimoBloqueo: bloqueo);

        var outcome = await sut.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null), ct);

        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.FirmaPendiente);
        bloqueo.PartesSinFirma.Should().Equal(new ParteSinFirma("comprador", FirmaNotificacionEstados.Enviada));
    }

    // ── A quién aplica el gate ──

    [Theory]
    [InlineData(TramiteEstado.Preparado, TramiteActor.Gestor, true)]
    [InlineData(TramiteEstado.Entregado, TramiteActor.Gestor, true)]
    [InlineData(TramiteEstado.Preasignacion, TramiteActor.Gestor, true)]
    [InlineData(TramiteEstado.Entregado, TramiteActor.Sistema, true)]
    [InlineData(TramiteEstado.Preasignacion, TramiteActor.Ot, false)] // liberar placa: ya está en el OT
    [InlineData(TramiteEstado.Entregado, TramiteActor.Quipux, false)] // el documento ya está en Quipux
    [InlineData(TramiteEstado.Anulado, TramiteActor.Gestor, false)]
    [InlineData(TramiteEstado.Rechazado, TramiteActor.Ot, false)]
    [InlineData(TramiteEstado.Aprobado, TramiteActor.Ot, false)]
    public void Aplica_SoloALlegadasAlOtDelGestorOSistema(string to, TramiteActor actor, bool aplica) =>
        FirmaGate.Aplica(to, actor).Should().Be(aplica);

    [Fact]
    public void PartesQueFirman_SinPerfil_UsaLaReglaDeSubmitGate()
    {
        static ProcedureInstance ConFamilia(string familia) => new()
        {
            ProcedureType = new ProcedureType { Code = "X", Name = "X", Family = familia, GateProfile = "{}" },
        };
        var traspaso = ConFamilia(ProcedureFamilyCodes.Traspaso);
        var matricula = ConFamilia(ProcedureFamilyCodes.Matriculas);

        FirmaGate.PartesQueFirman(traspaso).Should().Equal("comprador", "vendedor");
        FirmaGate.PartesQueFirman(matricula).Should().Equal("comprador");
    }
}
