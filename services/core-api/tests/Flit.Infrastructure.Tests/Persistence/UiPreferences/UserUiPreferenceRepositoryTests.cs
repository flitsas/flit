using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.UiPreferences;

/// <summary>
/// Preferencias de UI por usuario (<c>admin.user_ui_preferences</c>) — base compartida de los
/// criterios de "elegir columnas visibles" en tablas de trámites. InMemory: <see cref="TenantRlsScope"/>
/// delega directo sin transacción/set_config (no hay RLS real de Postgres que probar aquí; el
/// aislamiento por tenant/usuario se valida a nivel de filtro LINQ, que sí corre en InMemory).
/// </summary>
public sealed class UserUiPreferenceRepositoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private const string Scope = "tramites.columns";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static string NewDbName() => $"flit-ui-prefs-{Guid.NewGuid()}";

    // Sin fila previa: FindAsync devuelve null. El handler de Application (no este repositorio)
    // es quien traduce ese null a `value: {}` — el repositorio solo reporta "no hay fila".
    [Fact]
    public async Task FindAsync_SinFilaPrevia_DevuelveNull()
    {
        await using var db = NewContext(NewDbName());
        var repo = new UserUiPreferenceRepository(db);

        var result = await repo.FindAsync(TenantId, UserId, Scope, Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_SinFilaPrevia_Crea()
    {
        var dbName = NewDbName();
        await using var db = NewContext(dbName);
        var repo = new UserUiPreferenceRepository(db);

        var saved = await repo.UpsertAsync(TenantId, UserId, Scope, """{"columns":["placa","estado"]}""", Ct);

        saved.TenantId.Should().Be(TenantId);
        saved.UserId.Should().Be(UserId);
        saved.Scope.Should().Be(Scope);
        saved.ValueJson.Should().Be("""{"columns":["placa","estado"]}""");

        await using var verifyDb = NewContext(dbName);
        var rows = await verifyDb.Set<Flit.Infrastructure.Persistence.Entities.Admin.UserUiPreferenceEntity>()
            .ToListAsync(Ct);
        rows.Should().HaveCount(1);
    }

    // Upsert idempotente: la segunda llamada sobre el mismo tenant+usuario+scope actualiza el
    // value existente, NUNCA crea una segunda fila (la unicidad de negocio es tenant+usuario+scope).
    [Fact]
    public async Task UpsertAsync_SobreFilaExistente_ActualizaSinDuplicar()
    {
        var dbName = NewDbName();
        await using (var seedDb = NewContext(dbName))
        {
            var seedRepo = new UserUiPreferenceRepository(seedDb);
            await seedRepo.UpsertAsync(TenantId, UserId, Scope, """{"columns":["placa"]}""", Ct);
        }

        await using var db = NewContext(dbName);
        var repo = new UserUiPreferenceRepository(db);

        var updated = await repo.UpsertAsync(TenantId, UserId, Scope, """{"columns":["placa","estado","fecha"]}""", Ct);

        updated.ValueJson.Should().Be("""{"columns":["placa","estado","fecha"]}""");

        await using var verifyDb = NewContext(dbName);
        var rows = await verifyDb.Set<Flit.Infrastructure.Persistence.Entities.Admin.UserUiPreferenceEntity>()
            .Where(p => p.TenantId == TenantId && p.UserId == UserId && p.Scope == Scope)
            .ToListAsync(Ct);

        rows.Should().HaveCount(1);
        rows[0].Value.Should().Be("""{"columns":["placa","estado","fecha"]}""");
    }

    // Aislamiento por tenant: mismo usuario + mismo scope, pero otro tenant → no ve la preferencia.
    [Fact]
    public async Task FindAsync_ConOtroTenant_NoVeLaPreferenciaDelPrimero()
    {
        var dbName = NewDbName();
        await using (var seedDb = NewContext(dbName))
        {
            var seedRepo = new UserUiPreferenceRepository(seedDb);
            await seedRepo.UpsertAsync(TenantId, UserId, Scope, """{"columns":["placa"]}""", Ct);
        }

        await using var db = NewContext(dbName);
        var repo = new UserUiPreferenceRepository(db);

        var result = await repo.FindAsync(OtherTenantId, UserId, Scope, Ct);

        result.Should().BeNull();
    }

    // Aislamiento por usuario: mismo tenant + mismo scope, pero otro usuario → no ve la
    // preferencia ajena (cada usuario tiene su propia elección de columnas).
    [Fact]
    public async Task FindAsync_ConOtroUsuario_NoVeLaPreferenciaDelPrimero()
    {
        var dbName = NewDbName();
        await using (var seedDb = NewContext(dbName))
        {
            var seedRepo = new UserUiPreferenceRepository(seedDb);
            await seedRepo.UpsertAsync(TenantId, UserId, Scope, """{"columns":["placa"]}""", Ct);
        }

        await using var db = NewContext(dbName);
        var repo = new UserUiPreferenceRepository(db);

        var result = await repo.FindAsync(TenantId, OtherUserId, Scope, Ct);

        result.Should().BeNull();
    }
}
