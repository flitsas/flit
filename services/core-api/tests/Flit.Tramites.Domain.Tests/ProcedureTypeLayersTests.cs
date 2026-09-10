using Flit.Tramites.Domain.Tramites.Services;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// ADR-0050 — de quién es cada capa del expediente (tipo base vs complemento del art. 5.1.8).
/// Fuente normativa de los códigos: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c> (tabla 1).
/// </summary>
public sealed class ProcedureTypeLayersTests
{
    [Theory]
    [InlineData("PRENDA_INSCRIPCION")]
    [InlineData("LEVANTAMIENTO_PRENDA")]
    [InlineData("LEVANTAR_INSCRIBIR_PRENDA")]
    // Su casilla del numeral 3 es la 18, no la 11/12, pero la pregunta aquí es de quién es la capa:
    // sustituir un acreedor exige capturar el gravamen, así que el paso de prenda le pertenece.
    [InlineData("CAMBIO_ACREEDOR")]
    [InlineData("  levantamiento_prenda  ")]
    public void EsTipoPrendaBase_ReconoceLosTiposCuyoTramiteEsElGravamen(string code)
    {
        ProcedureTypeLayers.EsTipoPrendaBase(code).Should().BeTrue();
    }

    [Theory]
    [InlineData("BLINDAJE")]
    [InlineData("DUPLICADO_TARJETA")]
    [InlineData("TRASPASO_STANDARD")]
    [InlineData("MATRICULA_NUEVA")]
    [InlineData(null)]
    [InlineData("")]
    public void EsTipoPrendaBase_NoConfundeUnGravamenAnadidoConElTramite(string? code)
    {
        ProcedureTypeLayers.EsTipoPrendaBase(code).Should().BeFalse();
    }

    [Theory]
    [InlineData("CAMBIO_COLOR", TransformacionBase.Color)]
    [InlineData("CAMBIO_CARROCERIA", TransformacionBase.Carroceria)]
    [InlineData("CONVERSION_COMBUSTIBLE", TransformacionBase.Combustible)]
    [InlineData("BLINDAJE", TransformacionBase.Blindaje)]
    [InlineData("cambio_color", TransformacionBase.Color)]
    [InlineData("DUPLICADO_PLACA", TransformacionBase.Ninguna)]
    [InlineData("TRASPASO_STANDARD", TransformacionBase.Ninguna)]
    [InlineData(null, TransformacionBase.Ninguna)]
    public void TransformacionDelTipo_ResuelveElAtributoQueElTipoCambia(string? code, TransformacionBase esperada)
    {
        ProcedureTypeLayers.TransformacionDelTipo(code).Should().Be(esperada);
    }

    [Theory]
    [InlineData("MATRICULAS")]
    [InlineData("TRASPASO")]
    [InlineData("traspaso")]
    public void FamiliaAcumulaComplementarios_MatriculaYTraspasoConservanElArticulo518(string familia)
    {
        ProcedureTypeLayers.FamiliaAcumulaComplementarios(familia).Should().BeTrue();
    }

    [Theory]
    [InlineData("OTROS")]
    [InlineData("  otros  ")]
    public void FamiliaAcumulaComplementarios_OtrosNoAcumula(string familia)
    {
        ProcedureTypeLayers.FamiliaAcumulaComplementarios(familia).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FAMILIA_QUE_NO_EXISTE")]
    public void FamiliaAcumulaComplementarios_SinClasificarNoApagaNada(string? familia)
    {
        // Degradar a «no acumula» apagaría en silencio la prenda y las transformaciones de un
        // expediente en curso cuyo tipo llegara sin familia. El default seguro aquí es el statu quo.
        ProcedureTypeLayers.FamiliaAcumulaComplementarios(familia).Should().BeTrue();
    }
}

/// <summary>
/// Precedencia perfil → familia de los dos flags de acumulación. La AUSENCIA de la llave no es
/// <c>false</c>: es «lo que diga la familia», que es lo que permite añadirlas por DDL sin reescribir
/// los perfiles ya grabados ni los snapshots congelados de un borrador en curso.
/// </summary>
public sealed class ProcedureTypeGateProfileComplementosTests
{
    [Fact]
    public void SinLaLlave_DecideLaFamilia()
    {
        var perfil = ProcedureTypeGateProfile.FromJson("""{"entryMode":"PLATE"}""");

        perfil.ComplementaryTransformationsAllowed("TRASPASO").Should().BeTrue();
        perfil.ComplementaryPrendaAllowed("MATRICULAS").Should().BeTrue();
        perfil.ComplementaryTransformationsAllowed("OTROS").Should().BeFalse();
        perfil.ComplementaryPrendaAllowed("OTROS").Should().BeFalse();
    }

