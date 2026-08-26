using Flit.Tramites.Domain.Certifications;
using Flit.Tramites.Domain.Certifications.Normalization;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Certifications;

/// <summary>
/// Normalización canónica de certificaciones externas (HU #11302, Feature #11301, ADR-0041).
/// Cubre las tres invariantes que hicieron fracasar el intento anterior: la fecha no se corre de día,
/// el estado no se inventa, y lo que no se supo leer conserva el crudo en vez de desaparecer.
/// </summary>
public sealed class CertificationNormalizationTests
{
    // ── Fechas ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-05-14T00:00:00.000-05:00", 2026, 5, 14)]  // formato real del RUNT vía Kyverum
    [InlineData("2026-05-14", 2026, 5, 14)]
    [InlineData("14/05/2026", 2026, 5, 14)]                     // dd/MM: en Colombia 05/06 es 5 de junio
    [InlineData("14-05-2026", 2026, 5, 14)]
    [InlineData("2026/05/14", 2026, 5, 14)]
    public void Parse_LeeLosFormatosDeLosProveedores(string raw, int year, int month, int day)
    {
        var result = ColombianCertificateDate.Parse(raw);

        result.Value.Should().Be(new DateOnly(year, month, day));
        result.Raw.Should().Be(raw);
    }

    [Fact]
    public void Parse_HoraTardiaEnColombia_NoCorreElDiaImpreso()
    {
        // El defecto que motiva esta clase: normalizar a UTC convierte las 20:00 del 14 en el día 15,
        // y el certificado afirmaría un vencimiento un día después del real.
        ColombianCertificateDate.Parse("2026-05-14T20:00:00-05:00").Value
            .Should().Be(new DateOnly(2026, 5, 14));
    }

    [Fact]
    public void Parse_InstanteUtc_SeConvierteAlDiaCivilColombiano()
    {
        // 2026-05-15T02:00Z son las 21:00 del 14 en Colombia.
        ColombianCertificateDate.Parse("2026-05-15T02:00:00Z").Value
            .Should().Be(new DateOnly(2026, 5, 14));
    }

    [Theory]
    [InlineData("no es una fecha")]
    [InlineData("0000-00-00")]
    [InlineData("31/02/2026")]
    public void Parse_LoIlegible_ConservaElCrudoYNoInventaFecha(string raw)
    {
        var result = ColombianCertificateDate.Parse(raw);

        result.Value.Should().BeNull();
        result.Raw.Should().Be(raw);
        result.Unresolved.Should().BeTrue("alimenta normalization_issues para corregir el mapper sin reconsultar");
    }

    [Fact]
    public void Parse_Vacio_NoEsUnaIncidencia()
    {
        // Ausente ≠ ilegible: que el proveedor no mande el campo no es un defecto del mapper.
        ColombianCertificateDate.Parse(null).Unresolved.Should().BeFalse();
        ColombianCertificateDate.Parse("   ").Unresolved.Should().BeFalse();
    }

    // ── Estados ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("VIGENTE", VigencyStatus.Vigente)]
    [InlineData("vigente", VigencyStatus.Vigente)]
    [InlineData("SI", VigencyStatus.Vigente)]
    [InlineData("VENCIDO", VigencyStatus.Vencido)]
    [InlineData("NO VIGENTE", VigencyStatus.Vencido)]
    [InlineData("NO_VIGENTE", VigencyStatus.Vencido)]
    [InlineData("NO", VigencyStatus.Vencido)]
    [InlineData("NO APLICA", VigencyStatus.NoAplica)]
    public void ForVehicle_TraduceElVocabularioObservado(string raw, VigencyStatus expected)
    {
        VigencyStatusNormalizer.ForVehicle(raw).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("APROBADA")]
    [InlineData("aprobada")]
    [InlineData("APROBADO")]
    public void ForVehicle_Aprobada_NoEsVigencia(string raw)
    {
        // Placa YNK04A: cuatro revisiones APROBADA, las cuatro con vigente:"NO". APROBADA describe el
        // resultado del trámite de la revisión; tratarlo como vigencia certifica una cobertura que el
        // RUNT nunca declaró.
        var result = VigencyStatusNormalizer.ForVehicle(raw);

        result.Value.Should().Be(VigencyStatus.Unknown);
        result.Raw.Should().Be(raw, "el certificado imprime lo que dijo la fuente");
    }

    [Theory]
    [InlineData("ANULADA")]
    [InlineData("CANCELADA")]
    [InlineData("cualquier cosa")]
    public void ForVehicle_LoNoReconocido_NoCaeEnVencidoPorDescarte(string raw)
    {
        VigencyStatusNormalizer.ForVehicle(raw).Value.Should().Be(VigencyStatus.Unknown);
    }

    [Theory]
    [InlineData("ACTIVA", VigencyStatus.Vigente)]
    [InlineData("CANCELADA", VigencyStatus.Vencido)]
    [InlineData("LIQUIDADA", VigencyStatus.Vencido)]
    public void ForMerchantRegistration_DerivaElCanonicoYConservaElCrudo(string raw, VigencyStatus expected)
    {
        var result = VigencyStatusNormalizer.ForMerchantRegistration(raw);

        result.Value.Should().Be(expected);
        result.Raw.Should().Be(raw, "D5: crudo + canónico derivado");
    }

    [Fact]
    public void ForMerchantRegistration_EstadoNoVisto_NoRompeNada()
    {
        var result = VigencyStatusNormalizer.ForMerchantRegistration("EN REORGANIZACION");

        result.Value.Should().Be(VigencyStatus.Unknown);
        result.ToDocumentText().Should().Be("EN REORGANIZACION");
    }

    // ── Números y nombres ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Number_PolizaDe16Digitos_SeConservaComoTexto()
    {
        // numSoat del RUNT supera int. Convertir a numérico pierde el número y los ceros de cabecera.
        CertificateNumberNormalizer.Normalize("1234567890123456").Value.Should().Be("1234567890123456");
        CertificateNumberNormalizer.Normalize("0012345").Value.Should().Be("0012345");
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("NO APLICA")]
    [InlineData("DESCONOCIDO")]
    [InlineData("-")]
    [InlineData("0")]
    [InlineData("000000")]
    public void Number_LosRellenosNoSonUnNumero(string raw)
    {
        // Regla HU #10856: ausente ⇒ celda en blanco, sin guion ni "N/A". Imprimir "0" como número de
        // póliza es peor que dejar la celda vacía: parece un dato.
        CertificateNumberNormalizer.Normalize(raw).Value.Should().BeNull();
    }

    [Fact]
    public void Name_LimpiaRuidoDeTransporteSinTocarLaCapitalizacion()
    {
        var result = EntityNameNormalizer.Normalize("  \"SEGUROS   DEL\n ESTADO S.A.\"  ");

        result.Value.Should().Be("SEGUROS DEL ESTADO S.A.");
    }
}
