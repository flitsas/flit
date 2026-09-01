using System.Text.Json;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class EnsureIdentityHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly EnsureIdentityHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private const string TipoDoc = "CC";
    private const string Documento = "1020304050";
    private const string Nit = "900123456";

    public EnsureIdentityHandlerTests()
    {
        _sut = new EnsureIdentityHandler(_repo);
    }

    // ── Regla de vigencia (HU #10350): día de aprobación = día 1; vence el día 31. ──────────────

    [Fact]
    public void EsAprobadaVigente_AprobadaHoy_EsVigente()
    {
        var now = DateTimeOffset.UtcNow;
        var v = Validation(BiometricEstados.Aprobado, validadoAt: now);
        BiometricRules.EsAprobadaVigente(v, now).Should().BeTrue();
    }

    [Fact]
    public void EsAprobadaVigente_Aprobada29DiasAtras_SigueVigente()
    {
        // Aprobada el día 1; hoy es el día 30 → todavía vigente.
        var now = DateTimeOffset.UtcNow;
        var v = Validation(BiometricEstados.Aprobado, validadoAt: now.AddDays(-29));
        BiometricRules.EsAprobadaVigente(v, now).Should().BeTrue();
    }

    [Fact]
    public void EsAprobadaVigente_Aprobada30DiasAtras_Vencida()
    {
        // Aprobada el día 1; hoy es el día 31 (validadoAt + 30 días) → vencida.
        var now = DateTimeOffset.UtcNow;
        var v = Validation(BiometricEstados.Aprobado, validadoAt: now.AddDays(-30));
        BiometricRules.EsAprobadaVigente(v, now).Should().BeFalse();
    }

    [Fact]
    public void EsAprobadaVigente_CuentaElDiaEnHoraDeColombia_NoEnUtc()
    {
        // El conteo es por DÍA calendario de Colombia (UTC-5), no por día UTC. Aprobada el 2026-06-24
        // 02:00 UTC = 2026-06-23 21:00 en Colombia → el día 1 es el 23-jun (Colombia). El día 31 es el
        // 23-jul. Hoy = 2026-07-23 17:00 UTC = 12:00 en Colombia (23-jul) → YA vencida.
        // (Con conteo en día UTC el día 1 sería el 24-jun y daría vigente — eso es justo lo que se corrige.)
        var validadoAt = new DateTimeOffset(2026, 6, 24, 2, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 7, 23, 17, 0, 0, TimeSpan.Zero);
        var v = Validation(BiometricEstados.Aprobado, validadoAt: validadoAt);
        BiometricRules.EsAprobadaVigente(v, now).Should().BeFalse();
    }

    [Fact]
    public void EsAprobadaVigente_NoAprobada_EsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        BiometricRules.EsAprobadaVigente(Validation(BiometricEstados.EnProceso, now), now).Should().BeFalse();
        BiometricRules.EsAprobadaVigente(Validation(BiometricEstados.Rechazado, now), now).Should().BeFalse();
    }

    // ── EnsureIdentityHandler ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PersonaConValidacionVigenteEnOtroTramite_ReferenciaSinClonar()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador(); // sin validaciones locales
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        var source = Validation(BiometricEstados.Aprobado, DateTimeOffset.UtcNow.AddDays(-5));
        source.Score = 95;
        _repo.FindVigenteApprovedByDocumentAsync(TenantId, TipoDoc, Documento, Arg.Any<DateTimeOffset>(), ct)
            .Returns(source);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.Reusada);
        // Rediseño HU #10350: se REFERENCIA la identidad vigente de la persona (id origen), SIN clonar ni
        // crear una fila por trámite. La identidad se valida una vez y sirve para N trámites hasta que venza.
        result.ValidationId.Should().Be(source.Id);
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    [Fact]
    public async Task Handle_SinValidacionVigente_RequiereValidacion_NoClona()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador();
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        _repo.FindVigenteApprovedByDocumentAsync(TenantId, TipoDoc, Documento, Arg.Any<DateTimeOffset>(), ct)
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion);
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    [Fact]
    public async Task Handle_TramiteYaTieneVigente_YaVigente_NoBuscaNiClona()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador();
        instance.BiometricValidations.Add(Validation(BiometricEstados.Aprobado, DateTimeOffset.UtcNow, parte: "comprador"));
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.YaVigente);
        await _repo.DidNotReceive().FindVigenteApprovedByDocumentAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    [Fact]
    public async Task Handle_TramiteConValidacionEnProceso_EnProceso()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador();
        instance.BiometricValidations.Add(Validation(BiometricEstados.EnProceso, null, parte: "comprador"));
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.EnProceso);
    }

    [Fact]
    public async Task Handle_AprobadaVencidaEnTramite_VuelveARequerirValidacion()
    {
        // Una aprobación vencida en el propio trámite NO cuenta como vigente → si tampoco hay otra
        // vigente en el tenant, requiere nueva validación (apalanca la regla de los 30 días).
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador();
        instance.BiometricValidations.Add(
            Validation(BiometricEstados.Aprobado, DateTimeOffset.UtcNow.AddDays(-40), parte: "comprador"));
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        _repo.FindVigenteApprovedByDocumentAsync(TenantId, TipoDoc, Documento, Arg.Any<DateTimeOffset>(), ct)
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion);
    }

    [Fact]
    public async Task Handle_DocumentoCambiado_InvalidaValidacionPreviaYRequiereValidacion()
    {
        // Bugfix HU #10350 — al cambiar el comprador (documento distinto), su identidad previa (de otra
        // persona) NO debe seguir contando: se invalida (expira) y se requiere nueva validación.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador(); // actor.DocumentNumber = "1020304050"
        var previa = Validation(BiometricEstados.Aprobado, DateTimeOffset.UtcNow, parte: "comprador");
        previa.DocumentNumber = "9999999"; // documento de la persona ANTERIOR (distinto al actor actual)
        instance.BiometricValidations.Add(previa);
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        _repo.FindVigenteApprovedByDocumentAsync(TenantId, TipoDoc, Documento, Arg.Any<DateTimeOffset>(), ct)
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion);
        previa.Status.Should().Be(BiometricEstados.Expirado); // la identidad anterior quedó invalidada
        await _repo.Received().SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Handle_MismoDocumentoVigente_NoInvalida_YaVigente()
    {
        // La validación previa es de la MISMA persona (documento coincide) y vigente → se reutiliza tal cual.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador();
        var previa = Validation(BiometricEstados.Aprobado, DateTimeOffset.UtcNow, parte: "comprador"); // doc coincide
        instance.BiometricValidations.Add(previa);
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.YaVigente);
        previa.Status.Should().Be(BiometricEstados.Aprobado); // NO se invalida (misma persona)
    }

    [Fact]
    public async Task Handle_SinActorParaLaParte_SinActor()
    {
        var ct = TestContext.Current.CancellationToken;
        // La parte SÍ valida identidad en este tipo (matrícula declara `biometricActors:["BUYER"]`),
        // pero todavía no hay actor capturado: ahí el desenlace sigue siendo «sin actor».
        var instance = MatriculaConComprador();
        instance.Actors.Clear();
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.SinActor);
    }

    [Fact]
    public async Task Handle_ParteQueElTipoNoSometeAValidacion_NoDisparaNada()
    {
        // ADR-0051 — el handler resolvía el actor y seguía adelante sin preguntar nunca si esa parte
        // firma. En `TRASPASO_UNILATERAL` el locatario se persiste como `comprador` —no hay rol propio
        // para una única parte entrante—, así que descartarlo por el NOMBRE del rol no lo atrapaba y
        // le salía el correo de validación a quien no firma (art. 5.3.2.2). Aquí se usa la matrícula,
        // que declara solo BUYER, porque el defecto es el mismo: la parte no declarada no se asegura.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador();
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "vendedor", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.ParteNoValidaIdentidad);
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    [Fact]
    public async Task Handle_ParteDeclarada_SigueAsegurandose()
    {
        // La guarda es aditiva: la parte que el tipo SÍ declara sigue el camino de siempre.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador();
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().NotBe(EnsureIdentityOutcomes.ParteNoValidaIdentidad);
    }

    [Fact]
    public async Task Handle_ParteInvalida_Error()
    {
        var ct = TestContext.Current.CancellationToken;
        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), TenantId, "tercero", ct: ct);
        result.Should().BeNull();
        error.Should().Be("parte_invalida");
    }

    [Fact]
    public async Task Handle_InstanceNotFound_Error()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithBiometricsAndActorsAsync(Arg.Any<Guid>(), TenantId, ct)
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), TenantId, "comprador", ct: ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    // ── Baúl de firmas (ADR-0025 §4, HU #10645, R14) ────────────────────────────────────────────

    [Fact]
    public async Task Handle_ActorJuridicoConFirmaBaulVigente_FirmaBaul_NoBuscaReusoCrossTramite()
    {
        // Actor NIT + firma de baúl activa+vigente → outcome firma_baul (sin exigir validación,
        // ValidationId null). Precedencia D8: el baúl gana a la reutilización cross-trámite, por lo que
        // NO se consulta FindVigenteApprovedByDocument (paso 2).
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConCompradorNit();
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        var vault = new FakeVaultPolicy(Match());
        var sut = new EnsureIdentityHandler(_repo, vault);

        var (result, error) = await sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.FirmaBaul);
        result.ValidationId.Should().BeNull();
        vault.Calls.Should().Be(1);
        vault.LastNit.Should().Be(Nit);
        await _repo.DidNotReceive().FindVigenteApprovedByDocumentAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BaulYReusoDisponibles_BaulTienePrecedencia()
    {
        // Aunque exista una identidad reutilizable cross-trámite, el baúl (D8: paso 1.5) tiene precedencia:
        // el desenlace es firma_baul, no reusada.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConCompradorNit();
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        _repo.FindVigenteApprovedByDocumentAsync(TenantId, "NIT", Nit, Arg.Any<DateTimeOffset>(), ct)
            .Returns(Validation(BiometricEstados.Aprobado, DateTimeOffset.UtcNow.AddDays(-2)));
        var sut = new EnsureIdentityHandler(_repo, new FakeVaultPolicy(Match()));

        var (result, error) = await sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.FirmaBaul);
    }

    [Fact]
    public async Task Handle_ActorJuridicoSinFirmaBaul_CaeAlFlujoNormal()
    {
        // Actor NIT pero baúl deshabilitado / sin firma vigente (política devuelve null) → cae al flujo
        // normal: sin reuso cross-trámite disponible → requiere_validacion.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConCompradorNit();
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        _repo.FindVigenteApprovedByDocumentAsync(TenantId, "NIT", Nit, Arg.Any<DateTimeOffset>(), ct)
            .Returns((ProcedureInstanceBiometricValidation?)null);
        var vault = new FakeVaultPolicy(null);
        var sut = new EnsureIdentityHandler(_repo, vault);

        var (result, error) = await sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion);
        vault.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Handle_PersonaNatural_NoConsultaElBaul()
    {
        // El baúl SOLO aplica a actores jurídicos (NIT). Una persona natural (CC) nunca consulta el baúl,
        // aunque la política tuviera una firma: su flujo de identidad queda intacto.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador(); // CC
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        _repo.FindVigenteApprovedByDocumentAsync(TenantId, TipoDoc, Documento, Arg.Any<DateTimeOffset>(), ct)
            .Returns((ProcedureInstanceBiometricValidation?)null);
        var vault = new FakeVaultPolicy(Match());
        var sut = new EnsureIdentityHandler(_repo, vault);

        var (result, error) = await sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion);
        vault.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ActorNitConValidacionLocalVigente_YaVigente_NoConsultaBaul()
    {
        // Precedencia: una validación PROPIA vigente del trámite (paso 1) se resuelve ANTES del baúl (1.5).
        // El baúl no se consulta cuando ya hay ya_vigente local para el documento del actor.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConCompradorNit();
        instance.BiometricValidations.Add(ValidationNit(BiometricEstados.Aprobado, DateTimeOffset.UtcNow));
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        var vault = new FakeVaultPolicy(Match());
        var sut = new EnsureIdentityHandler(_repo, vault);

        var (result, error) = await sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.YaVigente);
        vault.Calls.Should().Be(0);
    }

    // ── HU #10867 — Reutilizar prevalidación standalone vigente al crear trámite ────────────────

    [Fact]
    public async Task Handle_PrevalidacionStandaloneVigenteAprobada_Reusada_AC1()
    {
        // AC1: Given prevalidación standalone aprobada vigente (ProcedureInstanceId == null)
        // When se crea trámite con esa persona como actor
        // Then EnsureIdentity retorna outcome "reusada" y el ValidationId de la standalone.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador();
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        // Standalone: ProcedureInstanceId == null, aprobada, vigente (hace 5 días)
        var standalone = Validation(BiometricEstados.Aprobado, DateTimeOffset.UtcNow.AddDays(-5));
        standalone.ProcedureInstanceId = null;

        _repo.FindVigenteApprovedByDocumentAsync(TenantId, TipoDoc, Documento, Arg.Any<DateTimeOffset>(), ct)
            .Returns(standalone);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.Reusada);
        // Reuso por referencia: se devuelve el Id de la standalone, sin clonar ni crear fila nueva.
        result.ValidationId.Should().Be(standalone.Id);
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    [Fact]
    public async Task Handle_PrevalidacionStandaloneExpirada_NoReutiliza_RequiereValidacion_AC2()
    {
        // AC2: Given prevalidación standalone con más de 30 días de antigüedad
        // When se crea trámite con esa persona como actor
        // Then el repositorio no la devuelve (ya filtrada) y EnsureIdentity retorna "requiere_validacion".
        // La responsabilidad del filtro de vigencia es del repositorio; el handler recibe null.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador();
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        // El repositorio filtra la standalone expirada (>30 días) y devuelve null.
        _repo.FindVigenteApprovedByDocumentAsync(TenantId, TipoDoc, Documento, Arg.Any<DateTimeOffset>(), ct)
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion);
        _repo.DidNotReceive().Add(Arg.Any<ProcedureInstanceBiometricValidation>());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static ProcedureInstance MatriculaConComprador() => new()
    {
        ProcedureType = ProcedureTypeFixture.For(TramiteModalidadEntradaCodes.MatriculaInicial),
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ReferenceNumber = "TRM-2026-000100",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
        Actors =
        {
            new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                ActorType = "comprador",
                FullName = "Ana Compradora",
                DocumentType = TipoDoc,
                DocumentNumber = Documento,
                Email = "ana@x.com",
            },
        },
    };

    private static ProcedureInstanceBiometricValidation Validation(
        string estado, DateTimeOffset? validadoAt, string? parte = "comprador") => new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            PartyRole = parte,
            Status = estado,
            Name = "Ana Compradora",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            Email = "ana@x.com",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ValidatedAt = validadoAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Matrícula cuyo comprador es una persona JURÍDICA (NIT) — el único caso que consume el baúl.</summary>
    [Fact]
    public async Task Handle_EligioSelloDeIdentidad_NoConsumeElBaul_YLanzaLaBiometrica()
    {
        // Bug espejo del #11141 (hallado en validación manual). Esta ruta tenía una TERCERA copia de la
        // regla "¿aplica el baúl?" que solo miraba si el actor era jurídico, sin consultar el mecanismo
        // elegido. Con «Sello de validación de identidad» seleccionado y firma del baúl vigente
        // respondía firma_baul, así que la biométrica que el gestor acababa de pedir no se lanzaba nunca
        // y la firma acababa saliendo en blanco.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConCompradorNit();
        instance.Actors.First().Metadata = MetadataConMecanismo("identidad");
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        _repo.FindVigenteApprovedByDocumentAsync(
                TenantId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), ct)
            .Returns((ProcedureInstanceBiometricValidation?)null);
        var vault = new FakeVaultPolicy(Match());
        var sut = new EnsureIdentityHandler(_repo, vault);

        var (result, error) = await sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion);
        // Ni siquiera se pregunta por la firma: el mecanismo elegido ya decide que no se va a consumir.
        vault.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EligioElBaulExplicitamente_ConservaLaCoberturaPorBaul()
    {
        // La otra mitad de la misma regla: elegir el baúl teniéndolo vigente sigue cubriendo la identidad.
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConCompradorNit();
        instance.Actors.First().Metadata = MetadataConMecanismo("baul");
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);
        var sut = new EnsureIdentityHandler(_repo, new FakeVaultPolicy(Match()));

        var (result, error) = await sut.HandleAsync(instance.Id, TenantId, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.FirmaBaul);
    }

    private static ProcedureInstance MatriculaConCompradorNit()
    {
        var instance = MatriculaConComprador();
        var actor = instance.Actors.First();
        actor.DocumentType = "NIT";
        actor.DocumentNumber = Nit;
        actor.FullName = "Renting SAS";
        return instance;
    }

    /// <summary>Validación biométrica local con el documento del actor JURÍDICO (NIT) para el caso ya_vigente.</summary>
    private static ProcedureInstanceBiometricValidation ValidationNit(string estado, DateTimeOffset? validadoAt)
    {
        var v = Validation(estado, validadoAt);
        v.DocumentType = "NIT";
        v.DocumentNumber = Nit;
        return v;
    }

    /// <summary>
    /// Metadata del actor con el mecanismo de firma elegido por el gestor (HU #11061). Va en el mismo
    /// jsonb que el representante legal, que es de donde lo lee <c>FirmaBaulCobertura</c>.
    /// </summary>
    private static string MetadataConMecanismo(string mecanismo) =>
        JsonSerializer.Serialize(new
        {
            representanteLegal = new
            {
                tipoDocumento = "CC",
                numeroDocumento = "1090123456",
                nombreCompleto = "Ana Representante",
                email = "rep@empresa.com",
                mecanismoFirma = mecanismo,
            },
        });

    private static SignatureVaultMatch Match() => new(
        Guid.NewGuid(), "Renting SAS", "sig-hash", "vault/firma.png", "art-sha",
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        "900123456");

    /// <summary>Doble de <see cref="ISignatureVaultPolicy"/>: devuelve el match configurado y cuenta las consultas.</summary>
    private sealed class FakeVaultPolicy(SignatureVaultMatch? match) : ISignatureVaultPolicy
    {
        public int Calls { get; private set; }

        /// <summary>Último DOCUMENTO consultado (HU #10937: el baúl se resuelve por documento del representante).</summary>
        public string? LastNit { get; private set; }

        public Task<SignatureVaultMatch?> ResolveAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastNit = documentNumber;
            return Task.FromResult(match);
        }
    }
}
