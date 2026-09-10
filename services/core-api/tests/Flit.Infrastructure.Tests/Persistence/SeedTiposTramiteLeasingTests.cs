using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>Seed de matrícula leasing y traspasos de locatario en procedure_types.</summary>
public sealed class SeedTiposTramiteLeasingTests
{
    [Fact]
    public void Seed_InsertsThreePublishedTypes_IdempotentByCode()
    {
        var sql = EmbeddedDdl.LoadUp("78-seed-tipos-tramite-leasing.sql");

        sql.Should().Contain("MATRICULA_LEASING");
        sql.Should().Contain("Matrícula Leasing");
        sql.Should().Contain("'MATRICULAS'");
        sql.Should().Contain("Matrícula con locatario.");

        sql.Should().Contain("TRASPASO_UNILATERAL");
        sql.Should().Contain("Traspaso Unilateral");
        sql.Should().Contain("Traspaso unilateral a locatario.");

        sql.Should().Contain("TRASPASO_TRANSFERENCIA_DE_DOMINIO");
        sql.Should().Contain("Traspaso con Transferencia de Dominio");
        sql.Should().Contain("Traspaso de un locatario a otro.");

        sql.Should().Contain("ON CONFLICT (code) DO NOTHING");
        sql.Should().Contain("'published'");
    }
}
