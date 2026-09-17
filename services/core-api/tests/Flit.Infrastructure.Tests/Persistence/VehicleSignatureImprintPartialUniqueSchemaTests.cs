using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// Bug #12594 — la idempotencia de la firma de impronta manual es por <b>trámite + hash</b>, no por
/// hash global. Esta suite no tiene Postgres (ver <see cref="ConsecutivoGlobalTramiteTests"/>), así
/// que aquí se verifica que el DDL <c>115-B12594</c> diga lo que debe y que el modelo de EF declare
/// el índice compuesto. Los tres casos con filas reales (mismo hash en trámites distintos convive;
/// misma llave activa choca; activa + soft-deleted conviven) están en
/// <c>Flit.Integration.Tests/Tramites/VehicleSignatureImprintIdempotenciaPorTramiteTests</c>.
/// </summary>
public sealed class VehicleSignatureImprintPartialUniqueSchemaTests
{
    private const string Ddl115 = "115-B12594-vehicle-signature-imprints-idempotencia-por-tramite.sql";
    private const string OldIndex = "uq_vehicle_signature_imprints_document_hash_active";
    private const string NewIndex = "uq_vehicle_signature_imprints_instance_document_hash_active";

    [Fact]
    public void Migration115_ReplacesGlobalHashIndexWithInstanceAndHashIndex()
    {
        var sql = EmbeddedDdl.LoadUp(Ddl115);

        sql.Should().Contain($"DROP INDEX IF EXISTS tramites.{OldIndex};");
        sql.Should().Contain($"CREATE UNIQUE INDEX IF NOT EXISTS {NewIndex}");
        sql.Should().Contain("(procedure_instance_id, document_hash)");
        sql.Should().Contain("WHERE deleted_at IS NULL");
        sql.Should().Contain("idempotencia por trámite entre filas activas");
    }

    [Fact]
    public void Migration115_NewIndexIsPartialOnActiveRowsAndKeyedByInstanceFirst()
    {
        var sql = EmbeddedDdl.LoadUp(Ddl115);

        var create = sql[sql.IndexOf($"CREATE UNIQUE INDEX IF NOT EXISTS {NewIndex}", StringComparison.Ordinal)..];
        create = create[..create.IndexOf(';', StringComparison.Ordinal)];

        create.Should().Contain("ON tramites.vehicle_signature_imprints (procedure_instance_id, document_hash)",
            "la llave es trámite + hash, con el trámite primero para servir la búsqueda por instancia");
        create.Should().Contain("WHERE deleted_at IS NULL", "soft-deleted conserva historial sin bloquear");
        create.Should().NotContain("tenant_id", "procedure_instance_id ya identifica el trámite; tenant_id lo cubre el RLS");
    }

    [Fact]
    public void Migration115_IsIdempotentAndCarriesNoData()
    {
        var sql = EmbeddedDdl.LoadUp(Ddl115);

        sql.Should().Contain("DROP INDEX IF EXISTS");
        sql.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS");
        sql.Should().NotContainAny("INSERT INTO", "UPDATE tramites", "DELETE FROM");
    }

    [Fact]
    public void EfModel_DeclaresCompositeUniqueFilteredIndex()
    {
        using var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

        var entity = db.Model.FindEntityType(typeof(VehicleSignatureImprint))!;
        var indexes = entity.GetIndexes().ToList();

        indexes.Should().NotContain(i => i.GetDatabaseName() == OldIndex,
            "el índice global por hash es el defecto del Bug #12594");

        var composite = indexes.Should().ContainSingle(i => i.GetDatabaseName() == NewIndex).Subject;
        composite.IsUnique.Should().BeTrue();
        composite.GetFilter().Should().Be("deleted_at IS NULL");
        composite.Properties.Select(p => p.Name).Should().Equal(
            nameof(VehicleSignatureImprint.ProcedureInstanceId),
            nameof(VehicleSignatureImprint.DocumentHash));
    }

    /// <summary>Historia: 99 sustituyó el UNIQUE global por parcial; 115 lo vuelve a acotar por trámite.</summary>
    [Fact]
    public void Migration99_StillDropsLegacyUniqueConstraint()
    {
        var sql = EmbeddedDdl.LoadUp("99-vehicle-signature-imprints-partial-unique-hash.sql");

        sql.Should().Contain("DROP CONSTRAINT IF EXISTS uq_vehicle_signature_imprints_document_hash");
        sql.Should().Contain("WHERE deleted_at IS NULL");
        sql.Should().NotContain("CONSTRAINT uq_vehicle_signature_imprints_document_hash UNIQUE");
    }
}
