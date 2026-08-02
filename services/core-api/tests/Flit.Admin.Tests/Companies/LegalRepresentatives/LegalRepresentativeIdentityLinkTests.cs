using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Admin.Application.Companies.LegalRepresentatives.CreateLegalRepresentative;
using Flit.Admin.Application.Companies.LegalRepresentatives.GetLegalRepresentative;
using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SignatureVaultAggregate = Flit.Admin.Domain.Companies.SignatureVault.SignatureVault;

namespace Flit.Admin.Tests.Companies.LegalRepresentatives;

/// <summary>
/// Tests del endpoint <c>POST /identity/link</c> para representantes legales (HU #11176, AC1–AC4).
/// Ejercitan <see cref="AdminIdentityValidationService.LinkExistingAsync"/> real +
/// <see cref="AdminIdentitySubjectLinker"/> real sobre InMemory; el proveedor externo no se invoca.
/// Cubren:
/// AC1 — persona con identidad aprobada y vigente en el tenant → queda vinculada al representante.
/// AC2 — persona sin ninguna identidad aprobada y vigente → devuelve null (→ 409 sin_identidad_vigente).
/// AC3 — representante que ya tiene su identidad vinculada y vigente → respuesta reused=true, sin cambios.
/// AC4 — tras vincular, <see cref="DbLegalRepresentativeReader.GetByIdAsync"/> devuelve
///        <c>identityStatus = "valid"</c> con la fecha de vigencia correcta.
/// </summary>
public sealed class LegalRepresentativeIdentityLinkTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-0000-4000-8000-000000001176");
    private const string Nit = "900000000-6";
    private const string DocType = "CC";
    private const string DocNumber = "99887766";

    // "Ahora" fijo (UTC): 2026-07-31T12:00:00Z. La vigencia de identidad es de 30 días (→ 2026-08-30).
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ─── AC1 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC1_LinkExisting_WhenPersonHasApprovedValidIdentity_LinksAndReturnsResult()
    {
        await using var ctx = NewContext();

        // Sembrar una identidad aprobada y vigente de la persona (como cualquier sujeto, en el mismo tenant).
        var previa = SeedApprovedIdentity(
            ctx, Tenant, AdminIdentitySubjectTypes.LegalRepresentative, Guid.NewGuid(), DocNumber);

        var repId = await SeedRepresentativeAsync(ctx);
        var (service, _) = BuildService(ctx);
        var descriptor = BuildDescriptor(repId);

        var result = await service.LinkExistingAsync(descriptor, Ct);

        result.Should().NotBeNull("la persona tiene identidad aprobada y vigente en el tenant");
        result!.Validation.Id.Should().Be(previa.Id);
        result.Reused.Should().BeTrue();

        // La identidad debe haber quedado anclada al representante en la BD.
        var linked = await ctx.CompanyLegalRepresentatives
            .AsNoTracking()
            .FirstAsync(r => r.Id == repId, Ct);
        linked.IdentityValidationRef.Should().Be(previa.Id, "el linker ancla la validación al representante");
    }

    // ─── AC2 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_LinkExisting_WhenPersonHasNoApprovedIdentity_ReturnsNull()
    {
        await using var ctx = NewContext();

        var repId = await SeedRepresentativeAsync(ctx);
        var (service, _) = BuildService(ctx);
        var descriptor = BuildDescriptor(repId);

        var result = await service.LinkExistingAsync(descriptor, Ct);

        result.Should().BeNull(
            "sin identidad aprobada y vigente el servicio devuelve null → el endpoint responde 409 sin_identidad_vigente");

        var rep = await ctx.CompanyLegalRepresentatives
            .AsNoTracking()
            .FirstAsync(r => r.Id == repId, Ct);
        rep.IdentityValidationRef.Should().BeNull("el registro no debe modificarse");
    }

    // ─── AC3 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_LinkExisting_WhenRepresentativeAlreadyHasValidIdentity_IsIdempotentAndReturnsReused()
    {
        await using var ctx = NewContext();

        var repId = await SeedRepresentativeAsync(ctx);

        // La identidad ya apunta a ESTE representante exacto (subjectRef = repId) y es vigente.
        // FindLatestBySubjectAsync la encuentra y retorna Reused=true sin llamar al linker.
        var yaVinculada = SeedApprovedIdentity(
            ctx, Tenant, AdminIdentitySubjectTypes.LegalRepresentative, repId, DocNumber);

        var (service, _) = BuildService(ctx);
        var descriptor = BuildDescriptor(repId);

        var result = await service.LinkExistingAsync(descriptor, Ct);

        result.Should().NotBeNull();
        result!.Validation.Id.Should().Be(yaVinculada.Id, "se reutiliza la identidad ya vigente");
        result.Reused.Should().BeTrue("el representante ya tenía la identidad vigente en la tabla de validaciones");
        // Nota: cuando "own" es encontrado, el servicio retorna early sin llamar al linker;
        // no se verifica IdentityValidationRef en la entidad porque esa ruta no lo modifica.
    }

    // ─── AC4 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC4_AfterLinking_GetByIdReturnsIdentityStatusValidWithValidUntilDate()
    {
        await using var ctx = NewContext();

        // La validación debe apuntar a repId para que LoadIdentityVigenciaAsync la encuentre
        // (WHERE SubjectRef = repId). Se siembra antes de crear el representante para tener repId.
        var repId = await SeedRepresentativeAsync(ctx);
        var previa = SeedApprovedIdentity(
            ctx, Tenant, AdminIdentitySubjectTypes.LegalRepresentative, repId, DocNumber);

        var (service, _) = BuildService(ctx);
        var descriptor = BuildDescriptor(repId);

        // LinkExistingAsync encuentra la validación por FindLatestBySubjectAsync (SubjectRef=repId)
        // y devuelve Reused=true.
        var linkResult = await service.LinkExistingAsync(descriptor, Ct);
        linkResult.Should().NotBeNull("hay identidad vigente con SubjectRef=repId");
        linkResult!.Reused.Should().BeTrue();

        // AC4: al consultar el detalle, la respuesta incluye el estado de identidad y la vigencia.
        var reader = new DbLegalRepresentativeReader(ctx, new FixedTimeProvider(Now));
        var item = await reader.GetByIdAsync(Tenant, repId, Ct);

        item.Should().NotBeNull();
        item!.IdentityStatus.Should().Be(AdminIdentityVigencia.Valid,
            "tras vincular la identidad, el estado debe ser 'valid'");
        item.IdentityValidUntil.Should().Be(previa.ValidUntil,
            "la fecha de vigencia debe coincidir con la de la validación vinculada");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra una <see cref="AdminIdentityValidation"/> aprobada y vigente (válida 30 días desde
    /// <see cref="Now"/>). Idéntico al patrón de <c>MandateSignerIdentityOnCreateTests</c>.
    /// </summary>
    private static AdminIdentityValidation SeedApprovedIdentity(
        FlitDbContext ctx,
        Guid tenantId,
        string subjectType,
        Guid subjectRef,
        string documentNumber)
    {
        var validation = AdminIdentityValidation.CreateSent(
            tenantId, subjectType, subjectRef,
            DocType, documentNumber,
            "Persona Validada", "validada@x.co",
            AdminIdentityProviders.Kyverum,
            captureUrl: "https://captura",
            kyverumVerificationId: "kv-test",
            webhookSecretEncrypted: "enc",
            providerStatus: "aprobado",
            providerPayload: "{}",
            Now.AddDays(-1));

        validation.Approve(Now.AddDays(-1), "cert-test");

        var repo = new AdminIdentityValidationRepository(ctx);
        // AddAsync es async, pero en InMemory se puede simplificar usando GetAwaiter sincrónicamente
        // para el setup del test (no hay I/O real).
        repo.AddAsync(validation, CancellationToken.None).GetAwaiter().GetResult();

        return validation;
    }

    /// <summary>
    /// Siembra un representante legal activo usando el handler real. Devuelve el id asignado.
    /// </summary>
    private static async Task<Guid> SeedRepresentativeAsync(FlitDbContext ctx)
    {
        var reader = new DbLegalRepresentativeReader(ctx);
        var repo = new LegalRepresentativeRepository(ctx);
        var writer = new LegalRepresentativeWriter(
            new EmptyProcedureTypeCatalog(),
            new NoneSignatureResolver(),
            new NullSignatureVaultReader(),
            repo, reader,
            new FixedTimeProvider(Now));

        var result = await new CreateLegalRepresentativeHandler(writer).HandleAsync(new CreateLegalRepresentativeCommand
        {
            TenantId = Tenant,
            CompanyNit = Nit,
            CompanyName = "Empresa Link Test S.A.S.",
            DocumentType = DocType,
            DocumentNumber = DocNumber,
            FirstLastName = "Linkable",
            Name = "Persona",
            Email = "persona@x.co",
            ProcedureTypeIds = [],
        }, CancellationToken.None);

        result.IsValid.Should().BeTrue("el representante de prueba debe crearse sin errores");
        return result.Id!.Value;
    }

    private static (AdminIdentityValidationService Service, object _) BuildService(FlitDbContext ctx) =>
        (new AdminIdentityValidationService(
            new NoOpIdentityProvider(),
            new AdminIdentityValidationRepository(ctx),
            new AdminIdentitySubjectLinker(ctx),
            new FixedTimeProvider(Now)),
        new object());

    private static AdminIdentitySubjectDescriptor BuildDescriptor(Guid repId) =>
        new(
            Tenant,
            AdminIdentitySubjectTypes.LegalRepresentative,
            repId,
            "Persona Linkable",
            DocType,
            DocNumber,
            "persona@x.co",
            ActorBy: null);

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"flit-rl-identity-link-{Guid.NewGuid()}")
            .Options);

    // ─── Fakes ─────────────────────────────────────────────────────────────────

    /// <summary>Proveedor de identidad que no hace nada: link no lo invoca nunca.</summary>
    private sealed class NoOpIdentityProvider : IAdminIdentityValidationProvider
    {
        public string Name => AdminIdentityProviders.Kyverum;

        public Task<AdminIdentityStartResult> StartAsync(
            AdminIdentityStartRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("LinkExistingAsync no debe llamar al proveedor externo.");

        public Task<AdminIdentityStatusResult?> GetStatusAsync(
            string verificationId, CancellationToken ct = default) =>
            Task.FromResult<AdminIdentityStatusResult?>(null);
    }

    private sealed class EmptyProcedureTypeCatalog : IProcedureTypeCatalog
    {
        public Task<bool> ExistsAsync(Guid procedureTypeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<ProcedureTypeCatalogItem>> ListActivePublishedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureTypeCatalogItem>>([]);
    }

    private sealed class NoneSignatureResolver : ILegalRepresentativeSignatureResolver
    {
        public Task<LegalRepresentativeSignatureResolution> ResolveAsync(
            Guid tenantId, string nitCompania, string tipoDocumento, string documentoRepresentante,
            DateOnly today, CancellationToken cancellationToken = default) =>
            Task.FromResult(LegalRepresentativeSignatureResolution.None);
    }

    private sealed class NullSignatureVaultReader : ISignatureVaultReader
    {
        public Task<SignatureVaultAggregate?> FindActiveByNitAsync(
            Guid tenantId, string nitEmpresa, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultAggregate?>(null);

        public Task<SignatureVaultAggregate?> FindActiveByDocumentAsync(
            Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultAggregate?>(null);

        public Task<IReadOnlyList<SignatureVaultItem>> ListByTenantAsync(
            Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SignatureVaultItem>>([]);

        public Task<SignatureVaultItem?> GetByIdAsync(
            Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignatureVaultItem?>(null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
