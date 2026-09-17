using Flit.Analytics.Application.CompanyQueries;
using FluentAssertions;
using Xunit;

namespace Flit.Analytics.Application.Tests.CompanyQueries;

/// <summary>
/// ADR-0059 (Epic #12549) — el campo «Estado del trámite» de Consultas tiene que ofrecer TODO el
/// ciclo de vida. Si el catálogo se queda corto, un trámite en Preasignación o Revocado existe en la
/// base pero no hay forma de pedirlo desde la consola: la consulta «parece» completa y no lo es.
/// </summary>
public sealed class CompanyQueryFieldCatalogTests
{
    [Fact]
    public void Estado_OfreceLosEstadosDeLaRutaLargaYElRevocado()
    {
        var estado = CompanyQueryFieldCatalog.Fields.Single(f => f.Id == CompanyQueryFieldCatalog.Estado);
        var valores = estado.Options.Select(o => o.Value).ToList();

        valores.Should().ContainInOrder("preparado", "preasignacion", "asignado", "entregado");
        valores.Should().Contain(["revocado", "subsanacion"]);
        estado.Options.Single(o => o.Value == "preasignacion").Label.Should().Be("Preasignación");
    }
}
