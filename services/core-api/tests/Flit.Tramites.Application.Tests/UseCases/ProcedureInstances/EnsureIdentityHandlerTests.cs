using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class EnsureIdentityHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly EnsureIdentityHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private const string TipoDoc = "CC";
    private const string Documento = "1020304050";

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

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct);

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

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct);

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

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct);

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

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct);

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

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct);

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

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct);

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

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "comprador", ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.YaVigente);
        previa.Status.Should().Be(BiometricEstados.Aprobado); // NO se invalida (misma persona)
    }

    [Fact]
    public async Task Handle_SinActorParaLaParte_SinActor()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = MatriculaConComprador(); // solo tiene comprador
        _repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, ct).Returns(instance);

        var (result, error) = await _sut.HandleAsync(instance.Id, TenantId, "vendedor", ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.SinActor);
    }

    [Fact]
    public async Task Handle_ParteInvalida_Error()
    {
        var ct = TestContext.Current.CancellationToken;
        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), TenantId, "tercero", ct);
        result.Should().BeNull();
        error.Should().Be("parte_invalida");
    }

    [Fact]
    public async Task Handle_InstanceNotFound_Error()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetByIdWithBiometricsAndActorsAsync(Arg.Any<Guid>(), TenantId, ct)
            .Returns((ProcedureInstance?)null);

        var (result, error) = await _sut.HandleAsync(Guid.NewGuid(), TenantId, "comprador", ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static ProcedureInstance MatriculaConComprador() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ReferenceNumber = "TRM-2026-000100",
        Status = ProcedureInstanceStatus.Draft,
        ModalidadEntrada = TramiteModalidadEntradaCodes.MatriculaInicial,
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
}
