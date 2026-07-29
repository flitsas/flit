using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11000 — el alta de un mandatario APALANCA la validación de identidad de la persona en vez de
/// arrancar siempre una nueva: si el mismo documento ya tiene una identidad aprobada y vigente (aunque
/// se haya validado como representante legal u otro mandatario), se ancla al mandatario nuevo y NO se
/// envía correo. Ejercita el handler real + <see cref="AdminIdentityValidationService"/> real +
/// <see cref="AdminIdentitySubjectLinker"/> real sobre InMemory; solo el proveedor externo es fake.
/// </summary>
public sealed class MandateSignerIdentityOnCreateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_WithoutEmail_DoesNotTouchIdentityProvider()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var (handler, provider) = Handler(ctx);

        var result = await handler.HandleAsync(Command("Sin Correo", "555", email: null), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.NotAttempted);
        provider.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task Create_WhenPersonNeverValidated_SendsValidation()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var (handler, provider) = Handler(ctx);

        var result = await handler.HandleAsync(Command("Nuevo Mandatario", "123456"), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.Sent);
        provider.StartCalls.Should().Be(1);
    }

    [Fact]
    public async Task Create_WhenPersonAlreadyValidatedElsewhere_ReusesAndAnchorsWithoutSending()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var (handler, provider) = Handler(ctx);

        // La misma persona (CC 123456) ya validó identidad como REPRESENTANTE LEGAL, hace 3 días.
        var previa = AdminIdentityValidation.CreateSent(
            MandateSignerHandlerTests.OtTenant, AdminIdentitySubjectTypes.LegalRepresentative,
            Guid.NewGuid(), "CC", "123456", "Ya Validada", "ya@x.co", AdminIdentityProviders.Kyverum,
            "url", "kv-1", "enc", "aprobado", "{}", Now.AddDays(-3));
        previa.Approve(Now.AddDays(-3), "cert-previo");
        await new AdminIdentityValidationRepository(ctx).AddAsync(previa, Ct);

        var result = await handler.HandleAsync(Command("Ya Validada", "123456"), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.Reused);
        provider.StartCalls.Should().Be(0); // no se le pide otra biométrica a la persona

        // Y la identidad vigente quedó ANCLADA al mandatario recién creado (rama mandate_signer del linker).
        var signer = await ctx.MandateSigners.AsNoTracking()
            .FirstAsync(s => s.Id == result.MandateSignerId!.Value, Ct);
        signer.IdentityValidationRef.Should().Be(previa.Id);
    }

    [Fact]
    public async Task Create_WhenPersonValidationExpired_SendsNewValidation()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var (handler, provider) = Handler(ctx);

        var vencida = AdminIdentityValidation.CreateSent(
            MandateSignerHandlerTests.OtTenant, AdminIdentitySubjectTypes.LegalRepresentative,
            Guid.NewGuid(), "CC", "123456", "Vencida", "v@x.co", AdminIdentityProviders.Kyverum,
            "url", "kv-2", "enc", "aprobado", "{}", Now.AddDays(-90));
        vencida.Approve(Now.AddDays(-90), "cert-viejo"); // > 30 días ⇒ no vigente
        await new AdminIdentityValidationRepository(ctx).AddAsync(vencida, Ct);

        var result = await handler.HandleAsync(Command("Vencida", "123456"), Ct);

        result.Identity.Should().Be(MandateSignerIdentityOutcome.Sent);
        provider.StartCalls.Should().Be(1);
    }

    [Fact]
    public async Task Create_WhenProviderFails_StillCreatesSignerAndReportsFailure()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var (handler, provider) = Handler(ctx, failing: true);

        var result = await handler.HandleAsync(Command("Proveedor Caído", "777"), Ct);

        // Best-effort: el alta NO se revierte por un fallo del proveedor de identidad.
        result.IsValid.Should().BeTrue();
        result.MandateSignerId.Should().NotBeNull();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.Failed);
        provider.StartCalls.Should().Be(1);
    }

    private static CreateMandateSignerCommand Command(
        string fullName, string documentNumber, string? email = "mandatario@x.co") =>
        new()
        {
            TransitOfficeId = MandateSignerHandlerTests.Office,
            FullName = fullName,
            DocumentNumber = documentNumber,
            DocumentType = "CC",
            Email = email,
            CompanyTenantIds = [MandateSignerHandlerTests.CompanyA],
            CreatedBy = MandateSignerHandlerTests.Operator,
        };

    private static (CreateMandateSignerHandler Handler, FakeIdentityProvider Provider) Handler(
        FlitDbContext ctx, bool failing = false)
    {
        var provider = new FakeIdentityProvider(failing);
        var service = new AdminIdentityValidationService(
            provider,
            new AdminIdentityValidationRepository(ctx),
            new AdminIdentitySubjectLinker(ctx),
            new FixedTimeProvider(Now));

        return (
            new CreateMandateSignerHandler(
                new DbTransitOfficeOperationalStatusReader(ctx),
                new DbMandateSignerReader(ctx),
                new MandateSignerRepository(ctx),
                service),
            provider);
    }

    private sealed class FakeIdentityProvider(bool failing) : IAdminIdentityValidationProvider
    {
        public int StartCalls { get; private set; }

        public string Name => AdminIdentityProviders.Kyverum;

        public Task<AdminIdentityStartResult> StartAsync(
            AdminIdentityStartRequest request, CancellationToken ct = default)
        {
            StartCalls++;
            return failing
                ? throw new AdminIdentityProviderException("proveedor no disponible", transient: true)
                : Task.FromResult(new AdminIdentityStartResult("kv-new", "https://captura", "enc", "enviado", "{}"));
        }

        public Task<AdminIdentityStatusResult?> GetStatusAsync(
            string verificationId, CancellationToken ct = default) =>
            Task.FromResult<AdminIdentityStatusResult?>(null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
