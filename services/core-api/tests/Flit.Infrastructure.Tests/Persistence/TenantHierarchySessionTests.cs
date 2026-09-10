using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12323 (AC3) — <see cref="TenantHierarchySession"/>: helper que fija <c>app.current_user_id</c>
/// para que el trigger <c>tr_tenants_hierarchy_audit</c> registre el actor del vínculo/desvínculo.
/// Uso de ejemplo: <c>await TenantHierarchySession.SetCurrentUserAsync(db, actorUserId, ct);</c>
/// dentro de la transacción del repositorio de vínculo (HU #12355).
/// </summary>
public sealed class TenantHierarchySessionTests
{
    private const string DdlFile = "108-HU12323-hierarchy-switches-and-link-audit.sql";

    private static FlitDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(name).Options);

    [Fact]
    public void ElGucQueFijaElHelperEsElQueLeeElTriggerDelDdl()
    {
        var ddl = EmbeddedDdl.LoadUp(DdlFile);

        TenantHierarchySession.CurrentUserSetting.Should().Be("app.current_user_id");
        ddl.Should().Contain("current_setting('app.current_user_id', true)",
            "el trigger de auditoría del vínculo lee el mismo GUC que fija el helper");
    }

    [Fact]
    public void SetCurrentUserSql_EsSetConfigLocalALaTransaccionYParametrizado()
    {
        var userId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

        var sql = TenantHierarchySession.SetCurrentUserSql(userId);

        // set_config(name, value, is_local := true): el actor no sobrevive a la transacción; el valor es un
        // parámetro ({0}), nunca texto concatenado (regla innegociable 3).
        sql.Format.Should().Be("SELECT set_config('app.current_user_id', {0}, true)");
        sql.ArgumentCount.Should().Be(1);
        sql.GetArgument(0).Should().Be(userId.ToString());
    }

    [Fact]
    public async Task SetCurrentUserAsync_ProveedorNoRelacional_NoLanza()
    {
        await using var db = NewDb(nameof(SetCurrentUserAsync_ProveedorNoRelacional_NoLanza));

        var act = () => TenantHierarchySession.SetCurrentUserAsync(db, Guid.NewGuid(), TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync("en InMemory no hay trigger que alimentar: el helper es no-op");
    }

    [Fact]
    public async Task SetCurrentUserAsync_GuidEmpty_LanzaArgumentException()
    {
        await using var db = NewDb(nameof(SetCurrentUserAsync_GuidEmpty_LanzaArgumentException));

        var act = () => TenantHierarchySession.SetCurrentUserAsync(db, Guid.Empty, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetCurrentUserAsync_DbNulo_LanzaArgumentNullException()
    {
        var act = () => TenantHierarchySession.SetCurrentUserAsync(null!, Guid.NewGuid(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
