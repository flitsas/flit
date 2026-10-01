using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Integration;

/// <summary>HU #13142 (ADR-0066) — prelación única del firmante: Explícita → OT → compañía → asociado → default del OT → Ninguno.</summary>
public sealed class MandateSignerDefaultResolverTests
{
    private static readonly Guid IdOt = Guid.Parse("cccccccc-1111-4000-8000-000000000001");
    private static readonly Guid IdCompania = Guid.Parse("cccccccc-1111-4000-8000-000000000002");
    private static readonly Guid IdDefaultOt = Guid.Parse("cccccccc-1111-4000-8000-000000000099");
    private static readonly Guid IdOtro = Guid.Parse("cccccccc-1111-4000-8000-000000000003");
    private static readonly Guid IdAsociado2 = Guid.Parse("cccccccc-1111-4000-8000-000000000004");

    private static MandateSignerCandidate Cand(
        Guid id,
        string origen = MandateSignerOrigins.Organismo,
        string modelo = MandateSignerOrigins.ModeloNatural,
        string? metodo = MandateSignerOrigins.FormaBiometria,
        bool baul = false,
        bool firmaValida = true,
        string? motivo = null,
        bool fisica = false,
        bool eliminado = false,
        Guid? userId = null) =>
        new(id, "Mandatario " + id.ToString()[^2..], "100" + id.ToString()[^2..], userId,
            FirmaFisica: fisica, FirmaValida: firmaValida, MotivoSinFirma: motivo,
            Origen: origen, SignerModel: modelo, SignatureMethod: metodo, BaulVigente: baul, Eliminado: eliminado);

