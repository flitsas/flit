using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// Tests del servicio del bloque de validación de identidad administrativa desacoplada por correo
/// (HU #10907, ADR-0034) con proveedor/repositorio/linker FAKE: <c>send</c> crea estado <c>enviado</c>,
/// <c>resend</c> respeta la vigencia (reutiliza una aprobada+vigente pero reenvía las demás) y
/// <c>approve</c> fija <c>valid_until</c> (30 días) y VINCULA la validación al sujeto.
/// </summary>
public sealed class AdminIdentityValidationServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000dd01");
    private static readonly Guid Representative = Guid.Parse("77777777-0000-4000-8000-00000000dd02");
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AdminIdentitySubjectDescriptor Descriptor() => new(
        Tenant,
        AdminIdentitySubjectTypes.LegalRepresentative,
        Representative,
        "Juan Perez",
        "CC",
        "123456789",
        "juan@x.co",
        ActorBy: null);

    [Fact]
    public async Task Send_CreatesValidation_InSentState_AndInvokesProvider()
    {
        var provider = new FakeProvider();
        var repo = new FakeRepository();
        var linker = new FakeLinker();
        var service = new AdminIdentityValidationService(provider, repo, linker, new FixedTimeProvider(Now));

        var result = await service.SendAsync(Descriptor(), Ct);

        provider.StartCalls.Should().Be(1);
        result.Reused.Should().BeFalse();
        result.Validation.Status.Should().Be(AdminIdentityEstados.Enviado);
        result.Validation.SubjectType.Should().Be(AdminIdentitySubjectTypes.LegalRepresentative);
        result.Validation.SubjectRef.Should().Be(Representative);
        result.Validation.CaptureUrl.Should().Be(FakeProvider.CaptureUrl);
        result.Validation.KyverumVerificationId.Should().Be(FakeProvider.VerificationId);
        // El secreto se persiste YA CIFRADO (lo entrega el adaptador; el servicio no lo maneja en claro).
        result.Validation.WebhookSecretEncrypted.Should().Be(FakeProvider.SecretEncrypted);
        repo.Store.Should().ContainSingle();
    }

    [Fact]
    public async Task Resend_WhenNoPriorValidation_Sends()
    {
        var provider = new FakeProvider();
        var repo = new FakeRepository();
        var service = new AdminIdentityValidationService(provider, repo, new FakeLinker(), new FixedTimeProvider(Now));

        var result = await service.ResendAsync(Descriptor(), Ct);

        provider.StartCalls.Should().Be(1);
        result.Reused.Should().BeFalse();
        result.Validation.Status.Should().Be(AdminIdentityEstados.Enviado);
    }

    [Fact]
    public async Task Resend_WhenInProcess_Resends_WithoutBiometriaActivaGuard()
    {
        var provider = new FakeProvider();
        var repo = new FakeRepository();
        // Una validación en curso (enviado) NO bloquea el reenvío (a diferencia del flujo de trámite).
        var enProceso = AdminIdentityValidation.CreateSent(
            Tenant, AdminIdentitySubjectTypes.LegalRepresentative, Representative, "CC", "123456789",
            "Juan Perez", "juan@x.co", AdminIdentityProviders.Kyverum, "url", "kv-prev", "enc", "pending",
            "{}", Now.AddMinutes(-10));
        repo.Store[enProceso.Id] = enProceso;

        var service = new AdminIdentityValidationService(provider, repo, new FakeLinker(), new FixedTimeProvider(Now));
        var result = await service.ResendAsync(Descriptor(), Ct);

        provider.StartCalls.Should().Be(1);
        result.Reused.Should().BeFalse();
        repo.Store.Should().HaveCount(2); // se creó una nueva validación
    }

    [Fact]
    public async Task Resend_WhenApprovedAndVigente_ReusesWithoutSending()
    {
        var provider = new FakeProvider();
        var repo = new FakeRepository();
        var approved = AdminIdentityValidation.CreateSent(
            Tenant, AdminIdentitySubjectTypes.LegalRepresentative, Representative, "CC", "123456789",
            "Juan Perez", "juan@x.co", AdminIdentityProviders.Kyverum, "url", "kv-prev", "enc", "pending",
            "{}", Now.AddDays(-5));
        approved.Approve(Now.AddDays(-5), "cert-1"); // vigente: aprobada hace 5 días (vence a los 30)
        repo.Store[approved.Id] = approved;

        var service = new AdminIdentityValidationService(provider, repo, new FakeLinker(), new FixedTimeProvider(Now));
        var result = await service.ResendAsync(Descriptor(), Ct);

        provider.StartCalls.Should().Be(0); // NO se reenvía: respeta la vigencia
        result.Reused.Should().BeTrue();
        result.Validation.Id.Should().Be(approved.Id);
    }

    [Fact]
    public async Task Resend_WhenApprovedButExpired_Resends()
    {
        var provider = new FakeProvider();
        var repo = new FakeRepository();
        var expired = AdminIdentityValidation.CreateSent(
            Tenant, AdminIdentitySubjectTypes.LegalRepresentative, Representative, "CC", "123456789",
            "Juan Perez", "juan@x.co", AdminIdentityProviders.Kyverum, "url", "kv-prev", "enc", "pending",
            "{}", Now.AddDays(-40));
        expired.Approve(Now.AddDays(-40), "cert-1"); // aprobada hace 40 días → ya venció (30 días)
        repo.Store[expired.Id] = expired;

        var service = new AdminIdentityValidationService(provider, repo, new FakeLinker(), new FixedTimeProvider(Now));
        var result = await service.ResendAsync(Descriptor(), Ct);

        provider.StartCalls.Should().Be(1); // venció → se reenvía
        result.Reused.Should().BeFalse();
        result.Validation.Status.Should().Be(AdminIdentityEstados.Enviado);
    }

    [Fact]
    public async Task Approve_SetsValidUntil30Days_AndLinksSubject()
    {
        var provider = new FakeProvider();
        var repo = new FakeRepository();
        var linker = new FakeLinker();
        var service = new AdminIdentityValidationService(provider, repo, linker, new FixedTimeProvider(Now));

        var sent = await service.SendAsync(Descriptor(), Ct);
        var validationId = sent.Validation.Id;

        var transitioned = await service.ApproveAsync(Tenant, validationId, "cert-xyz", Now, Ct);

        transitioned.Should().BeTrue();
        var stored = repo.Store[validationId];
        stored.Status.Should().Be(AdminIdentityEstados.Aprobado);
        stored.ValidatedAt.Should().Be(Now);
        stored.CertificateHash.Should().Be("cert-xyz");
        stored.ValidUntil.Should().NotBeNull();
        // Vigencia = medianoche Colombia de (aprobación + 30 días).
        stored.ValidUntil!.Value.Should().Be(AdminIdentityRules.FechaFinVigencia(Now));
        stored.EsAprobadaVigente(Now).Should().BeTrue();

        // Se ancló al sujeto (representante legal → identity_validation_ref).
        linker.Links.Should().ContainSingle();
        linker.Links[0].Should().Be((Tenant, AdminIdentitySubjectTypes.LegalRepresentative, Representative, validationId));
    }

    [Fact]
    public async Task Approve_WhenValidationMissing_ReturnsFalse()
    {
        var service = new AdminIdentityValidationService(
            new FakeProvider(), new FakeRepository(), new FakeLinker(), new FixedTimeProvider(Now));

        var result = await service.ApproveAsync(Tenant, Guid.NewGuid(), "cert", Now, Ct);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Reconcile_WhenProviderApproves_ApprovesAndLinks()
    {
        var provider = new FakeProvider
        {
            Status = new AdminIdentityStatusResult(Approved: true, Rejected: false, "aprobado", "cert-recon", "{}"),
        };
        var repo = new FakeRepository();
        var linker = new FakeLinker();
        var service = new AdminIdentityValidationService(provider, repo, linker, new FixedTimeProvider(Now));

        var sent = await service.SendAsync(Descriptor(), Ct);
        var changed = await service.ReconcileAsync(Tenant, sent.Validation.Id, Now, Ct);

        changed.Should().BeTrue();
        repo.Store[sent.Validation.Id].Status.Should().Be(AdminIdentityEstados.Aprobado);
        repo.Store[sent.Validation.Id].CertificateHash.Should().Be("cert-recon");
        linker.Links.Should().ContainSingle();
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeProvider : IAdminIdentityValidationProvider
    {
        public const string VerificationId = "kv-1";
        public const string CaptureUrl = "https://capture.kyverum/abc";
        public const string SecretEncrypted = "enc-secret";

        public int StartCalls { get; private set; }

        public AdminIdentityStatusResult? Status { get; set; }

        public string Name => AdminIdentityProviders.Kyverum;

        public Task<AdminIdentityStartResult> StartAsync(AdminIdentityStartRequest request, CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(new AdminIdentityStartResult(VerificationId, CaptureUrl, SecretEncrypted, "pending", "{}"));
        }

        public Task<AdminIdentityStatusResult?> GetStatusAsync(string verificationId, CancellationToken ct = default) =>
            Task.FromResult(Status);
    }

    private sealed class FakeRepository : IAdminIdentityValidationRepository
    {
        public Dictionary<Guid, AdminIdentityValidation> Store { get; } = [];

        public Task AddAsync(AdminIdentityValidation validation, CancellationToken cancellationToken = default)
        {
            Store[validation.Id] = validation;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AdminIdentityValidation validation, CancellationToken cancellationToken = default)
        {
            Store[validation.Id] = validation;
            return Task.CompletedTask;
        }

        public Task<AdminIdentityValidation?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.TryGetValue(id, out var v) && v.TenantId == tenantId ? v : null);

        public Task<AdminIdentityValidation?> FindLatestBySubjectAsync(
            Guid tenantId, string subjectType, Guid subjectRef, CancellationToken cancellationToken = default)
        {
            var latest = Store.Values
                .Where(v => v.TenantId == tenantId && v.SubjectType == subjectType && v.SubjectRef == subjectRef)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefault();
            return Task.FromResult(latest);
        }
    }

    private sealed class FakeLinker : IAdminIdentitySubjectLinker
    {
        public List<(Guid Tenant, string SubjectType, Guid SubjectRef, Guid ValidationRef)> Links { get; } = [];

        public Task<bool> LinkAsync(
            Guid tenantId, string subjectType, Guid subjectRef, Guid validationRef, Guid? actorBy,
            CancellationToken cancellationToken = default)
        {
            Links.Add((tenantId, subjectType, subjectRef, validationRef));
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
