using Flit.Admin.Domain.Companies.Create;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// Escenario complementario a <see cref="HierarchyScenario"/> para redes Marca Blanca (HU #12347, #12407).
/// </summary>
internal static class TransitNetworkSeed
{
    public static readonly Guid MbHead = Id(0x00, 0x21);
    public static readonly Guid MbC1 = Id(0x11, 0x21);
    public static readonly Guid MbC2 = Id(0x12, 0x21);

    /// <summary>Reutiliza las OT del escenario de jerarquía cuando ya están sembradas.</summary>
    public static Guid Ot1 => HierarchyScenario.Ot1;

    public static Guid Ot2 => HierarchyScenario.Ot2;

    public static async Task SeedMarcaBlancaNetworkAsync(PostgresDatabaseFixture fixture)
    {
        await HierarchyScenario.SeedAsync(fixture);

        await using var ctx = fixture.CreateDbContext();

        ctx.Tenants.Add(NewMbTenant(MbHead, "IT-MB", isGroupParent: true, parentId: null));
        await ctx.SaveChangesAsync();

        ctx.Tenants.Add(NewMbTenant(MbC1, "IT-MB1", isGroupParent: false, parentId: MbHead));
        ctx.Tenants.Add(NewMbTenant(MbC2, "IT-MB2", isGroupParent: false, parentId: MbHead));
        await ctx.SaveChangesAsync();
    }

    public static async Task SeedUserAsync(FlitDbContext ctx, Guid userId, Guid homeTenantId, string email)
    {
        if (await ctx.Users.AnyAsync(u => u.Id == userId))
        {
            return;
        }

        ctx.Users.Add(new User
        {
            Id = userId,
            Email = email,
            DisplayName = "Actor IT MB",
            Status = "active",
            HomeTenantId = homeTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await ctx.SaveChangesAsync();
    }

    public static async Task SetHeadGrantsAsync(
        FlitDbContext ctx,
        Guid headTenantId,
        params Guid[] officeIds)
    {
        var existing = await ctx.TenantTransitOfficeGrants
            .Where(g => g.TenantId == headTenantId)
            .ToListAsync();

        ctx.TenantTransitOfficeGrants.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        foreach (var officeId in officeIds)
        {
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(),
                TenantId = headTenantId,
                TransitOfficeId = officeId,
                IsEnabled = true,
                Source = TransitGrantSources.System,
                CreatedAt = now,
            });
        }

        await ctx.SaveChangesAsync();
    }

    private static Tenant NewMbTenant(Guid id, string code, bool isGroupParent, Guid? parentId)
    {
        var tenant = TenantSeed.New(
            id,
            code,
            isGroupParent,
            parentId,
            tenantType: isGroupParent ? GroupKindCodes.MarcaBlanca : CompanyTenantTypes.Concesionario);
        var tail = int.Parse(id.ToString("N")[^8..], System.Globalization.NumberStyles.HexNumber);
        tenant.TaxId = $"8{tail:D14}";
        return tenant;
    }

    private static Guid Id(int suffix, int block) =>
        new($"b0000000-{block:x4}-4000-8000-00000000{suffix:x2}00");
}
