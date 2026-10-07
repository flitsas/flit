using System.Text.Json;
using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13373 (AC1, ADR-0070 D8) — <see cref="ConsolidadoLoteAuditoria"/>: <c>filter_summary</c> guarda la forma del
/// filtro y nunca sus valores personales (listas pegadas como conteo; texto libre como presente/longitud).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var json = ConsolidadoLoteAuditoria.ResumirSeleccion(new SeleccionPorIds(ids));
/// </code>
/// </remarks>
public sealed class ConsolidadoLoteAuditoriaTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ListasPegadas_SeGuardanComoConteo_SinNingunValor()
    {
        var placas = Enumerable.Range(1, 12).Select(i => $"PEG{i:D3}").ToList();
        var criterios = new ProcedureInstanceListRequest
        {
            Condiciones =
            [
                new QueryCondition("placa", QueryOperator.EsAlguno, placas),
                new QueryCondition("compania", QueryOperator.EsAlguno, ["NIT900123456"]),
            ],
        };

        var json = ConsolidadoLoteAuditoria.ResumirSeleccion(new SeleccionPorFiltro(new TramitesLoteFiltro(criterios)));

        json.Should().NotContain("PEG").And.NotContain("NIT900123456");
        var condiciones = Parse(json).GetProperty("filtro").GetProperty("condiciones");
        condiciones.GetArrayLength().Should().Be(2);
        condiciones[0].GetProperty("campo").GetString().Should().Be("placa");
        condiciones[0].GetProperty("operador").GetString().Should().Be(QueryOperator.EsAlguno);
        condiciones[0].GetProperty("cantidad").GetInt32().Should().Be(12);
        condiciones[1].GetProperty("cantidad").GetInt32().Should().Be(1, "la condición compañía del Super Admin también se cuenta (A4.7)");
    }

    [Fact]
    public void TextoLibre_SeGuardaComoPresenteYLongitud()
    {
        var criterios = new ProcedureInstanceListRequest
        {
            Busqueda = "  Pedro Perez 1020304050 ",
            Placa = "ABC",
            Vendedor = "Maria",
            OrganismoTransito = "Bogota",
        };

        var json = ConsolidadoLoteAuditoria.ResumirSeleccion(new SeleccionPorFiltro(new TramitesLoteFiltro(criterios)));

        json.Should().NotContain("Pedro").And.NotContain("1020304050").And.NotContain("ABC").And.NotContain("Maria").And.NotContain("Bogota");
        var filtro = Parse(json).GetProperty("filtro");
        filtro.GetProperty("busqueda").GetProperty("presente").GetBoolean().Should().BeTrue();
        filtro.GetProperty("busqueda").GetProperty("longitud").GetInt32().Should().Be("Pedro Perez 1020304050".Length);
        filtro.GetProperty("placa").GetProperty("longitud").GetInt32().Should().Be(3);
    }

    [Fact]
    public void ValoresDeCatalogo_FechasYBanderas_SeGuardanTalCual_YLoQueNoLlegaNoAparece()
    {
        var desde = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        var criterios = new ProcedureInstanceListRequest
        {
            Estados = ["radicado", "aprobado"],
            Modalidad = "TRASPASO",
            BusquedaRapida = "mis_tramites",
            Prioritario = true,
            CreatedFrom = desde,
            TenantId = Guid.NewGuid(),
            UsuarioActualId = Guid.NewGuid(),
        };

        var json = ConsolidadoLoteAuditoria.ResumirSeleccion(
            new SeleccionPorFiltro(new TramitesLoteFiltro(criterios), [Guid.NewGuid()]));

        var raiz = Parse(json);
        raiz.GetProperty("modo").GetString().Should().Be("filtro");
        raiz.GetProperty("origenFiltro").GetString().Should().Be("tramites");
        raiz.GetProperty("excluidos").GetProperty("cantidad").GetInt32().Should().Be(1);
        var filtro = raiz.GetProperty("filtro");
        filtro.GetProperty("estados").EnumerateArray().Select(e => e.GetString()).Should().Equal("radicado", "aprobado");
        filtro.GetProperty("modalidad").GetString().Should().Be("TRASPASO");
        filtro.GetProperty("prioritario").GetBoolean().Should().BeTrue();
        filtro.GetProperty("createdFrom").GetString().Should().Be(desde.ToString("O"));
        filtro.TryGetProperty("vin", out _).Should().BeFalse("solo se registran los criterios presentes");
        json.Should().NotContain(criterios.TenantId!.Value.ToString("D"), "tenant y usuario salen del token, no del filtro");
        json.Should().NotContain(criterios.UsuarioActualId!.Value.ToString("D"));
    }

    [Fact]
    public void ModoIds_SoloElConteo()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        var json = ConsolidadoLoteAuditoria.ResumirSeleccion(new SeleccionPorIds(ids));

        Parse(json).GetProperty("ids").GetProperty("cantidad").GetInt32().Should().Be(5);
        ids.Should().OnlyContain(id => !json.Contains(id.ToString("D"), StringComparison.Ordinal));
        ConsolidadoLoteAuditoria.ContarIds(new SeleccionPorIds(ids)).Should().Be(5);
        ConsolidadoLoteAuditoria.ContarExcluidos(new SeleccionPorIds(ids)).Should().BeNull();
    }

    [Fact]
    public void FiltroSinResumenDeclarado_NoDejaValores()
    {
        var json = ConsolidadoLoteAuditoria.ResumirSeleccion(new SeleccionPorFiltro(new FiltroAjeno("ABC123")));

        Parse(json).GetProperty("origenFiltro").GetString().Should().Be("no_resumible");
        json.Should().NotContain("ABC123");
    }

    [Fact]
    public void FiltroResumible_DeclaraSuPropioResumen_YElOrganismoSeAudita()
    {
        var organismo = Guid.NewGuid();

        var json = ConsolidadoLoteAuditoria.ResumirSeleccion(
            new SeleccionPorFiltro(new FiltroResumible("XYZ987")), organismo);

        var raiz = Parse(json);
        raiz.GetProperty("origenFiltro").GetString().Should().Be("prueba");
        raiz.GetProperty("filtro").GetProperty("placa").GetProperty("longitud").GetInt32().Should().Be(6);
        raiz.GetProperty("organismo").GetString().Should().Be(organismo.ToString("D"));
        json.Should().NotContain("XYZ987");
    }

    private sealed record FiltroAjeno(string Placa) : LoteFiltro;

    private sealed record FiltroResumible(string Placa) : LoteFiltro, ILoteFiltroResumible
    {
        public string OrigenFiltro => "prueba";

        public void Resumir(LoteFiltroResumen resumen) => resumen.TextoLibre("placa", Placa);
    }
}
