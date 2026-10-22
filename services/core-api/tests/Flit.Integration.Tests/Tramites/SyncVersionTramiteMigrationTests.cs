using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13073 (Feature #13062, Épica #12737) contra Postgres real, con TODAS las migraciones
/// aplicadas: la versión de sincronización la asigna la base en el alta (AC1) y en cualquier cambio
/// del trámite (AC2), y no se puede fijar desde fuera (AC3).
/// </summary>
public sealed class SyncVersionTramiteMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Cliente = new("5a5a5a5a-0001-4000-8000-000000013073");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000013073");
    private static readonly Guid Primero = new("5a5a5a5a-0003-4000-8000-000000000001");
    private static readonly Guid Segundo = new("5a5a5a5a-0003-4000-8000-000000000002");

    [PostgresFact]
    public async Task AC1_ElAltaAsignaUnaVersionMayorQueCualquieraExistenteYLaFechaDeCambio()
    {
        await SembrarAsync(Primero, 1);
        var primero = await LeerAsync(Primero);

        await SembrarTramiteAsync(Segundo, 2);
        var segundo = await LeerAsync(Segundo);

        primero.Version.Should().BePositive();
        segundo.Version.Should().BeGreaterThan(primero.Version);
        segundo.CambiadoEn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [PostgresTheory]
    [InlineData("UPDATE tramites.procedure_instances SET status = 'preparado' WHERE id = @id")]
    [InlineData("UPDATE tramites.procedure_instances SET assigned_to_user_id = NULL WHERE id = @id")]
    [InlineData("UPDATE tramites.procedure_instances SET transit_office_id = NULL WHERE id = @id")]
    [InlineData("UPDATE tramites.procedure_instances SET deleted_at = now() WHERE id = @id")]
    public async Task AC2_CualquierCambioDelTramiteSubeLaVersion(string sentencia)
    {
        await SembrarAsync(Primero, 1);
        var antes = await LeerAsync(Primero);

        await EjecutarAsync(sentencia, Primero);

        var despues = await LeerAsync(Primero);
        despues.Version.Should().BeGreaterThan(antes.Version);
        despues.CambiadoEn.Should().BeOnOrAfter(antes.CambiadoEn);
    }

    [PostgresFact]
    public async Task AC2_UnGuardadoPorEfTambienSubeLaVersion()
    {
        await SembrarAsync(Primero, 1);
        var antes = await LeerAsync(Primero);

        await using (var ctx = NewContext())
        {
            var tramite = await ctx.ProcedureInstances.SingleAsync(p => p.Id == Primero);
            tramite.Status = TramiteEstado.Preparado;
            await ctx.SaveChangesAsync();
        }

        (await LeerAsync(Primero)).Version.Should().BeGreaterThan(antes.Version);
    }

    [PostgresFact]
    public async Task AC3_UnaVersionFijadaAManoLaPisaLaSecuencia()
    {
        await SembrarAsync(Primero, 1);
        var antes = await LeerAsync(Primero);

        await EjecutarAsync("UPDATE tramites.procedure_instances SET sync_version = 1 WHERE id = @id", Primero);
        var trasBajarla = await LeerAsync(Primero);
        await EjecutarAsync("UPDATE tramites.procedure_instances SET sync_version = 9223372036854775000 WHERE id = @id", Primero);
        var trasSubirla = await LeerAsync(Primero);

        trasBajarla.Version.Should().BeGreaterThan(antes.Version);
        trasSubirla.Version.Should().Be(trasBajarla.Version + 1, "la siguiente de la secuencia, no el valor enviado");
    }

    [PostgresFact]
    public async Task ReaplicarElUp_NoProduceError()
    {
        var up = new HU13073_SyncVersionTramite().UpOperations.OfType<SqlOperation>().Single().Sql;
        await using var conn = await Fixture.OpenConnectionAsync();

        for (var i = 0; i < 2; i++)
        {
            await using var cmd = new NpgsqlCommand(up, conn);
            var reaplicar = async () => await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await reaplicar.Should().NotThrowAsync();
        }
    }

    private async Task SembrarAsync(Guid tramite, int n)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Cliente, "IT-13073", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13073@flit.test",
                DisplayName = "Gestor 13073",
                Status = "active",
                HomeTenantId = Cliente,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await ctx.SaveChangesAsync();
        }

        await SembrarTramiteAsync(tramite, n);
    }

    private async Task SembrarTramiteAsync(Guid id, int n)
    {
        await using var ctx = NewContext();
        var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();

        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = Cliente,
            ProcedureTypeId = tipo,
            ReferenceNumber = $"IT13073-{n}",
            Status = TramiteEstado.Borrador,
            Vin = $"VIN13073{n:D9}",
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<(long Version, DateTimeOffset CambiadoEn)> LeerAsync(Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT sync_version, sync_changed_at FROM tramites.procedure_instances WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        return (reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async Task EjecutarAsync(string sql, Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        (await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }
}
