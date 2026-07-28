using System.Globalization;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #10987 / #10988 (Feature #10972) — el recuadro OBSERVACIONES y las casillas de fecha del FUR
/// leían <c>fur_observations</c> y <c>fur_processing_date</c>, dos llaves que ningún productor
/// escribía. Aquí se cubre el lado del mapper: que el texto llegue al recuadro y que la fecha del
/// trámite mande sobre el fallback.
/// </summary>
public sealed class FurObservacionesYFechaTests
{
    private static FurDocumentData Data(string? observaciones = null, DateTime? fechaTramite = null) => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000001",
        Modalidad: "matricula_inicial",
        TipologiaCodigo: "matricula_inicial",
        Vehiculo: new VehiculoDatos(
            Marca: "TESLA", Linea: "MODELO Y", Modelo: "2026", Color: "BLANCO",
            Clase: "CAMIONETA", Combustible: "ELECTRICO", Cilindraje: "0",
            Vin: "LRWYGCFJ7TC495717", Placa: "YYY090"),
        Organismo: new OrganismoTransito("25286000", "OT", "CIUDAD"),
        Partes: [new DocumentParte("comprador", "DANIEL AMADO", "1193552679", null, DocumentType: "CC")],
        ValorVenta: null,
        Causal: null,
        SellosFirma: [],
        FechaTramite: fechaTramite,
        Observaciones: observaciones);

    private static string Campo(FurDocumentData data, string id) =>
        FurFieldMapper.Map(data)[id].Text ?? "";

    // ── HU #10987 — observaciones ────────────────────────────────────────────

    [Fact]
    public void Observaciones_DelGestor_LleganAlRecuadroDelFur()
    {
        Campo(Data(observaciones: "Vehículo con platón adaptado."), "observations")
            .Should().Be("Vehículo con platón adaptado.");
    }

    [Fact]
    public void Observaciones_ConGravamenYTransformacion_SeImprimenCompletas()
    {
        // El handler compone gravamen + manual + automático; el mapper debe pintarlo tal cual.
        const string compuesto =
            "GRAVAMEN / PRENDA A FAVOR DE: BANCO XYZ S.A. - NIT 890900608 "
            + "Vehículo con platón adaptado. Cambio de color: ROJO.";

        Campo(Data(observaciones: compuesto), "observations").Should().Be(compuesto);
    }

    [Fact]
    public void Observaciones_VaciasDejanElRecuadroEnBlanco()
    {
        Campo(Data(observaciones: null), "observations").Should().BeEmpty();
    }

    // ── HU #10988 — fecha del trámite ────────────────────────────────────────

    [Fact]
    public void FechaTramite_FijadaPorElGestor_MandaSobreElFallback()
    {
        var data = Data(fechaTramite: new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc));

        Campo(data, "processing_day").Should().Be("20");
        Campo(data, "processing_month").Should().Be("7");
        Campo(data, "processing_year").Should().Be("2026");
    }

    [Fact]
    public void FechaTramite_AusenteCaeAlDiaDeHoy()
    {
        // Comportamiento previo a la HU: se conserva como fallback, no como única opción.
        var hoy = DateTime.UtcNow;
        var data = Data(fechaTramite: null);

        Campo(data, "processing_day").Should().Be(hoy.Day.ToString(CultureInfo.InvariantCulture));
        Campo(data, "processing_month").Should().Be(hoy.Month.ToString(CultureInfo.InvariantCulture));
        Campo(data, "processing_year").Should().Be(hoy.Year.ToString(CultureInfo.InvariantCulture));
    }
}
