using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Identity;

/// <summary>
/// HU #11028 — dos acciones para desbloquear la prueba de la firma del mandato:
/// <c>LinkExistingAsync</c> vincula una identidad que la PERSONA ya validó (sin enviar correo ni crear
/// nada) y <c>SimulateApprovedAsync</c> fabrica una validación aprobada en ambientes de prueba,
/// marcada como simulada para que jamás se confunda con una real.
/// </summary>
public sealed class AdminIdentityLinkAndMockTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-0000-4000-8000-00000000ee01");
    private static readonly Guid Representante = Guid.Parse("77777777-0000-4000-8000-00000000ee02");
    private static readonly Guid Mandatario = Guid.Parse("77777777-0000-4000-8000-00000000ee03");
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AdminIdentitySubjectDescriptor Descriptor() => new(
        Tenant,
        AdminIdentitySubjectTypes.MandateSigner,
        Mandatario,
        "Juan Perez",
        "CC",
        "123456789",
        "juan@x.co",
        ActorBy: null);

    private static AdminIdentityValidation IdentidadDeLaPersona(DateTimeOffset aprobadaEn)
    {
        var v = AdminIdentityValidation.CreateSent(
            Tenant, AdminIdentitySubjectTypes.LegalRepresentative, Representante, "CC", "123456789",
            "Juan Perez", "juan@x.co", AdminIdentityProviders.Kyverum, "url", "kv-1", "enc", "aprobado",
            "{}", aprobadaEn);
        v.Approve(aprobadaEn, "firma-serie-real");
        return v;
    }

    private static (AdminIdentityValidationService Service, FakeProvider Provider, FakeLinker Linker, FakeRepository Repo) Sut()
    {
        var provider = new FakeProvider();
        var repo = new FakeRepository();
        var linker = new FakeLinker();
        return (new AdminIdentityValidationService(provider, repo, linker, new FixedTimeProvider(Now)), provider, linker, repo);
    }

    // ── Vincular ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Link_ConIdentidadVigenteDeLaPersona_LaAnclaSinEnviarCorreo()
    {
        var (service, provider, linker, repo) = Sut();
        var previa = IdentidadDeLaPersona(Now.AddDays(-3));
        repo.Store[previa.Id] = previa;

        var result = await service.LinkExistingAsync(Descriptor(), Ct);

        result.Should().NotBeNull();
        result!.Reused.Should().BeTrue();
        result.Validation.Id.Should().Be(previa.Id);
        provider.StartCalls.Should().Be(0);
        linker.Links.Should().ContainSingle(l =>
            l.SubjectType == AdminIdentitySubjectTypes.MandateSigner && l.SubjectRef == Mandatario);
    }

    [Fact]
    public async Task Link_SinIdentidadDeLaPersona_DevuelveNullYNoCreaNada()
    {
        var (service, provider, linker, repo) = Sut();

        var result = await service.LinkExistingAsync(Descriptor(), Ct);

        // Vincular NUNCA inventa una identidad: el llamador responde 409 y el usuario decide.
        result.Should().BeNull();
        provider.StartCalls.Should().Be(0);
        linker.Links.Should().BeEmpty();
        repo.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Link_ConIdentidadVencida_NoVincula()
    {
        var (service, _, linker, repo) = Sut();
        var vencida = IdentidadDeLaPersona(Now.AddDays(-90));
        repo.Store[vencida.Id] = vencida;

        var result = await service.LinkExistingAsync(Descriptor(), Ct);

        result.Should().BeNull();
        linker.Links.Should().BeEmpty();
    }

    // ── Simular ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Simulate_CreaIdentidadAprobadaVigenteYMarcadaComoSimulada()
    {
        var (service, provider, linker, repo) = Sut();

        var result = await service.SimulateApprovedAsync(Descriptor(), Ct);

        result.Reused.Should().BeFalse();
        result.Validation.Status.Should().Be(AdminIdentityEstados.Aprobado);
        result.Validation.EsAprobadaVigente(Now).Should().BeTrue();
        // Trazabilidad: proveedor y certificado delatan que NO hubo captura biométrica.
        result.Validation.Provider.Should().Be(AdminIdentityProviders.Mock);
        result.Validation.CertificateHash.Should().StartWith("MOCK-");
        provider.StartCalls.Should().Be(0);
        repo.Store.Should().ContainSingle();
        linker.Links.Should().ContainSingle(l => l.SubjectRef == Mandatario);
    }

    [Fact]
    public async Task Simulate_ConIdentidadVigente_NoDuplica()
    {
        var (service, _, _, repo) = Sut();
        var propia = AdminIdentityValidation.CreateSent(
            Tenant, AdminIdentitySubjectTypes.MandateSigner, Mandatario, "CC", "123456789",
            "Juan Perez", "juan@x.co", AdminIdentityProviders.Kyverum, "url", "kv-2", "enc", "aprobado",
            "{}", Now.AddDays(-1));
        propia.Approve(Now.AddDays(-1), "firma-serie-real");
        repo.Store[propia.Id] = propia;

        var result = await service.SimulateApprovedAsync(Descriptor(), Ct);

        result.Reused.Should().BeTrue();
        result.Validation.Id.Should().Be(propia.Id);
        // No pisa una identidad REAL con una simulada.
        result.Validation.Provider.Should().Be(AdminIdentityProviders.Kyverum);
        repo.Store.Should().ContainSingle();
    }

    // ── Dobles ──────────────────────────────────────────────────────────────────────────────

    private sealed class FakeProvider : IAdminIdentityValidationProvider
    {
        public int StartCalls { get; private set; }

        public string Name => AdminIdentityProviders.Kyverum;

        public Task<AdminIdentityStartResult> StartAsync(AdminIdentityStartRequest request, CancellationToken ct = default)
        {
            StartCalls++;
            return Task.FromResult(new AdminIdentityStartResult("kv-new", "https://captura", "enc", "enviado", "{}"));
        }

        public Task<AdminIdentityStatusResult?> GetStatusAsync(string verificationId, CancellationToken ct = default) =>
            Task.FromResult<AdminIdentityStatusResult?>(null);
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
            Guid tenantId, string subjectType, Guid subjectRef, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.Values
                .Where(v => v.TenantId == tenantId && v.SubjectType == subjectType && v.SubjectRef == subjectRef)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefault());

        public Task<AdminIdentityValidation?> FindLatestApprovedByDocumentAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.Values
                .Where(v => v.TenantId == tenantId
                    && v.DocumentType == documentType
                    && v.DocumentNumber == documentNumber
                    && v.Status == AdminIdentityEstados.Aprobado)
                .OrderByDescending(v => v.ValidatedAt)
                .FirstOrDefault());
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
