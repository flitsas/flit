using Flit.Queries.Domain;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12106 — validación de las condiciones contra el catálogo (AC5) y forma del catálogo (AC1).
///
/// <para>Lo que se protege aquí es que una condición inválida se RECHACE en vez de ignorarse.
/// Ignorarla devolvería un listado más amplio del que el usuario pidió, con la apariencia de estar
/// filtrado — y un resultado que parece correcto no lo revisa nadie.</para>
/// </summary>
public sealed class TramitesQueryConditionsTests
{
    private static QueryCondition Cond(string campo, string op, params string[] valores) =>
        new(campo, op, valores);

    [Fact]
    public void SinCondiciones_EsValido()
    {
        TramitesQueryConditions.Validate(null).Should().BeNull();
        TramitesQueryConditions.Validate([]).Should().BeNull();
    }

    [Fact]
    public void CampoDesconocido_SeRechazaNombrandolo()
    {
        var error = TramitesQueryConditions.Validate([Cond("radicado_runt", QueryOperator.EsAlguno, "X")]);

        error.Should().NotBeNull().And.Contain("radicado_runt");
    }

    [Fact]
    public void OperadorInexistente_SeRechaza()
    {
        var error = TramitesQueryConditions.Validate(
            [Cond(TramitesQueryFieldCatalog.Placa, "empieza_por", "ABC")]);

        error.Should().NotBeNull().And.Contain("empieza_por");
    }

    [Fact]
    public void OperadorQueElCampoNoAdmite_SeRechaza()
    {
        // "Estado" es lista cerrada: "contiene" sobre una opción no significa nada.
        var error = TramitesQueryConditions.Validate(
            [Cond(TramitesQueryFieldCatalog.Estado, QueryOperator.Contiene, "borr")]);

        error.Should().NotBeNull().And.Contain("Estado del trámite");
    }

    [Fact]
    public void OperadorUnarioConValores_SeRechaza()
    {
        var error = TramitesQueryConditions.Validate(
            [Cond(TramitesQueryFieldCatalog.Vin, QueryOperator.EstaVacio, "algo")]);

        error.Should().NotBeNull().And.Contain("no lleva valores");
    }

    [Fact]
    public void FiltroSinValor_SeRechaza()
    {
        var error = TramitesQueryConditions.Validate(
            [Cond(TramitesQueryFieldCatalog.Placa, QueryOperator.EsAlguno)]);

        error.Should().NotBeNull().And.Contain("Falta el valor");
    }

    [Fact]
    public void ContieneConVariosValores_SeRechaza()
    {
        // Con dos textos el resultado dependería de cuál se eligiera: mejor decirlo que decidir por él.
        var error = TramitesQueryConditions.Validate(
            [Cond(TramitesQueryFieldCatalog.Placa, QueryOperator.Contiene, "AB", "XY")]);

        error.Should().NotBeNull().And.Contain("un solo valor");
    }

    [Fact]
    public void OpcionFueraDelCatalogo_SeRechaza()
    {
        var error = TramitesQueryConditions.Validate(
            [Cond(TramitesQueryFieldCatalog.Fuente, QueryOperator.EsAlguno, "quipux")]);

        error.Should().NotBeNull().And.Contain("quipux");
    }

    [Fact]
    public void ListaEnCampoQueLaAdmite_EsValida()
    {
        var error = TramitesQueryConditions.Validate(
            [Cond(TramitesQueryFieldCatalog.Placa, QueryOperator.EsAlguno, "ABC123", "XYZ999", "DEF456")]);

        error.Should().BeNull();
    }

    [Fact]
    public void ListaEnCampoQueNoLaAdmite_SeRechaza()
    {
        var error = TramitesQueryConditions.Validate(
            [Cond(TramitesQueryFieldCatalog.Gestor, QueryOperator.EsAlguno, "Ana", "Luis")]);

        error.Should().NotBeNull().And.Contain("un solo valor");
    }

    // ── AC1 / AC4 — forma del catálogo ───────────────────────────────────────────────────────

    [Fact]
    public void ElCatalogoCubreLosCamposPedidos()
    {
        var ids = TramitesQueryFieldCatalog.Fields.Select(f => f.Id).ToList();

        ids.Should().Contain([
            TramitesQueryFieldCatalog.Radicado,
            TramitesQueryFieldCatalog.Placa,
            TramitesQueryFieldCatalog.Vin,
            TramitesQueryFieldCatalog.Comprador,
            TramitesQueryFieldCatalog.Vendedor,
            TramitesQueryFieldCatalog.Organismo,
            TramitesQueryFieldCatalog.TipoTramite,
            TramitesQueryFieldCatalog.Estado,
            TramitesQueryFieldCatalog.Gestor,
            TramitesQueryFieldCatalog.Fuente,
            TramitesQueryFieldCatalog.FirmaCompraventa,
        ]);
    }

    [Fact]
    public void FirmadoSeExponeConSuNombreHonesto()
    {
        var campo = TramitesQueryFieldCatalog.Find(TramitesQueryFieldCatalog.FirmaCompraventa);

        // El nombre viejo prometía el estado compuesto que se ve junto a cada actor, que además
        // considera identidad acreditada y firma del baúl. Este campo no es eso, y ahora lo dice.
        campo!.Label.Should().Be("Firma de compraventa");
        campo.Hint.Should().Contain("baúl");
    }

    [Fact]
    public void LosIdentificadoresSonLosQueSePeganDesdeExcel()
    {
        TramitesQueryFieldCatalog.IsIdentifier(TramitesQueryFieldCatalog.Placa).Should().BeTrue();
        TramitesQueryFieldCatalog.IsIdentifier(TramitesQueryFieldCatalog.Vin).Should().BeTrue();
        TramitesQueryFieldCatalog.IsIdentifier(TramitesQueryFieldCatalog.Radicado).Should().BeTrue();
        TramitesQueryFieldCatalog.IsIdentifier(TramitesQueryFieldCatalog.Comprador).Should().BeFalse();
    }

    [Fact]
    public void ElOrdenCubreLosSubcamposDeLasCeldasCompuestas()
    {
        // HU #12108: sin estas claves, la mitad del desplegable de orden no ordenaría nada.
        TramitesQuerySort.All.Should().Contain([
            TramitesQuerySort.Radicado,
            TramitesQuerySort.Estado,
            TramitesQuerySort.TipoTramite,
            TramitesQuerySort.Fuente,
        ]);
    }

    [Theory]
    [InlineData("radicado", ProcedureInstanceSortBy.Radicado)]
    [InlineData("estado", ProcedureInstanceSortBy.Estado)]
    [InlineData("tipo_tramite", ProcedureInstanceSortBy.TipoTramite)]
    [InlineData("fuente", ProcedureInstanceSortBy.Fuente)]
    [InlineData("no_existe", ProcedureInstanceSortBy.Default)]
    public void LaListaBlancaDeOrdenResuelveLasClavesNuevas(string sortBy, ProcedureInstanceSortBy esperado)
    {
        ProcedureInstanceSortFields.Resolve(sortBy).Should().Be(esperado);
    }
}