    [Fact]
    public void ConLaLlave_MandaElPerfil()
    {
        var perfil = ProcedureTypeGateProfile.FromJson(
            """{"allowsComplementaryTransformations":false,"allowsComplementaryPrenda":false}""");

        // Un tipo puede declararse sin complementos aunque su familia acumule.
        perfil.ComplementaryTransformationsAllowed("TRASPASO").Should().BeFalse();
        perfil.ComplementaryPrendaAllowed("TRASPASO").Should().BeFalse();
    }

    [Fact]
    public void PerfilCorrupto_NoApagaLosComplementosDeUnaFamiliaQueAcumula()
    {
        // FromJson degrada al perfil por defecto; con las llaves nulas manda la familia.
        var perfil = ProcedureTypeGateProfile.FromJson("{ no es json");

        perfil.ComplementaryTransformationsAllowed("TRASPASO").Should().BeTrue();
        perfil.ComplementaryTransformationsAllowed("OTROS").Should().BeFalse();
    }

    // ── ExigeDecisionDePrenda ────────────────────────────────────────────────────────
    // «El RUNT reportó un gravamen» es un hecho del VEHÍCULO; resolverlo es responsabilidad del
    // TRÁMITE. Confundirlos dejaba un cambio de color sobre un carro con prenda exigiendo una
    // decisión que el asistente no pinta y que la API rechaza: bloqueo sin salida.

    private static ProcedureTypeGateProfile PerfilPorDefecto() => ProcedureTypeGateProfile.FromJson("{}");

    [Theory]
    [InlineData("CAMBIO_COLOR")]
    [InlineData("DUPLICADO_TARJETA")]
    [InlineData("CAMBIO_CARROCERIA")]
    [InlineData("BLINDAJE")]
    public void ExigeDecisionDePrenda_OtrosNoPrendario_NoExigeAunqueElRuntReporteGravamen(string tipo)
    {
        ProcedureTypeLayers
            .ExigeDecisionDePrenda(PerfilPorDefecto(), "OTROS", tipo, runtReportaGravamen: true)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("PRENDA_INSCRIPCION")]
    [InlineData("LEVANTAMIENTO_PRENDA")]
    [InlineData("CAMBIO_ACREEDOR")]
    [InlineData("LEVANTAR_INSCRIBIR_PRENDA")]
    public void ExigeDecisionDePrenda_OtrosPrendario_ExigeAunqueElRuntNoReporteNada(string tipo)
    {
        // La prenda ES el trámite: entra por el primer disparador, que no depende del RUNT (una
        // inscripción constituye un gravamen que todavía no existe).
        ProcedureTypeLayers
            .ExigeDecisionDePrenda(PerfilPorDefecto(), "OTROS", tipo, runtReportaGravamen: false)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("TRASPASO", "TRASPASO_STANDARD")]
    [InlineData("MATRICULAS", "MATRICULA_NUEVA")]
    public void ExigeDecisionDePrenda_FamiliasQueAcumulan_SiguenExigiendoConGravamen(
        string familia, string tipo)
    {
        // R10 intacto: donde el expediente acumula un gravamen sobre el tipo base, el hecho del
        // vehículo sí es asunto del trámite.
        ProcedureTypeLayers
            .ExigeDecisionDePrenda(PerfilPorDefecto(), familia, tipo, runtReportaGravamen: true)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("TRASPASO", "TRASPASO_STANDARD")]
    [InlineData("MATRICULAS", "MATRICULA_NUEVA")]
    public void ExigeDecisionDePrenda_SinGravamenNiTipoPrendario_NoExige(string familia, string tipo)
    {
        ProcedureTypeLayers
            .ExigeDecisionDePrenda(PerfilPorDefecto(), familia, tipo, runtReportaGravamen: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ExigeDecisionDePrenda_SinFamilia_ConservaElComportamientoPrevio()
    {
        // Fail-safe: un tipo sin clasificar se trata como acumulable, no se le apaga la prenda en
        // silencio (mismo criterio que FamiliaAcumulaComplementarios).
        ProcedureTypeLayers
            .ExigeDecisionDePrenda(PerfilPorDefecto(), null, "LO_QUE_SEA", runtReportaGravamen: true)
            .Should().BeTrue();
    }

    [Fact]
    public void ExigeDecisionDePrenda_PerfilQueDeclaraElGravamenComplementario_GanaALaFamilia()
    {
        // Precedencia perfil → familia: un tipo de OTROS que declare el gravamen complementario
        // vuelve a exigir la decisión, sin tocar código.
        var perfil = ProcedureTypeGateProfile.FromJson("""{"allowsComplementaryPrenda":true}""");

        ProcedureTypeLayers
            .ExigeDecisionDePrenda(perfil, "OTROS", "CAMBIO_COLOR", runtReportaGravamen: true)
            .Should().BeTrue();
    }
}
