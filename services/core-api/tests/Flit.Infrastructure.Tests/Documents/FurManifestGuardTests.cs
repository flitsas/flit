using System.Globalization;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #10460 — guardia anti-regresión del manifest del FUR. Valida integridad estructural
/// (ids únicos, campos dentro de la página, todos los tokens del mapper con placement) y compara
/// la geometría de cada campo contra una línea base congelada: un descuadre (AC2) o un campo
/// nuevo/eliminado (AC3) hace fallar la guardia identificando el/los campo(s).
/// </summary>
public sealed class FurManifestGuardTests
{
    private readonly ITestOutputHelper _output;

    public FurManifestGuardTests(ITestOutputHelper output) => _output = output;

    private static FurFieldManifest Manifest() => FurFieldManifestLoader.LoadEmbedded();

    private static string N(double d) => d.ToString(CultureInfo.InvariantCulture);

    /// <summary>Huella canónica de un campo: id + tipo + geometría. Cualquier cambio la altera.</summary>
    private static string Canon(FurFieldDefinition f) =>
        f.Type == FurFieldType.Checkbox
            ? string.Create(CultureInfo.InvariantCulture, $"{f.Id}=cb:{N(f.X)},{N(f.Y)},{N(f.Size)}")
            : string.Create(CultureInfo.InvariantCulture,
                $"{f.Id}={f.Type}:{N(f.X)},{N(f.Y)},{N(f.W)},{N(f.H)},{N(f.FontSize)},{f.Align}");

    // ── Integridad estructural ────────────────────────────────────────────────

    [Fact]
    public void Manifest_HasNoDuplicateFieldIds()
    {
        var dupes = Manifest().Fields
            .GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        dupes.Should().BeEmpty("ningún id de campo del manifest debe repetirse: {0}", string.Join(", ", dupes));
    }

    [Fact]
    public void Manifest_AllFieldsWithinPageBounds()
    {
        var m = Manifest();
        var outOfBounds = m.Fields.Where(f =>
        {
            var right = f.Type == FurFieldType.Checkbox ? f.X + f.Size : f.X + f.W;
            var bottom = f.Type == FurFieldType.Checkbox ? f.Y + f.Size : f.Y + f.H;
            return f.X < 0 || f.Y < 0 || right > m.PageWidth || bottom > m.PageHeight;
        }).Select(f => f.Id).ToList();

        outOfBounds.Should().BeEmpty(
            "todo campo debe caer dentro de {0}x{1}; fuera de límites: {2}",
            N(m.PageWidth), N(m.PageHeight), string.Join(", ", outOfBounds));
    }

