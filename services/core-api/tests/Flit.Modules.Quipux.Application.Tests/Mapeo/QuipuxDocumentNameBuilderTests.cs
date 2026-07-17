using Flit.Modules.Quipux.Domain.Mapeo;
using FluentAssertions;
using Xunit;

namespace Flit.Modules.Quipux.Application.Tests.Mapeo;

public sealed class QuipuxDocumentNameBuilderTests
{
    /// <summary>15/07/2026 14:30 — el formato tiene precisión de minuto, los segundos no viajan.</summary>
    private static readonly DateTimeOffset Momento =
        new(2026, 7, 15, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Build_TraspasoTipico_ProduceElFormatoExactoDeQuipux()
    {
        var nombre = QuipuxDocumentNameBuilder.Build(
            razonSocial: "Leasing Bancolombia S.A.",
            prefijo: "TR",
            momento: Momento,
            sufijo: "ABC123",
            maxLongitudEmpresa: 35);

        nombre.Should().Be("Leasing_Bancolombia_SA_TR_20260715_1430_ABC123");
    }

    [Fact]
    public void Build_MatriculaTipica_UsaVinComoSufijoYTope25()
    {
        var nombre = QuipuxDocumentNameBuilder.Build(
            razonSocial: "Leasing Bancolombia S.A.",
            prefijo: "MI",
            momento: Momento,
            sufijo: "9BWZZZ377VT004251",
            maxLongitudEmpresa: 25);

        nombre.Should().Be("Leasing_Bancolombia_SA_MI_20260715_1430_9BWZZZ377VT004251");
    }

    [Theory]
    [InlineData("TRU")] // traspaso unilateral
    [InlineData("MIL")] // matrícula en leasing
    public void Build_PrefijoDeVariante_ViajaTalCualAlNombre(string prefijo)
    {
        var nombre = QuipuxDocumentNameBuilder.Build(
            razonSocial: "Leasing Bancolombia S.A.",
            prefijo: prefijo,
            momento: Momento,
            sufijo: "ABC123",
            maxLongitudEmpresa: 35);

        nombre.Should().Be($"Leasing_Bancolombia_SA_{prefijo}_20260715_1430_ABC123");
    }

    [Fact]
    public void Build_MomentoConDesfase_SeFormateaTalCualSinConvertirAUtc()
    {
        // El desfase lo decide quien encola: el builder no reinterpreta el reloj.
        var enBogota = new DateTimeOffset(2026, 7, 15, 14, 30, 0, TimeSpan.FromHours(-5));

        var nombre = QuipuxDocumentNameBuilder.Build("Acme", "TR", enBogota, "ABC123", 35);

        nombre.Should().Be("Acme_TR_20260715_1430_ABC123");
    }

    [Fact]
    public void Build_MomentoConSegundos_LosIgnoraPorPrecisionDeMinuto()
    {
        var conSegundos = new DateTimeOffset(2026, 7, 15, 14, 30, 59, TimeSpan.Zero);

        QuipuxDocumentNameBuilder.Build("Acme", "TR", conSegundos, "ABC123", 35)
            .Should().Be("Acme_TR_20260715_1430_ABC123");
    }

    // ---------------------------------------------------------------------
    // Sanitización: quitar [^a-zA-Z0-9\s], luego cada racha de espacios → un "_"
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Acme S.A.", "Acme_SA")]                      // puntos
    [InlineData("Acme, Ltda.", "Acme_Ltda")]                  // comas
    [InlineData("Acme & Co", "Acme_Co")]                      // ampersand (queda 1 solo "_")
    [InlineData("Acme-Colombia", "AcmeColombia")]             // guion: DESAPARECE, no separa
    [InlineData("Acme (Colombia) S.A.S.", "Acme_Colombia_SAS")] // paréntesis
    [InlineData("Inversiones #1", "Inversiones_1")]           // numeral
    [InlineData("Acme 100% Ltda", "Acme_100_Ltda")]           // porcentaje
    public void SanitizarRazonSocial_CaracteresNoAlfanumericos_SeEliminan(string entrada, string esperado)
    {
        QuipuxDocumentNameBuilder.SanitizarRazonSocial(entrada, 35).Should().Be(esperado);
    }

    [Theory]
    [InlineData("Compañía Ñandú", "Compaa_and")]  // ñ, í y ú se BORRAN, no se transliteran
    [InlineData("Ñandú S.A.", "and_SA")]
    [InlineData("Créditos Andinos", "Crditos_Andinos")]
    [InlineData("Ámbito Ltda", "mbito_Ltda")]
    public void SanitizarRazonSocial_TildesYEnhe_SeEliminanNoSeTransliteran(string entrada, string esperado)
    {
        // Paridad con 1.0: el regex era [^a-zA-Z0-9\s], que no conoce Unicode.
        QuipuxDocumentNameBuilder.SanitizarRazonSocial(entrada, 35).Should().Be(esperado);
    }

    [Theory]
    [InlineData("Acme    Colombia", "Acme_Colombia")]   // espacios múltiples → UN "_"
    [InlineData("Acme \t Colombia", "Acme_Colombia")]   // tabs cuentan como espacio
    [InlineData("  Acme Colombia  ", "Acme_Colombia")]  // extremos recortados
    [InlineData("Acme . . Colombia", "Acme_Colombia")]  // basura entre espacios no multiplica "_"
    public void SanitizarRazonSocial_EspaciosMultiples_ColapsanAUnSoloGuionBajo(string entrada, string esperado)
    {
        QuipuxDocumentNameBuilder.SanitizarRazonSocial(entrada, 35).Should().Be(esperado);
    }

    [Fact]
    public void SanitizarRazonSocial_CaracterBorradoAlFinal_DejaGuionBajoColgando()
    {
        // Rareza heredada de 1.0: el borrado ocurre ANTES del colapso, así que "Leasing Ñ"
        // se convierte en "Leasing " y el espacio final termina siendo un "_".
        QuipuxDocumentNameBuilder.SanitizarRazonSocial("Leasing Ñ", 35).Should().Be("Leasing_");
    }

    // ---------------------------------------------------------------------
    // Truncado: es el ÚLTIMO paso, los "_" ya insertados cuentan contra el tope
    // ---------------------------------------------------------------------

    [Fact]
    public void Build_RazonSocialLarga_TruncaEmpresaA35EnTraspaso()
    {
        var nombre = QuipuxDocumentNameBuilder.Build(
            razonSocial: "Compañía Colombiana de Leasing y Servicios Financieros S.A.S.",
            prefijo: "TR",
            momento: Momento,
            sufijo: "ABC123",
            maxLongitudEmpresa: 35);

        // Sanitizado completo: Compaa_Colombiana_de_Leasing_y_Servicios_Financieros_SAS (56)
        // Cortado a 35 — parte una palabra por la mitad, tal como 1.0.
        nombre.Should().Be("Compaa_Colombiana_de_Leasing_y_Serv_TR_20260715_1430_ABC123");
        nombre.Split('_')[0].Should().Be("Compaa");
    }

    [Fact]
    public void Build_RazonSocialLarga_TruncaEmpresaA25EnMatricula()
    {
        var nombre = QuipuxDocumentNameBuilder.Build(
            razonSocial: "Compañía Colombiana de Leasing y Servicios Financieros S.A.S.",
            prefijo: "MI",
            momento: Momento,
            sufijo: "9BWZZZ377VT004251",
            maxLongitudEmpresa: 25);

        nombre.Should().Be("Compaa_Colombiana_de_Leas_MI_20260715_1430_9BWZZZ377VT004251");
    }

    [Fact]
    public void SanitizarRazonSocial_Trunca_DespuesDeInsertarLosGuionesBajos()
    {
        // "AB CD EF" → "AB_CD_EF" → cortar a 5 = "AB_CD".
        // Si el truncado ocurriera ANTES de meter los "_", el corte caería sobre "AB CD"
        // y el resultado sería otro. Este test fija el orden.
        QuipuxDocumentNameBuilder.SanitizarRazonSocial("AB CD EF", 5).Should().Be("AB_CD");
    }

    [Fact]
    public void SanitizarRazonSocial_TruncadoQuePartePalabra_PuedeDejarGuionBajoFinal()
    {
        // El corte es ciego: no respeta límites de palabra ni limpia el "_" del borde.
        QuipuxDocumentNameBuilder.SanitizarRazonSocial("AB CD EF", 3).Should().Be("AB_");
    }

    [Theory]
    [InlineData(35)]
    [InlineData(25)]
    public void SanitizarRazonSocial_RazonCorta_NoSeTrunca(int max)
    {
        QuipuxDocumentNameBuilder.SanitizarRazonSocial("Acme S.A.", max).Should().Be("Acme_SA");
    }

    [Fact]
    public void SanitizarRazonSocial_LongitudExacta_NoSeTrunca()
    {
        QuipuxDocumentNameBuilder.SanitizarRazonSocial("Acme SA", 7).Should().Be("Acme_SA");
    }

    // ---------------------------------------------------------------------
    // Fallos
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]      // solo puntuación: queda vacía tras sanitizar
    [InlineData("&&&")]
    [InlineData("ÑÑÑ")]      // solo no-ASCII: también queda vacía
    public void Build_RazonSocialVaciaTrasSanitizar_Lanza(string? entrada)
    {
        // En 1.0 el catch devolvía '' y el flujo caía al UUID del PDF como nombre:
        // un accidente, no un requisito. Un nombre sin empresa no es correlacionable.
        var act = () => QuipuxDocumentNameBuilder.Build(entrada, "TR", Momento, "ABC123", 35);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("razonSocial");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizarRazonSocial_RazonNulaOVacia_DevuelveVacio(string? entrada)
    {
        QuipuxDocumentNameBuilder.SanitizarRazonSocial(entrada, 35).Should().BeEmpty();
    }

    [Fact]
    public void Build_PrefijoNulo_Lanza()
    {
        var act = () => QuipuxDocumentNameBuilder.Build("Acme", null!, Momento, "ABC123", 35);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_SufijoNulo_Lanza()
    {
        var act = () => QuipuxDocumentNameBuilder.Build("Acme", "TR", Momento, null!, 35);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_MaxLongitudEmpresaNoPositiva_Lanza(int max)
    {
        var act = () => QuipuxDocumentNameBuilder.Build("Acme", "TR", Momento, "ABC123", max);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
