using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.UpsertBrandingDraft;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Branding;

/// <summary>
/// HU #12413 AC7 — "existe ... una de integración por escenario de rechazo": a diferencia de
/// <see cref="TenantBrandingConstraintsTests"/> (constraints de BD de #12412), esta clase ejercita
/// <see cref="UpsertBrandingDraftHandler"/> con el validador REAL (<see cref="BrandAssetValidator"/>)
/// y <see cref="TenantBrandingRepository"/> contra PostgreSQL real: confirma que un rechazo de
/// formato/contraste (AC1-AC5) NO llega a persistir nada, y que un borrador válido SÍ persiste.
/// <para>
/// Uso de ejemplo:
/// <code>
/// var handler = new UpsertBrandingDraftHandler(repo, new BrandAssetValidator(new BrandingOptions()));
/// var result = await handler.HandleAsync(command);
/// result.Outcome.Should().Be(UpsertBrandingDraftOutcome.Invalid);
/// </code>
/// </para>
/// </summary>
public sealed class BrandingValidationRejectionTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid MarcaBlancaHeadId = TenantSeed.ParentId;

    private async Task SeedMarcaBlancaHeadAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(MarcaBlancaHeadId, "IT-MB-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca));
        await ctx.SaveChangesAsync();
    }

    private static UpsertBrandingDraftHandler NewHandler(Flit.Infrastructure.Persistence.FlitDbContext ctx) =>
        new(new TenantBrandingRepository(ctx), new BrandAssetValidator(new BrandingOptions()));

    [PostgresFact]
    public async Task AC3_ColorMalFormado_SeRechaza_YNoPersisteNada()
    {
        await SeedMarcaBlancaHeadAsync();

        await using var ctx = NewContext();
        var handler = NewHandler(ctx);

        var request = new UpsertBrandingDraftRequest(
            "Movilidad Andina",
            new UpsertBrandingDraftColorsRequest("no-es-hex", "#1FA2FF", "#FFFFFF"),
            null,
            null);

        var result = await handler.HandleAsync(
            new UpsertBrandingDraftCommand { TenantId = MarcaBlancaHeadId, Request = request },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpsertBrandingDraftOutcome.Invalid);
        result.Errors.Should().ContainSingle(e => e.Code == BrandingErrors.ColorFormat && e.Field == "colors.primary");

        await using var check = NewContext();
        (await check.TenantBrandings.AnyAsync(b => b.TenantId == MarcaBlancaHeadId)).Should().BeFalse(
            "un borrador rechazado por formato no debe llegar a persistirse");
    }

    [PostgresFact]
    public async Task AC4_ContrasteInsuficiente_SeRechaza_YNoPersisteNada()
    {
        await SeedMarcaBlancaHeadAsync();

        await using var ctx = NewContext();
        var handler = NewHandler(ctx);

        // onPrimary/primary = #FFFFFF/#557EFF = 3.61 < 4.5 (fixture compartido con el frontend).
        var request = new UpsertBrandingDraftRequest(
            "Movilidad Andina",
            new UpsertBrandingDraftColorsRequest("#557EFF", "#162744", "#FFFFFF"),
            null,
            null);

        var result = await handler.HandleAsync(
            new UpsertBrandingDraftCommand { TenantId = MarcaBlancaHeadId, Request = request },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpsertBrandingDraftOutcome.Invalid);
        result.Errors.Should().ContainSingle(e => e.Code == BrandingErrors.ContrastTooLow);

        await using var check = NewContext();
        (await check.TenantBrandings.AnyAsync(b => b.TenantId == MarcaBlancaHeadId)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task AC5_NombreDemasiadoCorto_SeRechaza_YNoPersisteNada()
    {
        await SeedMarcaBlancaHeadAsync();

        await using var ctx = NewContext();
        var handler = NewHandler(ctx);

        var request = new UpsertBrandingDraftRequest("A", null, null, null);

        var result = await handler.HandleAsync(
            new UpsertBrandingDraftCommand { TenantId = MarcaBlancaHeadId, Request = request },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpsertBrandingDraftOutcome.Invalid);
        result.Errors.Should().ContainSingle(e => e.Code == BrandingErrors.NameLength);

        await using var check = NewContext();
        (await check.TenantBrandings.AnyAsync(b => b.TenantId == MarcaBlancaHeadId)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task AC1AC3AC4_BorradorValido_SiPersiste()
    {
        await SeedMarcaBlancaHeadAsync();

        await using var ctx = NewContext();
        var handler = NewHandler(ctx);

        var request = new UpsertBrandingDraftRequest(
            "Movilidad Andina",
            new UpsertBrandingDraftColorsRequest("#0B3D91", "#162744", "#FFFFFF"),
            null,
            null);

        var result = await handler.HandleAsync(
            new UpsertBrandingDraftCommand { TenantId = MarcaBlancaHeadId, Request = request },
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(UpsertBrandingDraftOutcome.Saved);

        await using var check = NewContext();
        (await check.TenantBrandings.AnyAsync(b => b.TenantId == MarcaBlancaHeadId)).Should().BeTrue();
    }
}
