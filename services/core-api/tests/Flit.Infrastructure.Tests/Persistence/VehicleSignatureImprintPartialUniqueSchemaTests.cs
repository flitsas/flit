using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

public sealed class VehicleSignatureImprintPartialUniqueSchemaTests
{
    [Fact]
    public void Migration99_ReplacesGlobalUniqueWithPartialIndexOnActiveRows()
    {
        var sql = EmbeddedDdl.LoadUp("99-vehicle-signature-imprints-partial-unique-hash.sql");

        sql.Should().Contain("DROP CONSTRAINT IF EXISTS uq_vehicle_signature_imprints_document_hash");
        sql.Should().Contain("uq_vehicle_signature_imprints_document_hash_active");
        sql.Should().Contain("WHERE deleted_at IS NULL");
        sql.Should().NotContain("CONSTRAINT uq_vehicle_signature_imprints_document_hash UNIQUE");
    }

    [Fact]
    public void Greenfield97_UsesPartialUniqueOnDocumentHash()
    {
        var sql = EmbeddedDdl.LoadUp("97-vehicle-signature-imprints.sql");

        sql.Should().Contain("uq_vehicle_signature_imprints_document_hash_active");
        sql.Should().Contain("WHERE deleted_at IS NULL");
        sql.Should().NotContain("CONSTRAINT uq_vehicle_signature_imprints_document_hash UNIQUE");
    }
}
