using System.Reflection;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13438 (Feature #13436, Épica #12750) — AC3: la migración retira <c>banners.manage</c> de todo rol distinto de
/// SuperAdmin y es idempotente. Valida el DDL embebido y el descubrimiento de la migración (atributos inline).
/// </summary>
public sealed class Hu13438BannersSoloSuperAdminTests
{
    private const string DdlFileName = "133-HU13438-banners-solo-super-admin.sql";
    private const string MigrationId = "20261008130000_HU13438_BannersSoloSuperAdmin";

    [Fact]
    public void AC3_Ddl_BorraGrantsDeBannersManage_DeTodoRolQueNoSeaSuperAdmin()
    {
        var sql = EmbeddedDdl.LoadUp(DdlFileName);

        sql.Should().Contain("DELETE FROM security.role_permissions");
        sql.Should().Contain("p.slug = 'banners.manage'");
        sql.Should().Contain("r.code <> 'SuperAdmin'");
        sql.Should().NotContain("DROP ", "es solo datos y no debe tocar el esquema");
    }

    [Fact]
    public void AC3_Ddl_EsIdempotente_PorqueSoloBorra()
    {
        var sql = EmbeddedDdl.LoadUp(DdlFileName);

        sql.Should().NotContainEquivalentOf("INSERT INTO");
    }

    [Fact]
    public void AC3_Migracion_SeDescubreConAtributosInline()
    {
        var type = typeof(FlitDbContext).Assembly
            .GetType("Flit.Infrastructure.Migrations.HU13438_BannersSoloSuperAdmin")!;

        type.Should().NotBeNull();
        typeof(Migration).IsAssignableFrom(type).Should().BeTrue();
        type.GetCustomAttribute<MigrationAttribute>()!.Id.Should().Be(MigrationId);
        type.GetCustomAttribute<DbContextAttribute>()!.ContextType.Should().Be<FlitDbContext>();
    }
}
