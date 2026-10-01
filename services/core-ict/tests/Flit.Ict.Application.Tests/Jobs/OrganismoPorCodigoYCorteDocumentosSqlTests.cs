using Flit.Ict.Domain.Trazabilidad;
using Flit.Ict.Infrastructure.Jobs;
using Flit.Ict.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Jobs;

/// <summary>
/// Bug #13109, puntos 1 y 4 y la nota de documentos pendientes. Leen el DDL embebido, igual que
/// <see cref="SpProcessorValidationBusinessSqlTests"/>: el repo de core-ict no levanta PostgreSQL en
/// tests, así que la regla se ancla en el texto canónico de los SP y del job.
/// </summary>
/// <example>
/// var sql = EmbeddedDdl.LoadUp("24-ICT-master-transit-office-id.sql");
/// sql.Should().Contain("ADD COLUMN IF NOT EXISTS transit_office_id uuid");
/// </example>
public sealed class OrganismoPorCodigoYCorteDocumentosSqlTests
{
    private const string EsperaDocumentos =
        "(closed_document = true OR process_without_attached_documents = true)";

    private static readonly string Business = EmbeddedDdl.LoadUp("05-ICT-sp-business.sql");
    private static readonly string External = EmbeddedDdl.LoadUp("06-ICT-sp-external.sql");

    [Fact]
    public void P1_ElMasterTieneColumnaParaElOrganismoResueltoPorCodigo()
    {
        var ddl = EmbeddedDdl.LoadUp("24-ICT-master-transit-office-id.sql");

        ddl.Should().Contain("ALTER TABLE ict.external_integration_master");
        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS transit_office_id uuid");
        ddl.Should().Contain("COMMENT ON COLUMN ict.external_integration_master.transit_office_id");
        EmbeddedDdl.AllScriptsInOrder().Should().Contain("24-ICT-master-transit-office-id.sql");
    }

    [Fact]
    public void P1_ElSpDeNegocioGuardaElIdDelOrganismoConElMismoJoinDelGrant()
    {
        var start = Business.IndexOf("SET (transit_office_id", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "el SP de negocio debe escribir el id que resolvió por código");
        var end = Business.IndexOf(';', start);
        var statement = Business[start..end];

        statement.Should().Contain("catalogs.transit_offices ts");
        statement.Should().Contain("JOIN admin.tenant_transit_office_grants g");
        statement.Should().Contain("g.tenant_id = rec.tenant_id");
        statement.Should().Contain("g.is_enabled = true");
        statement.Should().Contain("ts.code = eim.traffic_secretary_code");
        statement.Should().Contain("ts.is_active = true");
    }

    [Fact]
    public void P1_ElMasterTieneColumnasParaNombreYMunicipioDelOrganismo()
    {
        var ddl = EmbeddedDdl.LoadUp("25-ICT-master-transit-office-name.sql");

        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS transit_office_name varchar(200)");
        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS transit_office_city_code varchar(10)");
        ddl.Should().Contain("COMMENT ON COLUMN ict.external_integration_master.transit_office_name");
        ddl.Should().Contain("COMMENT ON COLUMN ict.external_integration_master.transit_office_city_code");
        var orden = EmbeddedDdl.AllScriptsInOrder().ToList();
        orden.Should().Contain("25-ICT-master-transit-office-name.sql");
        orden.IndexOf("25-ICT-master-transit-office-name.sql").Should()
            .BeGreaterThan(orden.IndexOf("24-ICT-master-transit-office-id.sql"));
    }

    [Fact]
    public void P1_ElSpGuardaIdNombreYMunicipioDeLaMismaFilaDelCatalogo()
    {
        // Asignación por fila: los tres salen del MISMO SELECT, así que no pueden desalinearse, y si el
        // código no se resuelve (sin fila) Postgres deja los tres en NULL.
        var start = Business.IndexOf("SET (transit_office_id", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var end = Business.IndexOf(';', start);
        var statement = Business[start..end];

        statement.Should().Contain("SET (transit_office_id, transit_office_name, transit_office_city_code) = (");
        statement.Should().Contain("SELECT ts.id, ts.name, ts.city_code");
        statement.Should().Contain("LIMIT 1)");
        System.Text.RegularExpressions.Regex.Count(statement, "SELECT").Should().Be(1,
            "una sola subconsulta: nombre y municipio no se resuelven aparte del id");
        Business.Should().NotContain("SET transit_office_id =",
            "no queda una asignación suelta del id que pueda divergir del nombre");
    }

    [Fact]
    public void P4_ElSpExternoNoIdentificaFuentesMientrasFaltenDocumentos()
    {
        var start = External.IndexOf("FOR rec IN", StringComparison.Ordinal);
        var end = External.IndexOf("LOOP", start, StringComparison.Ordinal);
        var seleccion = External[start..end];

        seleccion.Should().Contain("(m.closed_document = true OR m.process_without_attached_documents = true)");
    }

    [Fact]
    public void P4_ElIndiceParcialDelSpExternoFiltraIgual()
    {
        var start = External.IndexOf("CREATE INDEX IF NOT EXISTS ix_eim_pending_external", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        var end = External.IndexOf(';', start);
        External[start..end].Should().Contain(EsperaDocumentos);
    }

    [Fact]
    public void P4_ElJobExternoNoCuentaComoPendienteLoQueEsperaDocumentos()
    {
        ExternalValidationJob.PendingExternalSql.Should().Contain(EsperaDocumentos);
    }

    [Fact]
    public void Mejora_ElSpDeNegocioDejaLaNotaUnaSolaVezCuandoFaltanDocumentos()
    {
        var start = Business.IndexOf(NotasRecorrido.DocumentosPendientes, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "la nota va en el paso de negocio");
        var bloque = Business[Math.Max(0, start - 900)..start];

        bloque.Should().Contain("rec.closed_document = FALSE AND rec.process_without_attached_documents = FALSE");
        bloque.Should().Contain("NOT EXISTS");
        bloque.Should().Contain("ict.pretramite_events");
        bloque.Should().Contain("'" + NotasRecorrido.OutcomeDocumentosPendientes + "'");
        bloque.Should().Contain("ict.record_pretramite_event");
        bloque.Should().Contain("'en_validacion_negocio'");
    }

    [Fact]
    public void Mejora_LaNotaNoLaEscribeElPasoDeFuentesExternas()
    {
        External.Should().NotContain(NotasRecorrido.DocumentosPendientes);
        External.Should().NotContain(NotasRecorrido.OutcomeDocumentosPendientes);
    }
}
