using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Persons;

/// <summary>
/// Tests unitarios de <see cref="EditarPrevalidacionHandler"/> — HU #10943 (CF-03, Feature #10864).
/// Cobertura de AC1, AC3, AC4, AC5, AC6, AC7, AC9, AC10, AC13 (los AC8/AC12 se cubren en
/// <c>ReenviarPrevalidacionHandlerTests</c> y en <c>KyverumWebhookHandlerTests</c> respectivamente).
/// </summary>
public sealed class EditarPrevalidacionHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();

    private readonly Guid _tenantId = Guid.NewGuid();

    private EditarPrevalidacionHandler BuildHandler(bool isKyverum = false)
    {
        var opts = new BiometricsProviderOptions
        {
            Provider = isKyverum ? BiometricProviders.Kyverum : BiometricProviders.Mock,
        };
        return new EditarPrevalidacionHandler(_repo, _kyverum, opts, _protector, _events, _audit);
    }

    private (Person Person, ProcedureInstanceBiometricValidation Validation) SeedStandaloneNatural(
        string status = BiometricEstados.Enviado, int resendCount = 0, DateTimeOffset? lastResentAt = null)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DocumentType = "CC",
            DocumentNumber = "1020304050",
            FullName = "Ana Ríos",
            Email = "ana.rios@old.com",
            PersonType = PersonTypes.Natural,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PersonId = person.Id,
            Person = person,
            ProcedureInstanceId = null,
            Name = person.FullName,
            DocumentType = person.DocumentType,
            DocumentNumber = person.DocumentNumber,
            Email = person.Email,
            Status = status,
            Provider = BiometricProviders.Mock,
            TokenHash = "old-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ResendCount = resendCount,
            LastResentAt = lastResentAt,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _repo.GetBiometricByIdWithPersonAsync(validation.Id, _tenantId, Arg.Any<CancellationToken>())
            .Returns(validation);
        return (person, validation);
    }

    private (Person Person, ProcedureInstanceBiometricValidation Validation) SeedStandaloneJuridical(
        string legalRepEmail = "luis@empresa.com")
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DocumentType = "NIT",
            DocumentNumber = "900123456",
            FullName = "Empresa SAS",
            Email = "empresa@example.com",
            PersonType = PersonTypes.Juridical,
            LegalRepDocumentType = "CC",
            LegalRepDocumentNumber = "55667788",
            LegalRepName = "Luis Pardo",
            LegalRepEmail = legalRepEmail,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PersonId = person.Id,
            Person = person,
            ProcedureInstanceId = null,
            Name = person.LegalRepName!,
            DocumentType = person.LegalRepDocumentType!,
            DocumentNumber = person.LegalRepDocumentNumber!,
            Email = legalRepEmail,
            Status = BiometricEstados.Enviado,
            Provider = BiometricProviders.Mock,
            TokenHash = "old-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _repo.GetBiometricByIdWithPersonAsync(validation.Id, _tenantId, Arg.Any<CancellationToken>())
            .Returns(validation);
        return (person, validation);
    }

    // ── AC1 — editar el correo reenvía automáticamente ──────────────────────────

    [Fact]
    public async Task AC1_EmailChange_UpdatesPersonAndValidation_AndResendsAutomatically()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, validation) = SeedStandaloneNatural();
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(
            _tenantId, validation.Id, new EditarPrevalidacionRequest(Email: "ana.rios@new.com"), ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Resent.Should().BeTrue();
        person.Email.Should().Be("ana.rios@new.com");
        validation.Email.Should().Be("ana.rios@new.com");
    }

    [Fact]
    public async Task AC1_EmailChange_GeneratesNewTokenWithExpiry24h_AndInvalidatesPrevious()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandaloneNatural();
        var oldTokenHash = validation.TokenHash;
        var handler = BuildHandler();

        var before = DateTimeOffset.UtcNow;
        await handler.HandleAsync(_tenantId, validation.Id, new EditarPrevalidacionRequest(Email: "ana.rios@new.com"), ct);

        validation.TokenHash.Should().NotBe(oldTokenHash, "el enlace anterior debe dejar de ser válido");
        validation.ExpiresAt.Should().BeCloseTo(before.AddHours(BiometricRules.TokenTtlHoras), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AC1_EmailChange_ResendCountGoesFromZeroToOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandaloneNatural();
        var handler = BuildHandler();

        await handler.HandleAsync(_tenantId, validation.Id, new EditarPrevalidacionRequest(Email: "ana.rios@new.com"), ct);

        validation.ResendCount.Should().Be(1);
        validation.LastResentAt.Should().NotBeNull();
    }

    // ── AC3 — persona jurídica reenvía al representante legal ───────────────────

    [Fact]
    public async Task AC3_Juridical_EditingLegalRepEmail_ResendsToLegalRep_NotToNit()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, validation) = SeedStandaloneJuridical();
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(
            _tenantId, validation.Id, new EditarPrevalidacionRequest(LegalRepEmail: "lpardo@empresa.com"), ct);

        error.Should().BeNull();
        result!.Resent.Should().BeTrue();
        person.LegalRepEmail.Should().Be("lpardo@empresa.com");
        validation.Email.Should().Be("lpardo@empresa.com");
        validation.DocumentNumber.Should().Be("55667788", "el sujeto sigue siendo el representante legal, no el NIT");
    }

    // ── AC4 — editar solo el nombre no reenvía ───────────────────────────────────

    [Fact]
    public async Task AC4_NameOnlyChange_UpdatesButDoesNotResend()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, validation) = SeedStandaloneNatural();
        var handler = BuildHandler();
        var originalResendCount = validation.ResendCount;

        var (result, error, _) = await handler.HandleAsync(
            _tenantId, validation.Id, new EditarPrevalidacionRequest(Name: "Ana María Ríos"), ct);

        error.Should().BeNull();
        result!.Resent.Should().BeFalse();
        result.CaptureUrl.Should().BeNull();
        person.FullName.Should().Be("Ana María Ríos");
        validation.Name.Should().Be("Ana María Ríos");
        validation.ResendCount.Should().Be(originalResendCount);
    }

    // ── AC5 — validación de un trámite no es editable ────────────────────────────

    [Fact]
    public async Task AC5_ValidationBoundToProcedure_Returns403NoEditable_AndDoesNotChangeAnything()
    {
        var ct = TestContext.Current.CancellationToken;
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = Guid.NewGuid(), // ligada a un trámite
            Name = "Juan",
            Email = "juan@x.com",
            Status = BiometricEstados.Enviado,
        };
        _repo.GetBiometricByIdWithPersonAsync(validation.Id, _tenantId, Arg.Any<CancellationToken>())
            .Returns(validation);
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(
            _tenantId, validation.Id, new EditarPrevalidacionRequest(Email: "nuevo@x.com"), ct);

        error.Should().Be("no_editable");
        result.Should().BeNull();
        validation.Email.Should().Be("juan@x.com", "no se modifica ningún dato de la validación");
    }

    // ── AC6 — documento no editable ──────────────────────────────────────────────

    [Fact]
    public async Task AC6_AttemptToChangeDocumentNumber_Returns422_AndNothingChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, validation) = SeedStandaloneNatural();
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(
            _tenantId, validation.Id,
            new EditarPrevalidacionRequest(Name: "Otro Nombre", DocumentNumber: "9999999999"), ct);

        error.Should().Be("documento_no_editable");
        result.Should().BeNull();
        person.DocumentNumber.Should().Be("1020304050");
        person.FullName.Should().Be("Ana Ríos", "ni la persona ni la validación cambian de documento");
        validation.DocumentNumber.Should().Be("1020304050");
    }

    [Fact]
    public async Task AC6_AttemptToChangeDocumentType_Returns422()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandaloneNatural();
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(
            _tenantId, validation.Id, new EditarPrevalidacionRequest(DocumentType: "CE"), ct);

        error.Should().Be("documento_no_editable");
        result.Should().BeNull();
    }

    // ── AC7 — identidad aprobada bloquea edición y reenvío ───────────────────────

    [Theory]
    [InlineData(-12)] // vigente
    [InlineData(3)]   // vencida
    public async Task AC7_ApprovedIdentity_Blocks_RegardlessOfVigencia(int diasVencidaOffset)
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandaloneNatural(status: BiometricEstados.Aprobado);
        validation.ValidatedAt = DateTimeOffset.UtcNow.AddDays(-30 - diasVencidaOffset);
        validation.ValidUntil = DateTimeOffset.UtcNow.AddDays(-diasVencidaOffset);
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(
            _tenantId, validation.Id, new EditarPrevalidacionRequest(Email: "nuevo@x.com"), ct);

        error.Should().Be("identidad_aprobada");
        result.Should().BeNull();
        validation.Status.Should().Be(BiometricEstados.Aprobado);
    }

    // ── AC9 / AC10 — cooldown y tope de reenvíos también aplican al cambio de correo (D10) ──

    [Fact]
    public async Task AC9_Cooldown_BlocksEmailChange_WhenResentRecently()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, validation) = SeedStandaloneNatural(lastResentAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var handler = BuildHandler();

        var (result, error, cooldownMinutos) = await handler.HandleAsync(
            _tenantId, validation.Id, new EditarPrevalidacionRequest(Email: "ana.rios@new.com"), ct);

        error.Should().Be("reenvio_en_cooldown");
        result.Should().BeNull();
        cooldownMinutos.Should().BeGreaterThan(0);
        person.Email.Should().Be("ana.rios@old.com", "el bloqueo no debe dejar cambios a medias");
    }

    [Fact]
    public async Task AC10_MaxResendsReached_BlocksEmailChange_AndDoesNotConsumeProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, validation) = SeedStandaloneNatural(resendCount: BiometricRules.MaxReenvios);
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(
            _tenantId, validation.Id, new EditarPrevalidacionRequest(Email: "ana.rios@new.com"), ct);

        error.Should().Be("tope_reenvios");
        result.Should().BeNull();
        person.Email.Should().Be("ana.rios@old.com");
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    // ── AC13 (Habeas Data) — auditoría del cambio de correo, correos enmascarados ──

    [Fact]
    public async Task AC13_EmailChange_AuditsWithMaskedEmails_NeverInClear()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandaloneNatural();
        var handler = BuildHandler();

        await handler.HandleAsync(_tenantId, validation.Id, new EditarPrevalidacionRequest(Email: "ana.rios@new.com"), ct);

        await _audit.Received(1).LogAsync(
            Arg.Is<IdentityValidationAuditEntry>(e =>
                e.Stage == IdentityValidationAuditStages.ContactEdited
                && e.ValidationId == validation.Id
                && e.Message != null && !e.Message.Contains("ana.rios@old.com") && !e.Message.Contains("ana.rios@new.com")
                && (e.Detail == null || (!e.Detail.Contains("ana.rios@old.com") && !e.Detail.Contains("ana.rios@new.com")))),
            ct);
    }

    [Fact]
    public void MaskEmail_NeverReturnsTheOriginalValue()
    {
        var masked = EditarPrevalidacionHandler.MaskEmail("ana.rios@old.com");

        masked.Should().NotBe("ana.rios@old.com");
        masked.Should().EndWith("@old.com");
        masked.Should().NotContain("ana.rios");
    }

    // ── Kyverum — falla transitoria al reenviar por cambio de correo ─────────────

    [Fact]
    public async Task Kyverum_TransientFailureOnEmailChange_QueuesAsPendienteEnvio_AndStillConsumesQuota()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandaloneNatural(status: BiometricEstados.Expirado);
        validation.Provider = BiometricProviders.Kyverum;
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("caído", transient: true));
        var handler = BuildHandler(isKyverum: true);

        var (result, error, _) = await handler.HandleAsync(
            _tenantId, validation.Id, new EditarPrevalidacionRequest(Email: "ana.rios@new.com"), ct);

        error.Should().BeNull();
        result!.Resent.Should().BeTrue();
        result.CaptureUrl.Should().BeNull();
        validation.Status.Should().Be(BiometricEstados.PendienteEnvio);
        validation.ResendCount.Should().Be(1);
    }

    [Fact]
    public async Task NotFound_WhenValidationDoesNotExist()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = BuildHandler();
        _repo.GetBiometricByIdWithPersonAsync(Arg.Any<Guid>(), _tenantId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var (result, error, _) = await handler.HandleAsync(_tenantId, Guid.NewGuid(), new EditarPrevalidacionRequest(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesCalledOnce_AfterSuccessfulEdit()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandaloneNatural();
        var handler = BuildHandler();

        await handler.HandleAsync(_tenantId, validation.Id, new EditarPrevalidacionRequest(Name: "Otro"), ct);

        await _repo.Received(1).SaveChangesAsync(ct);
    }
}
