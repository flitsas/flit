using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Punto 4, incremento 2 (diseño §5.2, decisiones 4.2/4.3/4.6, ADR-0084) — una sola regla de elegibilidad para la grilla, el
/// individual y (incremento 3) la ejecución de cada ítem: vehículo terminado, dictamen vigente VIABLE (en los lotes Con
/// análisis), sin trámite y sin ítem vivo. Y el tipo de trámite del borrador: el del lote en Con análisis; el del cuerpo en Simple.
/// Sin base.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ElegibilidadDerivacionTests
/// </code>
/// </example>
public sealed class ElegibilidadDerivacionTests
{
    private static readonly DatosElegibilidad Elegible = new(Terminado: true, ExigeDictamen: true, Veredicto: "VIABLE", TieneTramite: false, ItemVivo: false);

    [Fact]
    public void Terminado_viable_sin_tramite_ni_item_vivo_es_elegible() =>
        ElegibilidadDerivacion.MotivoNoElegible(Elegible).Should().BeNull();

    [Fact]
    public void Con_trámite_creado_el_motivo_es_YA_TIENE_TRAMITE_aunque_falle_lo_demás() =>
        ElegibilidadDerivacion.MotivoNoElegible(Elegible with { TieneTramite = true, ItemVivo = true, Terminado = false, Veredicto = "BLOQUEADO" })
            .Should().Be(MotivoDerivacion.YaTieneTramite);

    [Fact]
    public void Con_un_ítem_vivo_el_motivo_es_EN_CURSO() =>
        ElegibilidadDerivacion.MotivoNoElegible(Elegible with { ItemVivo = true }).Should().Be(MotivoDerivacion.EnCurso);

    [Fact]
    public void Sin_terminar_el_motivo_es_NO_TERMINADO() =>
        ElegibilidadDerivacion.MotivoNoElegible(Elegible with { Terminado = false, Veredicto = null }).Should().Be(MotivoDerivacion.NoTerminado);

    [Theory]
    [InlineData("BLOQUEADO")]
    [InlineData(null)]
    public void Con_análisis_sin_dictamen_VIABLE_el_motivo_es_NO_VIABLE(string? veredicto) =>
        ElegibilidadDerivacion.MotivoNoElegible(Elegible with { Veredicto = veredicto }).Should().Be(MotivoDerivacion.NoViable);

    [Fact]
    public void Lote_Simple_no_tiene_dictamen_y_terminado_sin_trámite_es_elegible() =>
        ElegibilidadDerivacion.MotivoNoElegible(Elegible with { ExigeDictamen = false, Veredicto = null }).Should().BeNull();

    [Fact]
    public void Exige_dictamen_solo_en_Con_análisis()
    {
        ElegibilidadDerivacion.ExigeDictamen(ModoDiagnostico.ConAnalisis).Should().BeTrue();
        ElegibilidadDerivacion.ExigeDictamen(ModoDiagnostico.Simple).Should().BeFalse();
    }

    [Fact]
    public void Tipo_en_Con_análisis_es_el_del_lote_e_ignora_el_del_cuerpo() =>
        TipoTramiteDerivacion.Resolver(ModoDiagnostico.ConAnalisis, "TRASPASO_STANDARD", "TRASPASO_UNILATERAL").Should().Be("TRASPASO_STANDARD");

    [Fact]
    public void Tipo_en_Simple_es_el_del_cuerpo_sin_espacios() =>
        TipoTramiteDerivacion.Resolver(ModoDiagnostico.Simple, null, "  TRASPASO_UNILATERAL ").Should().Be("TRASPASO_UNILATERAL");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Tipo_en_Simple_sin_cuerpo_es_null(string? delCuerpo) =>
        TipoTramiteDerivacion.Resolver(ModoDiagnostico.Simple, null, delCuerpo).Should().BeNull();

    [Fact]
    public void Los_motivos_tienen_mensaje_claro_y_los_rechazos_de_Trámites_son_los_nueve_del_contrato()
    {
        MotivoDerivacion.RechazosTramites.Should().BeEquivalentTo(
        [
            "TIPO_NO_DISPONIBLE", "FAMILIA_BLOQUEADA", "USUARIO_SIN_PERMISO", "OT_NO_HABILITADO", "REGLA_COMPANIA",
            "TRAMITE_ACTIVO_DUPLICADO", "EMPRESA_INACTIVA", "REFERENCIA_DE_OTRO_ORIGEN", "DATOS_INVALIDOS",
        ]);
        foreach (var codigo in MotivoDerivacion.RechazosTramites.Concat(
                     [MotivoDerivacion.NoTerminado, MotivoDerivacion.NoViable, MotivoDerivacion.YaTieneTramite, MotivoDerivacion.EnCurso,
                      MotivoDerivacion.TramitesNoDisponible, MotivoDerivacion.RechazoTramites]))
        {
            codigo.Length.Should().BeLessThanOrEqualTo(40, "motivo_codigo es varchar(40)");
            MotivoDerivacion.Mensaje(codigo).Should().NotBeNullOrWhiteSpace().And.NotBe(codigo);
        }

        MotivoDerivacion.Mensaje(MotivoDerivacion.OtNoHabilitado).Should().Be("OT no habilitado para la compañía", "literal de la decisión 4.4");
    }
}
