using Flit.Tramites.Domain.Certifications;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.Certifications;

/// <summary>
/// Selección de la certificación vigente dentro del histórico (HU #11302, D9: al PDF va solo la
/// vigente). Se elige por <b>fecha</b> y no por el texto del estado, que es lo que evita certificar
/// una cobertura inexistente.
/// </summary>
public sealed class CertificationSelectionTests
{
    private static readonly DateOnly Hoy = new(2026, 8, 7);

    private static SoatCertification Poliza(string numero, DateOnly desde, DateOnly hasta, string? estado = null) =>
        new(new CertifiedNumber(numero, numero),
            new CertifiedName("SEGUROS DEL ESTADO S.A.", null),
            new CertifiedDate(desde.AddDays(-1), null),
            new CertifiedDate(desde, null),
            new CertifiedDate(hasta, null),
            estado is null ? CertifiedStatus.Empty : new CertifiedStatus(VigencyStatus.Unknown, estado));

    private static RtmCertification Revision(string numero, DateOnly desde, DateOnly hasta, string? estado = null) =>
        new(new CertifiedNumber(numero, numero),
            new CertifiedName("CDA LA 33", null),
            new CertifiedDate(desde.AddDays(-1), null),
            new CertifiedDate(desde, null),
            new CertifiedDate(hasta, null),
            estado is null ? CertifiedStatus.Empty : new CertifiedStatus(VigencyStatus.Unknown, estado));

    [Fact]
    public void Soat_EligeLaQueCubreHoy_AunqueNoSeaLaUltimaDeLaLista()
    {
        var historico = new[]
        {
            Poliza("VIEJA", new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1)),
            Poliza("VIGENTE", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)),
            Poliza("OTRA-VIEJA", new DateOnly(2025, 1, 2), new DateOnly(2026, 1, 2)),
        };

        SoatSelection.PickCurrent(historico, Hoy)!.PolicyNumber.Value.Should().Be("VIGENTE");
    }

    [Fact]
    public void Soat_SinCobertura_MuestraLaDeVencimientoMasReciente()
    {
        // Mejor "vencido el 2026/01/02" que una tabla muda: el OT necesita ver que hubo póliza.
        var historico = new[]
        {
            Poliza("VIEJA", new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1)),
            Poliza("MENOS-VIEJA", new DateOnly(2025, 1, 2), new DateOnly(2026, 1, 2)),
        };

        SoatSelection.PickCurrent(historico, Hoy)!.PolicyNumber.Value.Should().Be("MENOS-VIEJA");
    }

    [Fact]
    public void Soat_HistoricoVacioOSinDatos_NoDevuelveNada()
    {
        SoatSelection.PickCurrent([], Hoy).Should().BeNull();
        SoatSelection.PickCurrent([SoatCertification.Empty], Hoy).Should().BeNull();
    }

    [Fact]
    public void Soat_SinEstadoDeclarado_LaVigenciaSeDerivaDeLaFecha()
    {
        var vigente = Poliza("A", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        var vencida = Poliza("B", new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1));

        SoatSelection.DeriveStatus(vigente, Hoy).Should().Be(VigencyStatus.Vigente);
        SoatSelection.DeriveStatus(vencida, Hoy).Should().Be(VigencyStatus.Vencido);
    }

    [Fact]
    public void Soat_SinFechaDeVencimiento_NoSeInventaVigencia()
    {
        var sinFecha = SoatCertification.Empty with { PolicyNumber = new CertifiedNumber("X", "X") };

        SoatSelection.DeriveStatus(sinFecha, Hoy).Should().Be(VigencyStatus.Unknown);
    }

    [Fact]
    public void Rtm_CuatroRevisionesAprobadasYNingunaVigente_NoCertificaVigencia()
    {
        // Caso real de la placa YNK04A. Elegir "la última que diga APROBADA" es el defecto que se
        // evita seleccionando por fecha.
        var historico = new[]
        {
            Revision("R1", new DateOnly(2021, 1, 1), new DateOnly(2022, 1, 1), "APROBADA"),
            Revision("R2", new DateOnly(2022, 1, 1), new DateOnly(2023, 1, 1), "APROBADA"),
            Revision("R3", new DateOnly(2023, 1, 1), new DateOnly(2024, 1, 1), "APROBADA"),
            Revision("R4", new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1), "APROBADA"),
        };

        var elegida = RtmSelection.PickCurrent(historico, Hoy)!;

        elegida.CertificateNumber.Value.Should().Be("R4", "la más reciente, para que el documento la muestre");
        RtmSelection.DeriveStatus(elegida, Hoy).Should().Be(VigencyStatus.Vencido, "ninguna cubre hoy");
    }

    [Fact]
    public void Rtm_VehiculoConMenosDeCincoAnios_NoDebeRevisionTodavia()
    {
        var reciente = new VehicleRegistrationFacts(new CertifiedDate(new DateOnly(2026, 1, 15), null));

        RtmSelection.Applies(reciente, Hoy).Should().BeFalse();
    }

    [Fact]
    public void Rtm_VehiculoConMasDeCincoAnios_SiLaDebe()
    {
        var antiguo = new VehicleRegistrationFacts(new CertifiedDate(new DateOnly(2020, 1, 15), null));

        RtmSelection.Applies(antiguo, Hoy).Should().BeTrue();
    }

    [Fact]
    public void Rtm_EnElQuintoAniversarioTodaviaNoAplica()
    {
        // La regla es "MÁS de cinco años": en el aniversario los cumple, no los supera. Se delega en
        // RtmCertificado para no duplicar el umbral — duplicarlo es la forma más silenciosa de que el
        // certificado y el resto del sistema empiecen a discrepar.
        var aniversario = new VehicleRegistrationFacts(new CertifiedDate(Hoy.AddYears(-5), null));

        RtmSelection.Applies(aniversario, Hoy).Should().BeFalse();
    }

    [Fact]
    public void Rtm_SinFechaDeMatricula_SeAsumeExigible()
    {
        // Lado seguro: es preferible pedir la revisión de más que dar por eximido un vehículo viejo.
        RtmSelection.Applies(VehicleRegistrationFacts.Empty, Hoy).Should().BeTrue();
    }
}
