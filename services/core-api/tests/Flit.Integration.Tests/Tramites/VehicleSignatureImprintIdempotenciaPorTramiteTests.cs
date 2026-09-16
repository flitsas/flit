using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #12594 — el índice único parcial de <c>tramites.vehicle_signature_imprints</c> pasa de
/// <c>(document_hash)</c> global a <c>(procedure_instance_id, document_hash) WHERE deleted_at IS NULL</c>
/// (DDL <c>115-B12594</c>). Contra el motor real:
/// <list type="bullet">
///   <item>(a) el mismo PDF base (mismo hash) firmado en dos trámites distintos se inserta;</item>
///   <item>(b) dos filas activas con la misma llave (trámite, hash) chocan con 23505 en el índice nuevo;</item>
///   <item>(c) una activa y una soft-deleted con la misma llave conviven (historial de auditoría).</item>
/// </list>
/// </summary>
public sealed class VehicleSignatureImprintIdempotenciaPorTramiteTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private const string NewIndex = "uq_vehicle_signature_imprints_instance_document_hash_active";
    private const string OldIndex = "uq_vehicle_signature_imprints_document_hash_active";
    private const string SameHash = "0000000000000000000000000000000000000000000000000000000000012594";

    private static readonly Guid InstanceA = new("a12594a1-0000-4000-8000-000000000001");
    private static readonly Guid InstanceB = new("a12594a1-0000-4000-8000-000000000002");

    [PostgresFact]
    public async Task La_migracion_deja_el_indice_por_tramite_y_quita_el_global()
    {
        var indexes = await IndexDefinitionsAsync();

        indexes.Keys.Should().NotContain(OldIndex, "el índice global por hash es el defecto del Bug #12594");
        indexes.Should().ContainKey(NewIndex);
        indexes[NewIndex].Should().Contain("UNIQUE INDEX")
            .And.Contain("(procedure_instance_id, document_hash)")
            .And.Contain("WHERE (deleted_at IS NULL)");
    }

    [PostgresFact]
    public async Task A_El_mismo_hash_en_dos_tramites_distintos_se_inserta()
    {
        await SeedTwoInstancesAsync();

        await using var ctx = NewContext();
        ctx.VehicleSignatureImprints.Add(Row(InstanceA, SameHash));
        ctx.VehicleSignatureImprints.Add(Row(InstanceB, SameHash));

        await ctx.Invoking(c => c.SaveChangesAsync()).Should().NotThrowAsync(
            "el hash es del contenido del PDF; el mismo archivo puede pertenecer a trámites diferentes");

        (await ctx.VehicleSignatureImprints.CountAsync(x => x.DocumentHash == SameHash)).Should().Be(2);
    }

    [PostgresFact]
    public async Task B_Dos_filas_activas_con_el_mismo_tramite_y_hash_chocan_en_el_indice_nuevo()
    {
        await SeedTwoInstancesAsync();

        await using (var first = NewContext())
        {
            first.VehicleSignatureImprints.Add(Row(InstanceA, SameHash));
            await first.SaveChangesAsync();
        }

        await using var second = NewContext();
        second.VehicleSignatureImprints.Add(Row(InstanceA, SameHash));

        var caught = await second.Invoking(c => c.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        var pg = caught.Which.InnerException.Should().BeOfType<PostgresException>().Which;
        pg.SqlState.Should().Be("23505");
        pg.ConstraintName.Should().Be(NewIndex);
    }

    [PostgresFact]
    public async Task C_Una_activa_y_una_soft_deleted_con_la_misma_llave_conviven()
    {
        await SeedTwoInstancesAsync();

        await using var ctx = NewContext();
        ctx.VehicleSignatureImprints.Add(Row(InstanceA, SameHash, deletedAt: DateTimeOffset.UtcNow));
        ctx.VehicleSignatureImprints.Add(Row(InstanceA, SameHash));

        await ctx.Invoking(c => c.SaveChangesAsync()).Should().NotThrowAsync(
            "el índice es parcial: la fila soft-deleted queda como historial y no bloquea la re-firma");

        // IgnoreQueryFilters solo para contar en la prueba: el filtro global oculta la soft-deleted.
        (await ctx.VehicleSignatureImprints.IgnoreQueryFilters()
            .CountAsync(x => x.ProcedureInstanceId == InstanceA && x.DocumentHash == SameHash)).Should().Be(2);
    }

    // ── datos ────────────────────────────────────────────────────────────────

    private async Task SeedTwoInstancesAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync();

        var type = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).FirstAsync();
        var user = new User
        {
            Id = new("a12594a1-0000-4000-8000-0000000000aa"),
            Email = "it-b12594@flit.test",
            DisplayName = "Impronta B12594",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.ProcedureInstances.AddRange(
            Instance(InstanceA, type.Id, user.Id),
            Instance(InstanceB, type.Id, user.Id));
        await ctx.SaveChangesAsync();
    }

    private static ProcedureInstance Instance(Guid id, Guid typeId, Guid userId) => new()
    {
        Id = id,
        TenantId = TenantSeed.LoneId,
        ProcedureTypeId = typeId,
        CreatedByUserId = userId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static VehicleSignatureImprint Row(Guid instanceId, string hash, DateTimeOffset? deletedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantSeed.LoneId,
        ProcedureInstanceId = instanceId,
        ModuleCode = "tramites",
        PrivateKey = "pem-privada-de-prueba",
        PublicKey = "pem-publica-de-prueba",
        DocumentHash = hash,
        Signature = "c2ln",
        SignedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        DeletedAt = deletedAt,
    };

    private async Task<Dictionary<string, string>> IndexDefinitionsAsync()
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'tramites' AND tablename = 'vehicle_signature_imprints'",
            connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }
}
