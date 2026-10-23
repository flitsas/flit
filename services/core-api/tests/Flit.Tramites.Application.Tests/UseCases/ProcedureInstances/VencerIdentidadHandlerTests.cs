using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13824 (Feature #13820, Épica #13817) — <see cref="VencerIdentidadHandler"/>: «Vencer» una identidad aprobada y vigente
/// (solo Super Admin). Termina la vigencia, abre una validación nueva en el trámite en borrador más reciente que la usaba
/// (reutilizando el inicio de validación del trámite), conserva la firma de los trámites que ya no están en borrador y deja
/// tracking + bitácora técnica.
/// <para>Uso: <c>await handler.HandleAsync(new VencerIdentidadCommand(validationId, userId), ct)</c>.</para>
/// </summary>
public sealed class VencerIdentidadHandlerTests
{
    // Reloj real (truncado al segundo): el inicio de validación del trámite usa DateTimeOffset.UtcNow, así que el "ahora" del
    // handler tiene que estar en el presente para que el fin de vigencia previo (ahora + 10 días) siga siendo futuro.
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    private static readonly Guid Admin = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string TipoDoc = "CC";
    private const string Documento = "1020304050";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly List<ProcedureInstanceEvent> _eventos = [];
    private readonly List<IdentityValidationAuditEntry> _auditados = [];

    public VencerIdentidadHandlerTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        _repo.ListBorradoresConActorDocumentoAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstance>());
        _repo.AddEventAsync(Arg.Do<ProcedureInstanceEvent>(_eventos.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(_auditados.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult(
                "kyv_nuevo", "https://captura.example.test/nuevo", "whsec", "pending", "{}", Now.AddHours(24)));
    }

    private VencerIdentidadHandler Handler(bool kyverum = true) => new(
        _repo,
        new BiometricsProviderOptions { Provider = kyverum ? BiometricProviders.Kyverum : BiometricProviders.Mock },
        new IniciarKyverumVerifyHandler(_repo, _kyverum, new FakeWebhookSecretProtector(), _events, _audit),
        new IniciarBiometriaHandler(_repo),
        _audit,
        new FixedTime(Now));

    /// <summary>Identidad aprobada hace 20 días (le quedan 10), con certificado, ligada a un trámite radicado o standalone.</summary>
    private ProcedureInstanceBiometricValidation Aprobada(
        string? tramiteStatus = TramiteEstado.Entregado, string status = BiometricEstados.Aprobado)
    {
        var origen = tramiteStatus is null ? null : NuevoTramite(tramiteStatus);
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = origen?.Id,
            ProcedureInstance = origen,
            PersonId = origen is null ? Guid.NewGuid() : null,
            PartyRole = origen is null ? null : BiometricRules.ParteComprador,
            Status = status,
            Provider = BiometricProviders.Kyverum,
            Name = "Juan Pérez",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            Email = "juan.perez@example.test",
            TokenHash = new string('0', 64),
            ExpiresAt = Now.AddDays(-20),
            KyverumVerificationId = "kyv_aprobada",
            CertificateHash = "cert-serie-abc",
            ValidatedAt = Now.AddDays(-20),
            ValidUntil = Now.AddDays(10),
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    private static ProcedureInstance NuevoTramite(string status, DateTimeOffset? updated = null)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For(TramiteModalidadEntradaCodes.MatriculaInicial),
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = $"TRM-2026-{Random.Shared.Next(100000, 999999)}",
            Status = status,
            CreatedAt = Now.AddDays(-3),
            UpdatedAt = updated,
        };
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            ActorType = BiometricRules.ParteComprador,
            PersonType = ActorPersonTypes.Natural,
            FullName = "Juan Pérez",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            Email = "juan.actor@example.test",
            Ordinal = 1,
        });
        return instance;
    }

    /// <summary>Borrador que usa la identidad, cargable por el inicio de validación del trámite.</summary>
    private ProcedureInstance Borrador(DateTimeOffset? updated = null)
    {
        var d = NuevoTramite(TramiteEstado.Borrador, updated);
        _repo.GetByIdWithBiometricsAndActorsAsync(d.Id, Tenant, Arg.Any<CancellationToken>()).Returns(d);
        return d;
    }

    private void BorradoresQueUsan(params ProcedureInstance[] ordenadosDelMasRecienteAlMasAntiguo) =>
        _repo.ListBorradoresConActorDocumentoAsync(Tenant, TipoDoc, Documento, Arg.Any<CancellationToken>())
            .Returns(ordenadosDelMasRecienteAlMasAntiguo);

    // ── AC1 — la vigencia termina ahora; la fila y el certificado se conservan ─────────────────────

    [Fact]
    public async Task AC1_Vencer_TerminaLaVigenciaAhora_SinBorrarLaFilaNiElCertificado()
    {
        var v = Aprobada();
        var finPrevio = v.ValidUntil;

        var (result, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().BeNull();
        result!.VencidaAt.Should().Be(Now);
        v.ValidUntil.Should().Be(Now);
        v.ExpiredManuallyAt.Should().Be(Now);
        v.ValidUntilPrevious.Should().Be(finPrevio);
        BiometricRules.EsAprobadaVigente(v, Now).Should().BeFalse();
        BiometricRules.EsAprobadaVigente(v, Now.AddMinutes(1)).Should().BeFalse();
        // Fila y certificado intactos: no es un borrado ni un rechazo.
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.CertificateHash.Should().Be("cert-serie-abc");
        v.ValidatedAt.Should().Be(Now.AddDays(-20));
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_PrevalidacionStandalone_TambienSeVence()
    {
        var v = Aprobada(tramiteStatus: null);

        var (result, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().BeNull();
        result!.ProcedureInstanceId.Should().BeNull();
        v.ProcedureInstanceId.Should().BeNull();
        BiometricRules.EsAprobadaVigente(v, Now).Should().BeFalse();
    }

    // ── AC2 — validación nueva en el borrador más reciente, con enlace ──────────────────────────────

    [Fact]
    public async Task AC2_AbreValidacionNuevaEnElBorradorMasReciente_YDevuelveElEnlace()
    {
        var v = Aprobada();
        var masReciente = Borrador(updated: Now.AddHours(-1));
        var masAntiguo = Borrador(updated: Now.AddDays(-2));
        BorradoresQueUsan(masReciente, masAntiguo);

        var (result, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().BeNull();
        result!.EnlaceEnviado.Should().BeTrue();
        result.MotivoSinEnlace.Should().BeNull();
        result.TramiteBorradorId.Should().Be(masReciente.Id);
        result.TramiteBorradorReferencia.Should().Be(masReciente.ReferenceNumber);
        result.EnlaceCaptura.Should().Be("https://captura.example.test/nuevo");
        result.BorradoresAfectados.Should().Be(2);

        var nueva = masReciente.BiometricValidations.Should().ContainSingle().Subject;
        nueva.Id.Should().Be(result.NuevaValidacionId!.Value);
        nueva.Status.Should().Be(BiometricEstados.EnProceso);
        nueva.Provider.Should().Be(BiometricProviders.Kyverum);
        nueva.PartyRole.Should().Be(BiometricRules.ParteComprador);
        nueva.DocumentNumber.Should().Be(Documento);
        masAntiguo.BiometricValidations.Should().BeEmpty("solo el borrador más reciente recibe la validación nueva");
        await _kyverum.Received(1).StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_ConProveedorMock_DevuelveLaRutaDelMagicLink()
    {
        var v = Aprobada();
        var borrador = Borrador();
        BorradoresQueUsan(borrador);

        var (result, error) = await Handler(kyverum: false).HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().BeNull();
        result!.EnlaceEnviado.Should().BeTrue();
        result.EnlaceCaptura.Should().StartWith("/biometric/");
        borrador.BiometricValidations.Should().ContainSingle().Which.Status.Should().Be(BiometricEstados.Enviado);
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_ProveedorCaido_DejaLaValidacionEncolada_SinEnlaceTodavia()
    {
        var v = Aprobada();
        var borrador = Borrador();
        BorradoresQueUsan(borrador);
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("caído", transient: true));

        var (result, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().BeNull();
        result!.EnCola.Should().BeTrue();
        result.EnlaceCaptura.Should().BeNull();
        result.NuevaValidacionId.Should().NotBeNull();
        borrador.BiometricValidations.Should().ContainSingle().Which.Status.Should().Be(BiometricEstados.PendienteEnvio);
        BiometricRules.EsAprobadaVigente(v, Now).Should().BeFalse("el vencimiento ya está confirmado");
    }

    [Fact]
    public async Task AC2_ValidacionEnCursoEnElBorrador_VenceIgual_PeroNoDuplicaElEnvio()
    {
        var v = Aprobada();
        var borrador = Borrador();
        borrador.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(), TenantId = Tenant, ProcedureInstanceId = borrador.Id,
            PartyRole = BiometricRules.ParteComprador, DocumentType = TipoDoc, DocumentNumber = Documento,
            Name = "Juan Pérez", Email = "juan.actor@example.test", Status = BiometricEstados.EnProceso,
            TokenHash = new string('1', 64), ExpiresAt = Now.AddHours(5),
        });
        BorradoresQueUsan(borrador);

        var (result, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().BeNull();
        result!.EnlaceEnviado.Should().BeFalse();
        result.MotivoSinEnlace.Should().Be(VencerIdentidadHandler.MotivoValidacionEnCurso);
        BiometricRules.EsAprobadaVigente(v, Now).Should().BeFalse();
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_ElBorradorQueTeniaLaFilaAprobadaVencida_PuedeRevalidar()
    {
        // El borrador es el dueño de la propia fila aprobada: una vez vencida a mano ya no bloquea la validación nueva.
        var v = Aprobada(tramiteStatus: TramiteEstado.Borrador);
        var borrador = v.ProcedureInstance!;
        borrador.BiometricValidations.Add(v);
        _repo.GetByIdWithBiometricsAndActorsAsync(borrador.Id, Tenant, Arg.Any<CancellationToken>()).Returns(borrador);
        BorradoresQueUsan(borrador);

        var (result, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().BeNull();
        result!.EnlaceEnviado.Should().BeTrue();
        borrador.BiometricValidations.Should().HaveCount(2);
    }

    // ── AC3 — los borradores que la usaban quedan sin firmar para esa persona ───────────────────────

    [Fact]
    public async Task AC3_UnBorradorNoConservaLaFirma_UnTramiteFueraDeBorradorSi()
    {
        var v = Aprobada();
        await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        BiometricRules.EsAprobadaVigenteParaTramite(v, TramiteEstado.Borrador, Now).Should().BeFalse();
        BiometricRules.EsAprobadaVigenteParaTramite(v, TramiteEstado.Entregado, Now).Should().BeTrue();
    }

    [Fact]
    public async Task AC3_ElListadoMuestraSinFirmarAlBorradorQueUsabaLaIdentidad()
    {
        var v = Aprobada(tramiteStatus: TramiteEstado.Borrador);
        var borrador = v.ProcedureInstance!;
        borrador.BiometricValidations.Add(v);
        await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);
        PrepararListado(borrador, protegidas: [BiometricRules.IdentidadKey(Tenant, TipoDoc, Documento)]);

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct);

        filas.Should().ContainSingle().Which.FirmaCompradorEstado.Should().NotBe(FirmaParteEstados.Firmado,
            "aunque la identidad esté protegida para trámites radicados, un borrador debe revalidar");
    }

    // ── AC4 — un trámite que ya no está en borrador conserva su firma ───────────────────────────────

    [Fact]
    public async Task AC4_UnTramiteRadicadoQueLaUso_SigueFirmado_PorSuFilaPropia()
    {
        var v = Aprobada(tramiteStatus: TramiteEstado.Entregado);
        var radicado = v.ProcedureInstance!;
        radicado.BiometricValidations.Add(v);
        await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);
        PrepararListado(radicado, protegidas: []);

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct);

        filas.Should().ContainSingle().Which.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado);
    }

    [Fact]
    public async Task AC4_UnTramiteRadicadoQueLaReferenciaba_SigueFirmado_PorLaIdentidadProtegida()
    {
        var v = Aprobada();
        var radicado = NuevoTramite(TramiteEstado.Entregado); // sin fila propia: referenciaba la identidad por documento
        await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);
        PrepararListado(radicado, protegidas: [BiometricRules.IdentidadKey(Tenant, TipoDoc, Documento)]);

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct);

        filas.Should().ContainSingle().Which.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado);
    }

    [Fact]
    public async Task AC4_LaProteccionTerminaCuandoLlegaElFinDeVigenciaQueTeniaLaIdentidad()
    {
        var v = Aprobada();
        await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        BiometricRules.EsAprobadaVigenteParaTramite(v, TramiteEstado.Entregado, Now.AddDays(9)).Should().BeTrue();
        BiometricRules.EsAprobadaVigenteParaTramite(v, TramiteEstado.Entregado, Now.AddDays(10)).Should().BeFalse(
            "el trámite radicado no gana más vigencia de la que habría tenido sin el vencimiento manual");
    }

    [Fact]
    public async Task AC4_UnaIdentidadNuncaVencidaAMano_NoCambiaDeComportamiento()
    {
        var v = Aprobada();
        v.ValidUntil = Now.AddDays(-1); // vencida por el paso natural de los 30 días

        BiometricRules.EsAprobadaVigenteParaTramite(v, TramiteEstado.Entregado, Now).Should().BeFalse();
        BiometricRules.EsAprobadaVigenteParaTramite(v, TramiteEstado.Borrador, Now).Should().BeFalse();
    }

    // ── AC5 — sin borradores: vence igual, sin enlace, y lo indica ──────────────────────────────────

    [Fact]
    public async Task AC5_SinBorradoresQueLaUsen_VenceIgual_SinEnviarEnlace_YLoIndica()
    {
        var v = Aprobada();

        var (result, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().BeNull();
        result!.EnlaceEnviado.Should().BeFalse();
        result.MotivoSinEnlace.Should().Be(VencerIdentidadHandler.MotivoSinBorradores);
        result.NuevaValidacionId.Should().BeNull();
        result.EnlaceCaptura.Should().BeNull();
        result.BorradoresAfectados.Should().Be(0);
        BiometricRules.EsAprobadaVigente(v, Now).Should().BeFalse();
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC5_UnBorradorDeOtraPersona_NoCuentaComoUsoDeLaIdentidad()
    {
        var v = Aprobada();
        var ajeno = Borrador();
        ajeno.Actors.Single().DocumentNumber = "999888777"; // el prefiltro SQL lo trajo, pero el sujeto es otro
        BorradoresQueUsan(ajeno);

        var (result, _) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        result!.EnlaceEnviado.Should().BeFalse();
        result.BorradoresAfectados.Should().Be(0);
    }

    // ── AC6 — solo aprobada y vigente ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.Expirado)]
    [InlineData(BiometricEstados.EnProceso)]
    public async Task AC6_ValidacionNoAprobada_NoHayNadaQueVencer(string estado)
    {
        var v = Aprobada(status: estado);

        var (result, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        result.Should().BeNull();
        error.Should().Be(VencerIdentidadHandler.NoAprobadaVigente);
        v.ExpiredManuallyAt.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_AprobadaPeroYaVencida_NoHayNadaQueVencer()
    {
        var v = Aprobada();
        v.ValidUntil = Now.AddDays(-1);

        var (_, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().Be(VencerIdentidadHandler.NoAprobadaVigente);
        VencerIdentidadHandler.MensajeNoAprobadaVigente.Should().Be("Esta validación no está aprobada y vigente. No hay nada que vencer.");
    }

    [Fact]
    public async Task AC6_VencerDosVeces_LaSegundaNoTieneNadaQueVencer()
    {
        var v = Aprobada();
        await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        var (_, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().Be(VencerIdentidadHandler.NoAprobadaVigente);
    }

    [Fact]
    public async Task AC6_ValidacionInexistente_NoEncontrada()
    {
        var (result, error) = await Handler().HandleAsync(new VencerIdentidadCommand(Guid.NewGuid(), Admin), Ct);

        result.Should().BeNull();
        error.Should().Be(VencerIdentidadHandler.NoEncontrada);
    }

    [Fact]
    public async Task AC6_ValidacionDeMandatario_FueraDeAlcance()
    {
        var v = Aprobada();
        v.MandateSignerId = Guid.NewGuid();

        var (_, error) = await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        error.Should().Be(VencerIdentidadHandler.FueraDeAlcance);
        v.ExpiredManuallyAt.Should().BeNull();
    }

    // ── AC7 — tracking del trámite y bitácora técnica ───────────────────────────────────────────────

    [Fact]
    public async Task AC7_DejaEventoEnElTramiteDeOrigenYEnCadaBorradorAfectado_ConQuienYCuando()
    {
        var v = Aprobada(tramiteStatus: TramiteEstado.Entregado);
        var borrador = Borrador();
        BorradoresQueUsan(borrador);

        await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        _eventos.Should().HaveCount(2).And.OnlyContain(e => e.Tipo == VencerIdentidadHandler.EventoTipo);
        _eventos.Select(e => e.ProcedureInstanceId).Should().BeEquivalentTo([v.ProcedureInstanceId!.Value, borrador.Id]);
        _eventos.Should().OnlyContain(e => e.CreatedBy == Admin && e.CreatedAt == Now && e.TenantId == Tenant);
        _eventos[0].Payload.Should().Contain(v.Id.ToString()).And.Contain(Documento).And.Contain("\"persona\":\"Juan P");
    }

    [Fact]
    public async Task AC7_AuditaEnLaBitacoraTecnica_ConUsuario_YCorreoYDocumentoEnmascarados()
    {
        var v = Aprobada();

        await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        var entrada = _auditados.Should().ContainSingle(e => e.Stage == IdentityValidationAuditStages.VencidaPorAdmin).Subject;
        entrada.Outcome.Should().Be(IdentityValidationAuditOutcomes.Ok);
        entrada.TenantId.Should().Be(Tenant);
        entrada.ValidationId.Should().Be(v.Id);
        entrada.Message.Should().Contain(Admin.ToString());
        entrada.Detail.Should().Contain($"usuario={Admin}").And.Contain("vencida_at=");
        (entrada.Message + entrada.Detail).Should().NotContain("juan.perez@example.test").And.NotContain(Documento);
        entrada.Detail.Should().Contain("****").And.Contain("4050");
    }

    [Fact]
    public async Task AC7_LaStandaloneSinBorradores_NoDejaEventosDeTramite_PeroSiBitacora()
    {
        var v = Aprobada(tramiteStatus: null);

        await Handler().HandleAsync(new VencerIdentidadCommand(v.Id, Admin), Ct);

        _eventos.Should().BeEmpty();
        _auditados.Should().ContainSingle(e => e.Stage == IdentityValidationAuditStages.VencidaPorAdmin);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private void PrepararListado(ProcedureInstance instance, string[]? protegidas = null)
    {
        _repo.ListWithSummaryGraphAsync(Tenant, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns([instance]);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        // Tras vencer, la identidad ya no es vigente para nadie: el set global de vigentes queda vacío.
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        _repo.ListProtegidasPorVencimientoManualKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(protegidas ?? []));
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>(StringComparer.Ordinal));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