    [Fact]
    public void Mapper_EmitsOnlyTokensDefinedInManifest()
    {
        // Todo token que el FurFieldMapper produce debe tener un campo en el manifest (si no, no se
        // pinta). Cubre matrícula y traspaso para maximizar los tokens ejercitados.
        var ids = new HashSet<string>(Manifest().Fields.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);

        var tokens = FurFieldMapper.Map(SampleMatricula()).Keys
            .Concat(FurFieldMapper.Map(SampleTraspaso()).Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sinPlacement = tokens.Where(t => !ids.Contains(t)).ToList();
        sinPlacement.Should().BeEmpty("todo token del mapper debe existir en el manifest: {0}",
            string.Join(", ", sinPlacement));
    }

    // ── Línea base congelada ──────────────────────────────────────────────────

    [Fact]
    public void Manifest_MatchesFrozenBaseline()
    {
        var actual = new HashSet<string>(Manifest().Fields.Select(Canon));
        var baseline = new HashSet<string>(
            Baseline.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // AC2: campos descuadrados (misma id, geometría distinta) o AC3: eliminados.
        var faltantes = baseline.Except(actual).ToList();
        // AC3: campos nuevos sin línea base (o AC2: la nueva geometría de un campo movido).
        var nuevos = actual.Except(baseline).ToList();

        (faltantes.Count + nuevos.Count).Should().Be(0,
            "el manifest cambió respecto a la línea base; regenera la línea base de forma explícita.\n" +
            "  descuadrados/eliminados:\n    {0}\n  nuevos/movidos:\n    {1}",
            string.Join("\n    ", faltantes), string.Join("\n    ", nuevos));
    }

    // ── Verificación de la lógica de la guardia (AC2/AC3) con entradas sintéticas ──

    [Fact]
    public void Guard_DetectsMovedField()
    {
        // AC2: mover una coordenada de un campo hace que su huella deje de coincidir con la base.
        var fields = Manifest().Fields.ToList();
        var moved = fields[0];
        var mutated = fields.Select(Canon).ToList();
        mutated[0] = Canon(moved).Replace($"{moved.Id}=", $"{moved.Id}=X"); // simula geometría alterada

        var baseline = new HashSet<string>(fields.Select(Canon));
        var diff = mutated.Where(c => !baseline.Contains(c)).ToList();

        diff.Should().ContainSingle().Which.Should().StartWith(moved.Id + "=");
    }

    [Fact]
    public void Guard_DetectsNewFieldWithoutBaseline()
    {
        // AC3: un campo nuevo no presente en la base es señalado (no pasa en silencio).
        var baseline = new HashSet<string>(Manifest().Fields.Select(Canon));
        var withNew = Manifest().Fields.Select(Canon).Append("campo_nuevo=text:1,1,1,1,7,Left").ToList();

        withNew.Where(c => !baseline.Contains(c)).Should().ContainSingle()
            .Which.Should().StartWith("campo_nuevo=");
    }

    // Ejecutar manualmente para regenerar la línea base tras un cambio DELIBERADO del manifest.
    [Fact(Skip = "Emisor de línea base; quitar Skip solo para regenerar Baseline tras un cambio deliberado.")]
    public void EmitBaseline()
    {
        foreach (var line in Manifest().Fields.Select(Canon))
            _output.WriteLine(line);
    }

    private static FurDocumentData SampleMatricula() => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000001",
        Modalidad: "matricula_inicial",
        TipologiaCodigo: "matricula_inicial",
        Vehiculo: new VehiculoDatos(
            Marca: "TESLA", Linea: "MODELO Y", Modelo: "2026", Color: "BLANCO",
            Clase: "CAMIONETA", Combustible: "ELECTRICO", Cilindraje: "0",
            Vin: "LRWYGCFJ7TC495717", Placa: "YYY090"),
        Organismo: new OrganismoTransito("25286000", "OT", "CIUDAD"),
        Partes: [new DocumentParte("comprador", "DANIEL AMADO GARCIA", "1193552679", null, DocumentType: "CC")],
        ValorVenta: null, Causal: null, SellosFirma: []);

    private static FurDocumentData SampleTraspaso() => SampleMatricula() with
    {
        Modalidad = "traspaso",
        TipologiaCodigo = "traspaso_standard",
        Partes =
        [
            new DocumentParte("vendedor", "AMOR JIMENEZ GUERRA", "1000445459", null, DocumentType: "CC"),
            new DocumentParte("comprador", "STEFFEN REICHERT", "C27WKYL7", null, DocumentType: "PAS"),
        ],
    };

    // Línea base congelada de la geometría del manifest (version 2026-07-03...-newtpl792).
    // Recalibrada 2026-07-24 tras reemplazar el blank AUTOMOTOR por el formulario oficial 792×612
    // (HU #10921). Regenerar SOLO de forma deliberada vía EmitBaseline tras recalibrar el manifest.
    private const string Baseline = """
        traffic_secretary_name=Text:525,64,175,11.9,6.5,Left
        traffic_secretary_city=Text:500,89,50,11.9,6.5,Left
        traffic_secretary_code=Text:551,89,48,11.8,6.5,Left
        processing_day=Text:593.4,91,25.7,11.8,7.7,Center
        processing_month=Text:619.4,89.8,27.3,12.2,7.8,Center
        processing_year=Text:649.3,91.1,30.4,11.9,7.7,Center
        plate_letter=Text:704.7,75.9,26.2,11.6,9.7,Center
        plate_number=Text:734.1,76,23.7,11.8,9.7,Center
        requested_process_1=cb:71.3,119.2,9.9
        requested_process_2=cb:119.5,121.1,9.8
        requested_process_11=cb:286.9,170.9,10.1
        vehicle_class_1=cb:53.6,222.4,9.9
        vehicle_class_5=cb:231.5,221.5,10
        vehicle_class_9=cb:101.7,232.7,10
        vehicle_brand=Text:380.8,129.3,70.2,13.3,7.8,Left
        vehicle_line=Text:461.9,128.4,70,13,7.7,Left
        vehicle_colors=Text:380.5,157.5,229,13.7,7.5,Left
        vehicle_model=Text:624.7,156.7,49.9,14.2,7.6,Left
        vehicle_displacement=Text:683.9,155.5,65,14,7.6,Left
        vehicle_capacity=Text:371.5,178.5,70.4,14,7.6,Center
        vehicle_fuel_type_1=cb:551.8,130,8
        vehicle_fuel_type_2=cb:580.5,130,8
        vehicle_fuel_type_3=cb:607.4,130,8
        vehicle_fuel_type_4=cb:634.2,130,8
        vehicle_fuel_type_5=cb:661,130,8
        vehicle_fuel_type_6=cb:689.8,130,8
        vehicle_fuel_type_7=cb:716.6,130,8
        vehicle_fuel_type_8=cb:743.4,130,8
        is_armored_vehicle_no=cb:536,168,8
        is_dismantling_armor_no=cb:670,168,8
        vehicle_bodywork_type=Text:382.3,245,176.7,16.7,7.8,Left
        vehicle_engine_number=Text:572.7,226.3,134.4,13.2,7.8,Left
        vehicle_chassis_number=Text:572.8,248.3,134.7,16.8,7.8,Left
        importacion_manifest=cb:382.8,309.6,10.1
        importacion_declaracion=cb:404.4,309.7,10.1
        remate_acta=cb:431.3,310.1,10
        remate_entidad=cb:460.7,310.1,10.1
        remate_lugar=cb:488.8,309.7,10.3
        vehicle_service_type_1=cb:577.3,362.6,10
        vehicle_service_type_2=cb:600.9,362.2,9.9
        vehicle_service_type_3=cb:624.3,362.4,9.9
        vehicle_service_type_4=cb:658.2,364,10.2
        vehicle_service_type_5=cb:687.9,364.1,9.8
        vehicle_service_type_6=cb:726.5,364.5,9.8
        vehicle_serial_number=Text:570,291.5,124.7,14.5,7.8,Left
        vehicle_vin_number=Text:569.1,313.7,124.5,14.5,7.8,Left
        alert_data_code_5=Text:595.6,338.6,142.1,14.4,7.2,Left
        vehicle_owner_first_last_name=Text:30,303.8,128.4,14.3,7.7,Left
        vehicle_owner_second_last_name=Text:158.9,304.3,113.6,14.4,7.8,Left
        vehicle_owner_name=Text:276.4,303.6,93.5,14.4,7.7,Left
        vehicle_owner_document_type_c=cb:34.7,336.9,9.9
        vehicle_owner_document_type_nit=cb:63,338.2,10
        vehicle_owner_document_type_nn=cb:90,337.6,9.9
        vehicle_owner_document_type_p=cb:117.6,338.2,10
        vehicle_owner_document_type_ce=cb:153.4,337.1,10
        vehicle_owner_document_type_ti=cb:192.1,336,10
        vehicle_owner_document_type_nuip=cb:236,338.1,10
        vehicle_owner_document_type_cd=cb:278.9,336.8,9.9
        vehicle_owner_document_number=Text:318,335.1,43,14.3,7.8,Left
        vehicle_owner_address=Text:56.6,364.6,154.7,14,7.6,Left
        vehicle_owner_city=Text:218.8,363.8,37,14,7.7,Left
        vehicle_owner_phone=Text:317,364.9,44.9,14.2,7.7,Left
        vehicle_owner_signature=Multiline:34.4,385.6,331.1,35.2,6.5,Left
        vehicle_buyer_first_last_name=Text:30.5,451.5,127.3,14,7.6,Left
        vehicle_buyer_second_last_name=Text:159.5,451.2,112.7,14.1,7.7,Left
        vehicle_buyer_name=Text:274.7,451.4,93.7,14.1,7.7,Left
        vehicle_buyer_document_type_c=cb:33.2,486.5,9.9
        vehicle_buyer_document_type_nit=cb:64.7,486.4,9.9
        vehicle_buyer_document_type_nn=cb:91.6,486.4,9.9
        vehicle_buyer_document_type_p=cb:121.2,486.5,9.9
        vehicle_buyer_document_type_ce=cb:154.2,486.4,9.9
        vehicle_buyer_document_type_ti=cb:190.1,486.3,9.9
        vehicle_buyer_document_type_nuip=cb:231.5,486.5,9.9
        vehicle_buyer_document_type_cd=cb:276.6,486.7,9.9
        vehicle_buyer_document_number=Text:314.8,482,42.9,14.1,7.7,Left
        vehicle_buyer_address=Text:52.5,508.7,158.1,14.1,7.7,Left
        vehicle_buyer_city=Text:215.9,508.7,36.8,14.1,7.7,Left
        vehicle_buyer_phone=Text:314.8,509.3,44.9,14.1,7.7,Left
        vehicle_buyer_signature=Multiline:33.2,529.7,341.6,35.3,8,Left
        observations=Multiline:381.9,477.6,403.1,33,7.2,Left
        """;
}
