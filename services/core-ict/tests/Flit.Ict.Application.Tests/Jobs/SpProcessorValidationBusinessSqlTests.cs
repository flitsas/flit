using Flit.Ict.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Jobs;

/// <summary>
/// El procesador ICT aplica las reglas de negocio vía DDL embebido
/// (<c>05-ICT-sp-business.sql</c>). Estos tests leen el SP canónico para anclar los AC
/// de HU #12517 (grant OT) y HU #12518 (placa de traspaso).
/// </summary>
/// <example>
/// var sql = EmbeddedDdl.LoadUp("05-ICT-sp-business.sql");
/// sql.Should().Contain("traffic_secretary_code no esta habilitado para la compania");
/// </example>
public sealed class SpProcessorValidationBusinessSqlTests
{
    private static readonly string Sql = EmbeddedDdl.LoadUp("05-ICT-sp-business.sql");

    private const string CatalogNovedad = "traffic_secretary_code no es valido o no esta activo";
    private const string GrantNovedad = "traffic_secretary_code no esta habilitado para la compania";
    private const string PlateNovedad = "Ya existe un tramite activo para la placa";

    [Fact]
    public void Hu12517_Ac1_GrantHabilitado_NoDisparaNovedadDeCompania()
    {
        Sql.Should().Contain("admin.tenant_transit_office_grants");
        Sql.Should().Contain("g.is_enabled = true");
        Sql.Should().Contain("g.tenant_id = rec.tenant_id");
        Sql.Should().Contain("NOT EXISTS");
        Sql.Should().Contain(GrantNovedad);
    }

    [Fact]
    public void Hu12517_Ac2_OtActivoSinGrant_UsaMensajeDistintoAlDeCatalogo()
    {
        Sql.Should().Contain(CatalogNovedad);
        Sql.Should().Contain(GrantNovedad);
        GrantNovedad.Should().NotBe(CatalogNovedad);
        Sql.Should().Contain("process_status_id = 4");
        Sql.Should().Contain("con_novedades");
    }

    [Fact]
    public void Hu12517_Ac3_CodigoInvalidoOInactivo_SigueUsandoNovedadDeCatalogo()
    {
        Sql.Should().Contain("ts.code = eim.traffic_secretary_code AND ts.is_active = true");
        var grantBlockStart = Sql.IndexOf(GrantNovedad, StringComparison.Ordinal);
        grantBlockStart.Should().BeGreaterThan(0);
        var grantBlock = Sql[grantBlockStart..];
        grantBlock.Should().Contain("AND EXISTS (");
        grantBlock.Should().Contain("ts.is_active = true");
        Sql.Should().NotContain("TODO(ICT-GRANT)");
    }

    [Fact]
    public void Hu12518_Ac1_PlacaEnAnuladoOAprobado_NoCuentaComoTramiteActivo()
    {
        var plateBlock = PlateDuplicityBlock();
        plateBlock.Should().Contain("NOT IN ('anulado', 'rechazado', 'aprobado')");
        plateBlock.Should().Contain(PlateNovedad);
    }

    [Fact]
    public void Hu12518_Ac2_TraspasoEnProceso_SigueGenerandoNovedad()
    {
        var plateBlock = PlateDuplicityBlock();
        plateBlock.Should().Contain(PlateNovedad);
        plateBlock.Should().Contain("NOT IN ('anulado', 'rechazado', 'aprobado')");
        Sql.Should().Contain("process_status_id = 4");
        Sql.Should().Contain("con_novedades");
    }

    [Fact]
    public void Hu12518_Ac3_DuplicadoSeLimitaAFamiliaTraspasoYTenant()
    {
        var plateBlock = PlateDuplicityBlock();
        plateBlock.Should().Contain("pt.family = 'TRASPASO'");
        plateBlock.Should().Contain("pi.tenant_id = rec.tenant_id");
        plateBlock.Should().Contain("upper(btrim(");
        plateBlock.Should().Contain("JOIN tramites.procedure_types pt");
        Sql.Should().Contain("Ya existe un tramite activo para el VIN;");
        var vinBlockStart = Sql.IndexOf("Ya existe un tramite activo para el VIN;", StringComparison.Ordinal);
        var vinNearby = Sql.Substring(Math.Max(0, vinBlockStart - 400), 400);
        vinNearby.Should().Contain("NOT IN ('anulado', 'rechazado')");
        vinNearby.Should().NotContain("'aprobado'");
    }

    private static string PlateDuplicityBlock()
    {
        var start = Sql.IndexOf("Placa activa (traspasos 3/4", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var end = Sql.IndexOf("Otros trámites (5-16)", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return Sql[start..end];
    }
}
