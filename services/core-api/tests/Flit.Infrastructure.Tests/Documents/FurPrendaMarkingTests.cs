using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #10601 (Feature #10585), ampliado por HU #11257 (Feature #11254) — marcación de la prenda en el
/// FUR: <c>requested_process_11</c> (constitución) y <c>requested_process_12</c> (levantamiento) según
/// <see cref="FurDocumentData.PrendaMarking"/>, resuelto ya en el dominio
/// (<see cref="PrendaDecision.ToFurMarking"/>). Matriz decisión × formato: el mapper es agnóstico del
/// formato (produce los mismos tokens en los tres), pero se ejercita explícitamente contra
/// <see cref="FurTemplateFormat"/> para no depender de eso por accidente, y cada caso afirma que la
/// casilla CONTRARIA queda vacía (no solo que la esperada se marque).
/// </summary>
public sealed class FurPrendaMarkingTests
{
    private static FurDocumentData Data(FurPrendaMarking marking, FurTemplateFormat format) => new(
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
        PrendaMarking: marking,
        TemplateFormat: format);

    public static IEnumerable<object[]> DecisionPorFormato()
    {
        foreach (var format in new[] { FurTemplateFormat.Automotor, FurTemplateFormat.Maquinaria, FurTemplateFormat.Remolques })
        foreach (var marking in new[] { FurPrendaMarking.Constitucion, FurPrendaMarking.Levantamiento, FurPrendaMarking.Ninguna })
            yield return [marking, format];
    }

    [Theory]
    [MemberData(nameof(DecisionPorFormato))]
    public void Map_MarcaLaCasillaCorrectaYDejaLaContrariaVacia(FurPrendaMarking marking, FurTemplateFormat format)
    {
        var campos = FurFieldMapper.Map(Data(marking, format));

        var esperado11 = marking == FurPrendaMarking.Constitucion ? "X" : "";
        var esperado12 = marking == FurPrendaMarking.Levantamiento ? "X" : "";

        campos["requested_process_11"].Text.Should().Be(esperado11,
            "marking={0} en {1}", marking, format);
        campos["requested_process_12"].Text.Should().Be(esperado12,
            "marking={0} en {1}", marking, format);
    }

    [Fact]
    public void Constitucion_MarcaSolo11_LaCasilla12QuedaVacia()
    {
        var campos = FurFieldMapper.Map(Data(FurPrendaMarking.Constitucion, FurTemplateFormat.Automotor));

        campos["requested_process_11"].Text.Should().Be("X");
        campos["requested_process_12"].Text.Should().Be("", "la constitución no debe marcar el levantamiento");
    }

    [Fact]
    public void Levantamiento_Marca12_LaCasilla11QuedaVacia()
    {
        var campos = FurFieldMapper.Map(Data(FurPrendaMarking.Levantamiento, FurTemplateFormat.Automotor));

        campos["requested_process_12"].Text.Should().Be("X");
        campos["requested_process_11"].Text.Should().Be("", "el levantamiento no debe marcar la constitución");
    }

    [Fact]
    public void Ninguna_NoMarcaNiConstitucionNiLevantamiento()
    {
        var campos = FurFieldMapper.Map(Data(FurPrendaMarking.Ninguna, FurTemplateFormat.Automotor));

        campos["requested_process_11"].Text.Should().Be("");
        campos["requested_process_12"].Text.Should().Be("");
    }

    [Fact]
    public void MatriculaConPrenda_Marca1Mas11_NoUnaMezclaDe2Mas11()
    {
        // Verificación explícita de D1: requested_process_1/_2 (tipo de trámite) es independiente de
        // requested_process_11/_12 (modalidad de prenda). Una MATRÍCULA (no traspaso) con prenda debe
        // salir 1 + 11, nunca 2 + 11.
        var data = Data(FurPrendaMarking.Constitucion, FurTemplateFormat.Automotor);
        var campos = FurFieldMapper.Map(data);

        campos["requested_process_1"].Text.Should().Be("X", "es matrícula, no traspaso");
        campos["requested_process_2"].Text.Should().Be("");
        campos["requested_process_11"].Text.Should().Be("X");
    }
}
