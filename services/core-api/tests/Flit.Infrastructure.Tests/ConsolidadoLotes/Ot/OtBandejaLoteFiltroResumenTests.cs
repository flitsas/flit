using System.Text.Json;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Infrastructure.ConsolidadoLotes.Ot;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.ConsolidadoLotes.Ot;

/// <summary>
/// HU #13373 (ajuste 2026-10-07) — la minimización de <c>filter_summary</c> admite el filtro de la bandeja del OT:
/// búsqueda libre y campos de texto como <c>{presente, longitud}</c>, listas pegadas como conteo, y el organismo del
/// lote auditado (A5.5).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var json = ConsolidadoLoteAuditoria.ResumirSeleccion(new SeleccionPorFiltro(new OtBandejaLoteFiltro(filtro)), organismo);
/// </code>
/// </remarks>
public sealed class OtBandejaLoteFiltroResumenTests
{
    [Fact]
    public void FiltroDeLaBandeja_SeMinimiza_YAuditaElOrganismo()
    {
        var organismo = Guid.NewGuid();
        var tipo = Guid.NewGuid();
        var filtro = new OtClientProcedureFilter
        {
            Busqueda = "Juan Gomez 79123456",
            Placa = "QWE12",
            Status = "radicado",
            Familia = "TRASPASO",
            ProcedureTypeId = tipo,
            HasActiveRevocationRequest = true,
            Condiciones = [new QueryCondition("placa", QueryOperator.EsAlguno, ["AAA111", "BBB222", "CCC333"])],
            SortBy = "placa",
            SortDir = "desc",
            Page = 3,
            PageSize = 50,
        };

        var json = ConsolidadoLoteAuditoria.ResumirSeleccion(
            new SeleccionPorFiltro(new OtBandejaLoteFiltro(filtro), [Guid.NewGuid(), Guid.NewGuid()]), organismo);

        json.Should().NotContain("Juan").And.NotContain("79123456").And.NotContain("QWE12")
            .And.NotContain("AAA111").And.NotContain("BBB222");
        var raiz = JsonDocument.Parse(json).RootElement;
        raiz.GetProperty("origenFiltro").GetString().Should().Be("ot_bandeja");
        raiz.GetProperty("organismo").GetString().Should().Be(organismo.ToString("D"));
        raiz.GetProperty("excluidos").GetProperty("cantidad").GetInt32().Should().Be(2);
        var f = raiz.GetProperty("filtro");
        f.GetProperty("busqueda").GetProperty("presente").GetBoolean().Should().BeTrue();
        f.GetProperty("busqueda").GetProperty("longitud").GetInt32().Should().Be("Juan Gomez 79123456".Length);
        f.GetProperty("placa").GetProperty("longitud").GetInt32().Should().Be(5);
        f.GetProperty("status").GetString().Should().Be("radicado");
        f.GetProperty("familia").GetString().Should().Be("TRASPASO");
        f.GetProperty("procedureTypeId").GetString().Should().Be(tipo.ToString("D"));
        f.GetProperty("hasActiveRevocationRequest").GetBoolean().Should().BeTrue();
        f.GetProperty("condiciones")[0].GetProperty("cantidad").GetInt32().Should().Be(3);
        f.TryGetProperty("page", out _).Should().BeFalse("la selección no tiene página");
    }
}
