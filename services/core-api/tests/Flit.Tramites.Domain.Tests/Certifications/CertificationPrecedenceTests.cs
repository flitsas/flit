using Flit.Tramites.Domain.Certifications;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Certifications;

/// <summary>
/// Precedencia dato a dato entre fuentes (HU #11302, Feature #11301, ADR-0041).
/// Es la regla que hoy no existe: el escritor de consultas sobrescribe sin mirar quién había puesto el
/// valor anterior, así que una corrección manual se pierde en silencio en la siguiente consulta.
/// </summary>
public sealed class CertificationPrecedenceTests
{
    private static readonly DateTimeOffset Ayer = new(2026, 8, 6, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset Hoy = new(2026, 8, 7, 10, 0, 0, TimeSpan.FromHours(-5));

    private static CertificationProvenance From(CertificationSourceKind kind, DateTimeOffset at) =>
        new(kind, kind.ToString().ToLowerInvariant(), at);

    [Fact]
    public void ValorAusente_NuncaDesplazaAUnoPresente()
    {
        // Es la regla que convierte la reconsulta en una operación segura: un proveedor que esta vez
        // no mandó el número de póliza no puede borrar el que ya se tenía.
        CertificationPrecedence.Wins(
                From(CertificationSourceKind.Consultation, Hoy),
                From(CertificationSourceKind.Ocr, Ayer),
                incomingHasValue: false,
                existingHasValue: true)
            .Should().BeFalse();
    }

    [Fact]
    public void SobreUnHuecoEscribeCualquiera()
    {
        CertificationPrecedence.Wins(
                From(CertificationSourceKind.System, Ayer),
                From(CertificationSourceKind.Consultation, Hoy),
                incomingHasValue: true,
                existingHasValue: false)
            .Should().BeTrue();
    }

    [Theory]
    // Consultation → User NO va aquí: es la excepción de D2, con su propia prueba abajo.
    [InlineData(CertificationSourceKind.Consultation, CertificationSourceKind.Ocr, true)]
    [InlineData(CertificationSourceKind.User, CertificationSourceKind.Ocr, true)]
    [InlineData(CertificationSourceKind.Ocr, CertificationSourceKind.System, true)]
    [InlineData(CertificationSourceKind.Ocr, CertificationSourceKind.Consultation, false)]
    [InlineData(CertificationSourceKind.System, CertificationSourceKind.User, false)]
    public void AIgualdadDeFecha_GanaElPeso(
        CertificationSourceKind incoming, CertificationSourceKind existing, bool gana)
    {
        CertificationPrecedence.Wins(From(incoming, Hoy), From(existing, Hoy)).Should().Be(gana);
    }

    [Fact]
    public void CorreccionManual_SobreviveALaReconsulta()
    {
        // D2 — excepción deliberada al peso. Si un operador arregló el dato a mano, sabía algo que el
        // proveedor no. Hoy esa corrección se pierde en silencio en la siguiente consulta.
        CertificationPrecedence.Wins(
                From(CertificationSourceKind.Consultation, Ayer),
                From(CertificationSourceKind.User, Hoy))
            .Should().BeFalse();
    }

    [Fact]
    public void CorreccionManualVieja_TambienSobreviveAUnaConsultaNueva()
    {
        // El plan enuncia D2 como "user con observed_at posterior prevalece". Comparar fechas dejaría
        // la excepción INERTE: una consulta nueva siempre llega con observed_at más reciente que la
        // corrección que pretende proteger, así que la corrección se perdería igual — que es el
        // defecto que D2 venía a cerrar. Se implementa la intención enunciada por el PO.
        CertificationPrecedence.Wins(
                From(CertificationSourceKind.Consultation, Hoy),
                From(CertificationSourceKind.User, Ayer))
            .Should().BeFalse();
    }

    [Fact]
    public void LaExcepcionEsSoloParaConsultaSobreCorreccion()
    {
        // No es "el usuario gana siempre": un OCR posterior no desplaza una corrección manual, y una
        // corrección manual sí desplaza un OCR. Solo se acota el par consulta → corrección.
        CertificationPrecedence.Wins(
                From(CertificationSourceKind.Ocr, Hoy),
                From(CertificationSourceKind.User, Ayer))
            .Should().BeFalse();

        CertificationPrecedence.Wins(
                From(CertificationSourceKind.User, Hoy),
                From(CertificationSourceKind.Ocr, Ayer))
            .Should().BeTrue();
    }

    [Fact]
    public void AIgualPeso_GanaLoMasReciente()
    {
        CertificationPrecedence.Wins(
                From(CertificationSourceKind.Consultation, Hoy),
                From(CertificationSourceKind.Consultation, Ayer))
            .Should().BeTrue();

        CertificationPrecedence.Wins(
                From(CertificationSourceKind.Consultation, Ayer),
                From(CertificationSourceKind.Consultation, Hoy))
            .Should().BeFalse();
    }

    [Fact]
    public void Merge_DevuelveElValorQueDebeQuedar()
    {
        var guardado = new CertifiedNumber("POL-VIEJA", "POL-VIEJA");
        var entrante = new CertifiedNumber("POL-NUEVA", "POL-NUEVA");

        CertificationPrecedence.Merge(
                entrante, From(CertificationSourceKind.Consultation, Hoy),
                guardado, From(CertificationSourceKind.Ocr, Ayer))
            .Should().Be(entrante);

        // Y el caso que importa: el entrante vacío no borra lo guardado.
        CertificationPrecedence.Merge(
                CertifiedNumber.Empty, From(CertificationSourceKind.Consultation, Hoy),
                guardado, From(CertificationSourceKind.Ocr, Ayer))
            .Should().Be(guardado);
    }
}
