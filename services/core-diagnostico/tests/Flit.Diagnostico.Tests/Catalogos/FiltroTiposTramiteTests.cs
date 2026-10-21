using Flit.Diagnostico.Domain.Catalogos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Catalogos;

/// <summary>
/// HU #13742 (F01-H2), escenario 1 — unitario del filtro de la etapa: se ofrece por código de la lista blanca, nunca por
/// nombre. Un tipo con el mismo nombre que uno habilitado y otro código no se ofrece.
/// </summary>
/// <example>
/// <code>
/// FiltroTiposTramite.Habilitados(publicados, ["TRASPASO_STANDARD"]) // → solo el de código TRASPASO_STANDARD
/// </code>
/// </example>
public sealed class FiltroTiposTramiteTests
{
    private static readonly string[] Etapa = ["TRASPASO_STANDARD", "TRASPASO_UNILATERAL", "TRASPASO_TRANSFERENCIA_DE_DOMINIO"];

    [Fact]
    public void Un_tipo_con_el_mismo_nombre_y_otro_codigo_no_se_ofrece()
    {
        TipoTramite[] publicados =
        [
            new("TRASPASO_INDETERMINADA", "TRASPASO", "TRASPASO", []),
            new("TRASPASO_STANDARD", "TRASPASO", "TRASPASO", []),
        ];

        var habilitados = FiltroTiposTramite.Habilitados(publicados, Etapa);

        habilitados.Select(t => t.Codigo).Should().Equal("TRASPASO_STANDARD");
    }

    [Fact]
    public void Solo_los_codigos_de_la_lista_blanca_en_el_orden_publicado()
    {
        TipoTramite[] publicados =
        [
            new("MATRICULA_INICIAL", "MATRÍCULA INICIAL", "MATRICULAS", []),
            new("TRASPASO_STANDARD", "TRASPASO", "TRASPASO", []),
            new("TRASPASO_TRANSFERENCIA_DE_DOMINIO", "TRANSFERENCIA DE DOMINIO", "TRASPASO", []),
            new("TRASPASO_UNILATERAL", "TRASPASO UNILATERAL", "TRASPASO", []),
        ];

        FiltroTiposTramite.Habilitados(publicados, Etapa).Select(t => t.Codigo)
            .Should().Equal("TRASPASO_STANDARD", "TRASPASO_TRANSFERENCIA_DE_DOMINIO", "TRASPASO_UNILATERAL");
    }

    [Fact]
    public void Lista_blanca_vacia_no_ofrece_nada()
    {
        FiltroTiposTramite.Habilitados([new TipoTramite("TRASPASO_STANDARD", "TRASPASO", "TRASPASO", [])], []).Should().BeEmpty();
    }
}
