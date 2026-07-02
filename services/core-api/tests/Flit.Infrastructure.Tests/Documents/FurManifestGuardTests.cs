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

    // Línea base congelada de la geometría del manifest (version 2026-07-buyer-owner-calibrated).
    // Regenerar SOLO de forma deliberada vía EmitBaseline tras recalibrar el manifest.
    private const string Baseline = """
        traffic_secretary_name=Text:593,44,185,10,7,Left
        traffic_secretary_city=Text:605,69,34,10,7,Left
        traffic_secretary_code=Text:645,69,40,10,7,Left
        processing_day=Text:698,69,25,10,7,Center
        processing_month=Text:724,69,25,10,7,Center
        processing_year=Text:750,69,30,10,7,Center
        plate_letter=Text:808,53,26,10,9,Center
        plate_number=Text:837,53,24,10,9,Center
        requested_process_1=cb:183,94,9
        requested_process_2=cb:229,94,9
        requested_process_11=cb:399,137,9
        vehicle_class_1=cb:170,181,9
        vehicle_class_5=cb:345,181,9
        vehicle_class_9=cb:219,190,9
        vehicle_brand=Text:488,101,68,11,7,Left
        vehicle_line=Text:566,101,68,11,7,Left
        vehicle_colors=Text:488,124,228,12,7,Left
        vehicle_model=Text:728,124,50,12,7,Left
        vehicle_displacement=Text:790,124,65,12,7,Left
        vehicle_capacity=Text:486,144,70,12,7,Center
        vehicle_fuel_type_1=cb:681,92,9
        vehicle_fuel_type_2=cb:707,92,9
        vehicle_fuel_type_3=cb:733,92,9
        vehicle_fuel_type_4=cb:762,92,9
        vehicle_fuel_type_5=cb:791,92,9
        vehicle_fuel_type_6=cb:818,92,9
        vehicle_fuel_type_7=cb:846,92,9
        vehicle_fuel_type_8=cb:871,92,9
        is_armored_vehicle_no=cb:617,137,9
        is_dismantling_armor_no=cb:750,137,9
        vehicle_bodywork_type=Text:490,201,172,14,7,Left
        vehicle_engine_number=Text:674,184,130,11,7,Left
        vehicle_chassis_number=Text:674,202,130,14,7,Left
        importacion_manifest=cb:493,255,9
        importacion_declaracion=cb:519,255,9
        remate_acta=cb:544,255,9
        remate_entidad=cb:571,255,9
        remate_lugar=cb:598,255,9
        vehicle_service_type_1=cb:682,296,9
        vehicle_service_type_2=cb:707,296,9
        vehicle_service_type_3=cb:733,296,9
        vehicle_service_type_4=cb:762,296,9
        vehicle_service_type_5=cb:791,296,9
        vehicle_service_type_6=cb:831,296,9
        vehicle_serial_number=Text:672,237,120,12,7,Left
        vehicle_vin_number=Text:672,315,120,12,7,Left
        alert_data_code_5=Text:700,276,140,12,6.5,Left
        vehicle_owner_first_last_name=Text:148,248,126,12,7,Left
        vehicle_owner_second_last_name=Text:276,248,110,12,7,Left
        vehicle_owner_name=Text:388,248,92,12,7,Left
        vehicle_owner_document_type_c=cb:151,276,9
        vehicle_owner_document_type_nit=cb:182,276,9
        vehicle_owner_document_type_nn=cb:208,276,9
        vehicle_owner_document_type_p=cb:237,276,9
        vehicle_owner_document_type_ce=cb:270,276,9
        vehicle_owner_document_type_ti=cb:305,276,9
        vehicle_owner_document_type_nuip=cb:345,276,9
        vehicle_owner_document_type_cd=cb:390,276,9
        vehicle_owner_document_number=Text:438,272,42,12,7,Left
        vehicle_owner_address=Text:170,300,155,12,7,Left
        vehicle_owner_city=Text:330,300,36,12,7,Left
        vehicle_owner_phone=Text:438,300,44,12,7,Left
        vehicle_owner_signature=Multiline:146,318,335,30,6,Left
        vehicle_buyer_first_last_name=Text:148,370,126,12,7,Left
        vehicle_buyer_second_last_name=Text:276,370,110,12,7,Left
        vehicle_buyer_name=Text:388,370,92,12,7,Left
        vehicle_buyer_document_type_c=cb:151,410,9
        vehicle_buyer_document_type_nit=cb:182,410,9
        vehicle_buyer_document_type_nn=cb:208,410,9
        vehicle_buyer_document_type_p=cb:237,410,9
        vehicle_buyer_document_type_ce=cb:270,410,9
        vehicle_buyer_document_type_ti=cb:305,410,9
        vehicle_buyer_document_type_nuip=cb:345,410,9
        vehicle_buyer_document_type_cd=cb:390,410,9
        vehicle_buyer_document_number=Text:438,406,42,12,7,Left
        vehicle_buyer_address=Text:170,422,155,12,7,Left
        vehicle_buyer_city=Text:330,422,36,12,7,Left
        vehicle_buyer_phone=Text:438,422,44,12,7,Left
        vehicle_buyer_signature=Multiline:146,444,335,30,6,Left
        observations=Multiline:156,462,620,28,6.5,Left
        """;
}