    // AC1 — el OT prevalece sobre la compañía.
    [Fact]
    public void Ac1_MandatarioDeOrigenOt_GanaAlDeOrigenCompania_ConNivelOtParaCompania()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdCompania, MandateSignerOrigins.Compania), Cand(IdOt, MandateSignerOrigins.Organismo)],
            null, null, null);

        r.Signer!.Id.Should().Be(IdOt);
        r.Level.Should().Be(MandateSignerLevel.OtParaCompania);
    }

    [Fact]
    public void Ac1_SuperAdmin_SeEquiparaAlOt()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdCompania, MandateSignerOrigins.Compania), Cand(IdOt, MandateSignerOrigins.SuperAdmin)],
            null, null, null);

        r.Signer!.Id.Should().Be(IdOt);
        r.Level.Should().Be(MandateSignerLevel.OtParaCompania);
    }

    // AC2 — cascada.
    [Fact]
    public void Ac2_SinMandatarioDelOt_SeDevuelveElDeLaCompania()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdCompania, MandateSignerOrigins.Compania)], Cand(IdDefaultOt), null, null);

        r.Signer!.Id.Should().Be(IdCompania);
        r.Level.Should().Be(MandateSignerLevel.PropioDeCompania);
    }

    [Fact]
    public void Ac2_SinNivelesSuperiores_SeDevuelveElDefaultDelOt()
    {
        var r = MandateSignerDefaultResolver.Resolve([], Cand(IdDefaultOt), null, null);

        r.Signer!.Id.Should().Be(IdDefaultOt);
        r.Level.Should().Be(MandateSignerLevel.DefaultDelOt);
    }

    [Fact]
    public void Ac2_SinNadie_NivelNingunoYSinFirmante()
    {
        var r = MandateSignerDefaultResolver.Resolve([], null, null, null);

        r.Signer.Should().BeNull();
        r.Level.Should().Be(MandateSignerLevel.Ninguno);
        r.Validos.Should().BeEmpty();
        r.EleccionInvalida.Should().BeFalse();
    }

    [Fact]
    public void Ac2_LosNivelesSeEvaluanEnElOrdenDelEnum()
    {
        Enum.GetValues<MandateSignerLevel>().Should().Equal(
            MandateSignerLevel.Explicita, MandateSignerLevel.OtParaCompania, MandateSignerLevel.PropioDeCompania,
            MandateSignerLevel.AsociadoDeOtraCompania, MandateSignerLevel.DefaultDelOt, MandateSignerLevel.Ninguno);
    }

    // AC3 — elección explícita.
    [Fact]
    public void Ac3_EleccionExplicitaValida_Gana_YNoSeAplicaLaPrelacion()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo), Cand(IdCompania, MandateSignerOrigins.Compania)],
            Cand(IdDefaultOt), eleccionOt: IdCompania, guardado: null);

        r.Signer!.Id.Should().Be(IdCompania);
        r.Level.Should().Be(MandateSignerLevel.Explicita);
    }

    [Fact]
    public void Ac3_EleccionDeOtQueNoEstaEntreLosValidos_ExigeElegirOtraVez()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo)], null, eleccionOt: IdOtro, guardado: null);

        r.EleccionInvalida.Should().BeTrue();
        r.Signer.Should().BeNull();
    }

    [Fact]
    public void Ac3_EleccionDeOtSobreUnMandatarioDescartado_ExigeElegirOtraVez()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, firmaValida: false, motivo: "sin_validacion_aprobada")], null, eleccionOt: IdOt, guardado: null);

        r.EleccionInvalida.Should().BeTrue();
    }

    [Fact]
    public void Ac3_GuardadoValido_SeRespeta_ComoExplicita()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo), Cand(IdCompania, MandateSignerOrigins.Compania)],
            null, eleccionOt: null, guardado: IdCompania);

        r.Signer!.Id.Should().Be(IdCompania);
        r.Level.Should().Be(MandateSignerLevel.Explicita);
    }

    [Fact]
    public void Ac3_GuardadoInvalido_SeIgnora_YSeResuelvePorPrelacion()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo)], null, eleccionOt: null, guardado: IdOtro);

        r.Signer!.Id.Should().Be(IdOt);
        r.Level.Should().Be(MandateSignerLevel.OtParaCompania);
        r.EleccionInvalida.Should().BeFalse();
    }

    [Fact]
    public void Ac3_EleccionVaciaSeTrataComoAusente()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo)], null, Guid.Empty, Guid.Empty);

        r.Level.Should().Be(MandateSignerLevel.OtParaCompania);
    }

    // AC4 — se ignoran los no aplicables.
    [Theory]
    [InlineData("mandatario_fuera_de_vigencia")]
    [InlineData("mandatario_inactivo")]
    [InlineData("sin_validacion_aprobada")]
    public void Ac4_MandatarioDelOtSinFirmaValida_SeDescartaYContinuaConElSiguienteNivel(string motivo)
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo, firmaValida: false, motivo: motivo),
             Cand(IdCompania, MandateSignerOrigins.Compania)],
            null, null, null);

        r.Signer!.Id.Should().Be(IdCompania);
        r.Level.Should().Be(MandateSignerLevel.PropioDeCompania);
        r.Descartados.Should().ContainSingle(d => d.SignerId == IdOt)
            .Which.Should().Be(new MandateSignerDiscard(IdOt, MandateSignerLevel.OtParaCompania, motivo));
    }

    [Fact]
    public void Ac4_MandatarioEliminado_SeDescarta_ConMotivoMandatarioEliminado()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo, eliminado: true)], Cand(IdDefaultOt), null, null);

        r.Level.Should().Be(MandateSignerLevel.DefaultDelOt);
        r.Descartados.Should().ContainSingle(d => d.SignerId == IdOt && d.Motivo == "mandatario_eliminado");
    }

    [Fact]
    public void Ac4_DefaultDelOtNoVigente_SeDescarta_YQuedaNingunoConElMotivo()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [], Cand(IdDefaultOt, firmaValida: false, motivo: "mandatario_fuera_de_vigencia"), null, null);

        r.Level.Should().Be(MandateSignerLevel.Ninguno);
        r.Signer.Should().BeNull();
        r.Descartados.Should().ContainSingle(d =>
            d.SignerId == IdDefaultOt && d.Level == MandateSignerLevel.DefaultDelOt
            && d.Motivo == "mandatario_fuera_de_vigencia");
    }

    [Fact]
    public void Ac4_DefaultDelOtEliminado_SeDescarta()
    {
        var r = MandateSignerDefaultResolver.Resolve([], Cand(IdDefaultOt, eliminado: true), null, null);

        r.Level.Should().Be(MandateSignerLevel.Ninguno);
        r.Descartados.Should().ContainSingle(d => d.Motivo == "mandatario_eliminado");
    }

    // Firma válida por modelo (ADR-0066).
    [Fact]
    public void Baul_SinFirmaVigenteEnElBaul_SeDescarta_ConMotivoBaulSinFirmaVigente()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, metodo: MandateSignerOrigins.FormaBaul, baul: false)], null, null, null);

        r.Level.Should().Be(MandateSignerLevel.Ninguno);
        r.Descartados.Should().ContainSingle(d => d.Motivo == "baul_sin_firma_vigente");
    }

    [Fact]
    public void Baul_ConFirmaVigenteEnElBaul_Cuenta()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, metodo: MandateSignerOrigins.FormaBaul, baul: true)], null, null, null);

        r.Signer!.Id.Should().Be(IdOt);
    }

    [Fact]
    public void Biometria_ConFirmaValida_Cuenta_SinExigirBaul()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, metodo: MandateSignerOrigins.FormaBiometria, baul: false)], null, null, null);

        r.Signer!.Id.Should().Be(IdOt);
    }

    [Theory]
    [InlineData(MandateSignerOrigins.ModeloJuridica)]
    [InlineData(MandateSignerOrigins.ModeloFormatoBlanco)]
    public void JuridicaYFormatoEnBlanco_NoExigenFirmaPersonal_Cuentan(string modelo)
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, modelo: modelo, metodo: null, firmaValida: true)], null, null, null);

        r.Signer!.Id.Should().Be(IdOt);
    }

    [Fact]
    public void FormatoEnBlanco_EnNivelOt_TapaALosNivelesInferiores()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo, MandateSignerOrigins.ModeloFormatoBlanco, null),
             Cand(IdCompania, MandateSignerOrigins.Compania)],
            null, null, null);

        r.Signer!.Id.Should().Be(IdOt);
    }

    [Fact]
    public void FirmaFisica_NoHaceValidaLaFirma_SeDescartaConMotivoFirmaFisicaSinMigrar()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, firmaValida: false, motivo: "sin_validacion_aprobada", fisica: true)], null, null, null);

        r.Level.Should().Be(MandateSignerLevel.Ninguno);
        r.Descartados.Should().ContainSingle(d => d.Motivo == "firma_fisica_sin_migrar");
        // Se sigue honrando en el PDF / aprobación (P4).
        r.FirmaFisicaPendiente!.Id.Should().Be(IdOt);
    }

    [Fact]
    public void FirmaFisica_ConFirmaValida_CuentaNormalmente()
    {
        var r = MandateSignerDefaultResolver.Resolve([Cand(IdOt, fisica: true)], null, null, null);

        r.Signer!.Id.Should().Be(IdOt);
        r.FirmaFisicaPendiente.Should().BeNull();
    }

    [Fact]
    public void FirmaFisica_ConBaulSinVigencia_TambienSeDescartaPorFirmaFisicaSinMigrar()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, metodo: MandateSignerOrigins.FormaBaul, baul: false, fisica: true)], null, null, null);

        r.Descartados.Should().ContainSingle(d => d.Motivo == "firma_fisica_sin_migrar");
    }

    // AC5 — el default del OT entra sin vínculo con la compañía.
    [Fact]
    public void Ac5_DefaultDelOtSinVinculo_SeResuelveYQuedaEnElConjuntoValido()
    {
        var r = MandateSignerDefaultResolver.Resolve([], Cand(IdDefaultOt), null, null);

        r.Signer!.Id.Should().Be(IdDefaultOt);
        r.Validos.Select(v => v.Id).Should().Contain(IdDefaultOt);
    }

    [Fact]
    public void Ac5_ElOtPuedeElegirElDefaultDelOtAunqueNoEsteVinculado()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt)], Cand(IdDefaultOt), eleccionOt: IdDefaultOt, guardado: null);

        r.Signer!.Id.Should().Be(IdDefaultOt);
        r.Level.Should().Be(MandateSignerLevel.Explicita);
    }

    [Fact]
    public void Validos_NoRepiteAlMandatarioQueEntraPorDosOrigenes()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo), Cand(IdOt, MandateSignerOrigins.Compania)],
            Cand(IdOt), null, null);

        r.Validos.Should().ContainSingle(v => v.Id == IdOt);
        r.Signer!.Id.Should().Be(IdOt);
    }

    // Ambigüedad y desempate.
    [Fact]
    public void Nivel_ConVariosValidosSinDesignado_EsAmbiguo_YNoEligeAlAzar()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo), Cand(IdOtro, MandateSignerOrigins.Organismo)],
            Cand(IdDefaultOt), null, null);

        r.Signer.Should().BeNull();
        r.Ambiguo.Should().BeTrue();
        r.Level.Should().Be(MandateSignerLevel.OtParaCompania);
        r.Desempate!.Select(c => c.Id).Should().BeEquivalentTo([IdOt, IdOtro]);
    }

    [Fact]
    public void Nivel_ConVariosValidos_ElDesignadoDeLaReglaDesempata()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo), Cand(IdOtro, MandateSignerOrigins.Organismo)],
            null, null, null, designadoRegla: IdOtro);

        r.Signer!.Id.Should().Be(IdOtro);
        r.Level.Should().Be(MandateSignerLevel.OtParaCompania);
    }

    [Fact]
    public void Nivel_ElDesignadoDeLaReglaFueraDelGrupo_NoDesempata()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo), Cand(IdOtro, MandateSignerOrigins.Organismo)],
            null, null, null, designadoRegla: IdCompania);

        r.Ambiguo.Should().BeTrue();
    }

    [Fact]
    public void DesignadoDeLaRegla_YaNoEsUnNivel_ElOtGanaSiEsUnicoAunqueElDesignadoSeaOtro()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOt, MandateSignerOrigins.Organismo), Cand(IdCompania, MandateSignerOrigins.Compania)],
            null, null, null, designadoRegla: IdCompania);

        r.Signer!.Id.Should().Be(IdOt);
    }

    // Nivel 3 — punto de extensión (F7).
    [Fact]
    public void Asociado_Unico_SeResuelveEntreCompaniaYDefaultDelOt()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOtro, MandateSignerOrigins.Asociado)], Cand(IdDefaultOt), null, null);

        r.Signer!.Id.Should().Be(IdOtro);
        r.Level.Should().Be(MandateSignerLevel.AsociadoDeOtraCompania);
    }

    [Fact]
    public void Asociado_DosValidos_ContinuanAlDefaultDelOt_YAmbosQuedanComoCandidatos()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOtro, MandateSignerOrigins.Asociado), Cand(IdAsociado2, MandateSignerOrigins.Asociado)],
            Cand(IdDefaultOt), null, null);

        r.Signer!.Id.Should().Be(IdDefaultOt);
        r.Level.Should().Be(MandateSignerLevel.DefaultDelOt);
        r.Validos.Select(v => v.Id).Should().Contain([IdOtro, IdAsociado2, IdDefaultOt]);
    }

    [Fact]
    public void Asociado_DosValidos_SinDefaultDelOt_QuedaAmbiguoParaQueElOtElija()
    {
        var r = MandateSignerDefaultResolver.Resolve(
            [Cand(IdOtro, MandateSignerOrigins.Asociado), Cand(IdAsociado2, MandateSignerOrigins.Asociado)],
            null, null, null);

        r.Signer.Should().BeNull();
        r.Ambiguo.Should().BeTrue();
        r.Level.Should().Be(MandateSignerLevel.AsociadoDeOtraCompania);
    }

    [Fact]
    public void EnF4_SinAsociados_ElNivelTresSeSalta()
    {
        var r = MandateSignerDefaultResolver.Resolve([], Cand(IdDefaultOt), null, null);

        r.Level.Should().Be(MandateSignerLevel.DefaultDelOt);
    }
}
