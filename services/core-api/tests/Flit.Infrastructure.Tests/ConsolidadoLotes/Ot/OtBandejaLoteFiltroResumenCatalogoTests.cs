using System.Text.Json;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Infrastructure.ConsolidadoLotes.Ot;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.ConsolidadoLotes.Ot;

/// <summary>
/// HU #13390 (Épica #13216, hallazgo L3 de security-agent) — en el resumen del filtro de la bandeja del OT,
/// <c>status</c>, <c>familia</c>, <c>sortBy</c> y <c>sortDir</c> solo se guardan literales si pertenecen a su catálogo
/// (estados de <c>TramiteEstado</c>, familias de <c>ProcedureFamilyCodes</c>, lista blanca del orden de la bandeja,
/// <c>asc</c>/<c>desc</c>). Lo demás se minimiza a <c>{presente, longitud}</c>: <c>filter_summary</c> es append-only
/// y no puede retener texto libre (Ley 1581).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var json = ConsolidadoLoteAuditoria.ResumirSeleccion(
///     new SeleccionPorFiltro(new OtBandejaLoteFiltro(new OtClientProcedureFilter { Status = "entregado,aprobado" })));
/// // ..."filtro":{"status":["entregado","aprobado"]}
/// </code>
/// </remarks>
public sealed class OtBandejaLoteFiltroResumenCatalogoTests
{
    private const string Pii = "1037654321 JUAN PEREZ";

    private static JsonElement Filtro(OtClientProcedureFilter criterios, out string json)
    {
        json = ConsolidadoLoteAuditoria.ResumirSeleccion(new SeleccionPorFiltro(new OtBandejaLoteFiltro(criterios)));
        return JsonDocument.Parse(json).RootElement.GetProperty("filtro").Clone();
    }

    private static void DebeSerMinimo(JsonElement nodo, int longitud)
    {
        nodo.ValueKind.Should().Be(JsonValueKind.Object);
        nodo.GetProperty("presente").GetBoolean().Should().BeTrue();
        nodo.GetProperty("longitud").GetInt32().Should().Be(longitud);
    }

    [Fact]
    public void ValoresDeCatalogo_SeGuardanEnSuFormaCanonica()
    {
        var f = Filtro(new OtClientProcedureFilter
        {
            Status = " Entregado , aprobado ",
            Familia = " traspaso ",
            SortBy = " Placa ",
            SortDir = "ASC",
        }, out _);

        f.GetProperty("status").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("entregado", "aprobado");
        f.GetProperty("familia").GetString().Should().Be("TRASPASO");
        f.GetProperty("sortBy").GetString().Should().Be("placa");
        f.GetProperty("sortDir").GetString().Should().Be("asc");
    }

    [Theory]
    [InlineData("fechaRadicacion", "fecharadicacion")]
    [InlineData("referenceNumber", "referencenumber")]
    [InlineData("tipo_tramite", "tipo_tramite")]
    [InlineData("EMPRESA", "empresa")]
    [InlineData("createdAt", "createdat")]
    public void SortBy_DeLaListaBlancaDelOrdenDeLaBandeja_SeGuardaNormalizado(string sortBy, string canonico)
    {
        var f = Filtro(new OtClientProcedureFilter { SortBy = sortBy, SortDir = "desc" }, out _);

        f.GetProperty("sortBy").GetString().Should().Be(canonico);
        f.GetProperty("sortDir").GetString().Should().Be("desc");
    }

    [Fact]
    public void ValoresFueraDeCatalogo_ConPii_SeMinimizan()
    {
        var f = Filtro(new OtClientProcedureFilter
        {
            Status = Pii,
            Familia = "JUAN PEREZ",
            SortBy = Pii,
            SortDir = "PEREZ 1037654321",
        }, out var json);

        json.Should().NotContain("1037654321").And.NotContain("JUAN").And.NotContain("PEREZ");
        f.GetProperty("status").GetArrayLength().Should().Be(1);
        DebeSerMinimo(f.GetProperty("status")[0], Pii.Length);
        DebeSerMinimo(f.GetProperty("familia"), "JUAN PEREZ".Length);
        DebeSerMinimo(f.GetProperty("sortBy"), Pii.Length);
        DebeSerMinimo(f.GetProperty("sortDir"), "PEREZ 1037654321".Length);
    }

    [Fact]
    public void StatusMixto_GuardaElValidoYMinimizaLaBasura()
    {
        var f = Filtro(new OtClientProcedureFilter { Status = $"entregado,{Pii}, ,Rechazado" }, out var json);

        json.Should().NotContain("1037654321").And.NotContain("JUAN");
        var status = f.GetProperty("status");
        status.GetArrayLength().Should().Be(3, "los vacíos entre comas no se auditan");
        status[0].GetString().Should().Be("entregado");
        DebeSerMinimo(status[1], Pii.Length);
        status[2].GetString().Should().Be("rechazado");
    }

    [Fact]
    public void PseudoEstadoDelListadoDelGestor_NoEsDelCatalogoDeLaBandejaOt_SeMinimiza()
    {
        // La bandeja del OT compara el estado real (p.Status == e); «rechazado_preasignacion» solo existe en el
        // listado del gestor (ADR-0059), así que aquí no es un valor de catálogo.
        var f = Filtro(new OtClientProcedureFilter { Status = "rechazado_preasignacion" }, out _);

        DebeSerMinimo(f.GetProperty("status")[0], "rechazado_preasignacion".Length);
    }

    [Fact]
    public void LiteralLargoFueraDeCatalogo_NoDejaNingunFragmento()
    {
        var largo = new string('A', 40) + " " + Pii + " " + new string('B', 40);
        var f = Filtro(new OtClientProcedureFilter { Status = largo, SortBy = largo, SortDir = largo, Familia = largo }, out var json);

        json.Should().NotContain("AAAA").And.NotContain("BBBB").And.NotContain("JUAN");
        DebeSerMinimo(f.GetProperty("sortBy"), largo.Length);
    }
}
