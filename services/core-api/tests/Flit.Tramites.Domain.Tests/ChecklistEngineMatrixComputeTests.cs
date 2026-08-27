using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #10522 (RF17/RF22 — matriz viva) — cómputo del checklist tomando como base la matriz
/// documental resuelta del gestor (<see cref="ChecklistEngine.ComputeFromMatrix"/>): la lista,
/// obligatoriedad y orden salen de la matriz (el gestor manda), y las capas condicional/gestora
/// siguen aplicándose encima.
/// </summary>
public sealed class ChecklistEngineMatrixComputeTests
{
    private static readonly IReadOnlyList<ChecklistItem> Matriz =
    [
        new ChecklistItem("factura", "Factura de Venta", Obligatorio: true, DocTipo: "factura"),
        new ChecklistItem("aduana", "Aduana", Obligatorio: true, DocTipo: "aduana"),
        new ChecklistItem("soat", "SOAT", Obligatorio: false, DocTipo: "soat"),
    ];

    private static ChecklistResultado Compute(
        IReadOnlyList<ChecklistItem> matriz,
        IReadOnlyDictionary<string, bool>? manual = null,
        IReadOnlyCollection<string>? docTipos = null,
        IReadOnlyCollection<CompanyDocumentParam>? parametros = null) =>
        ChecklistEngine.ComputeFromMatrix(
            "matricula_inicial", matriz, manual, docTipos, new TramiteDocumentContext(), null, parametros);

    [Fact]
    public void Matriz_DefineLista_Obligatoriedad_Y_Orden()
    {
        var r = Compute(Matriz);

        r.Items.Select(i => i.Item.Id).Should().Equal("factura", "aduana", "soat");
        r.Items.Single(i => i.Item.Id == "factura").Item.Obligatorio.Should().BeTrue();
        r.Items.Single(i => i.Item.Id == "soat").Item.Obligatorio.Should().BeFalse();
        r.ObligatoriosTotal.Should().Be(2);
    }

    [Fact]
    public void ExcludeFromGestorCarga_QuitaGenerados_YRecalculaCompleto()
    {
        var r = Compute(Matriz);
        var filtrado = ChecklistEngine.ExcludeFromGestorCarga(
            r, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "soat", "aduana" });

        filtrado.Items.Select(i => i.Item.Id).Should().Equal("factura");
        filtrado.FaltanObligatorios.Should().Contain("factura");
        filtrado.FaltanObligatorios.Should().NotContain("aduana");
        filtrado.Completo.Should().BeFalse();
    }

    [Fact]
    public void Matriz_DocSubido_AutoMarcaSatisfecho()
    {
        var r = Compute(Matriz, docTipos: ["factura"]);

        r.Items.Single(i => i.Item.Id == "factura").Satisfecho.Should().BeTrue();
        r.FaltanObligatorios.Should().Contain("aduana").And.NotContain("factura");
        r.Completo.Should().BeFalse();
    }

    [Fact]
    public void Matriz_GestorMandaObligatoriedad_AunqueDifieraDelCatalogo()
    {
        // El gestor marca "soat" obligatorio (el catálogo vivo lo tiene opcional): manda la matriz.
        IReadOnlyList<ChecklistItem> conSoatObligatorio =
        [
            new ChecklistItem("factura", "Factura", Obligatorio: true, DocTipo: "factura"),
            new ChecklistItem("soat", "SOAT", Obligatorio: true, DocTipo: "soat"),
        ];

        var r = Compute(conSoatObligatorio);

        r.FaltanObligatorios.Should().Contain("soat");
    }

    [Fact]
    public void Matriz_ParametroGestora_Opcional_RelajaObligatoriedad_RF31()
    {
        var r = Compute(Matriz, parametros: [new CompanyDocumentParam("aduana", CompanyDocumentParamState.Opcional)]);

        r.Items.Single(i => i.Item.Id == "aduana").Item.Obligatorio.Should().BeFalse();
        r.FaltanObligatorios.Should().NotContain("aduana");
    }
}
