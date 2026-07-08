using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #10601 (Feature #10585) — marcación de la prenda en el FUR: el checkbox requested_process_11
/// refleja el gravamen declarado (TienePrenda) en el mapper de campos.
/// </summary>
public sealed class FurPrendaMarkingTests
{
    private static FurDocumentData Data(bool tienePrenda) => new(
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
        ValorVenta: null, Causal: null, SellosFirma: [],
        TienePrenda: tienePrenda);

    [Fact]
    public void FUR_marca_el_gravamen_cuando_hay_prenda_vigente()
    {
        var campos = FurFieldMapper.Map(Data(tienePrenda: true));
        campos["requested_process_11"].Text.Should().Be("X");
    }

    [Fact]
    public void FUR_no_marca_el_gravamen_sin_prenda()
    {
        var campos = FurFieldMapper.Map(Data(tienePrenda: false));
        campos["requested_process_11"].Text.Should().Be("");
    }
}
