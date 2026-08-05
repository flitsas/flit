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
/// HU #11255 (CI1) — la línea base congelada se extiende a los tres formatos (automotor, maquinaria,
/// remolques): antes solo automotor tenía guardia, así que un descuadre en los otros dos formatos no
/// lo detectaba nadie.
/// </summary>
public sealed class FurManifestGuardTests
{
    private readonly ITestOutputHelper _output;

    public FurManifestGuardTests(ITestOutputHelper output) => _output = output;

    private static FurFieldManifest Manifest() => FurFieldManifestLoader.LoadEmbedded();

    private static FurFieldManifest Manifest(FurTemplateFormat format) => FurFieldManifestLoader.LoadEmbedded(format);

    private static string N(double d) => d.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Huella canónica de un campo: id + tipo + geometría + <c>AutoFit</c> (HU #11256). Cualquier
    /// cambio la altera, incluida la bandera de auto-encaje: un <c>autoFit</c> que se cuele sin
    /// querer en un sello de firma debe fallar la guardia igual que un descuadre.
    /// </summary>
    private static string Canon(FurFieldDefinition f) =>
        f.Type == FurFieldType.Checkbox
            ? string.Create(CultureInfo.InvariantCulture, $"{f.Id}=cb:{N(f.X)},{N(f.Y)},{N(f.Size)}")
            : string.Create(CultureInfo.InvariantCulture,
                $"{f.Id}={f.Type}:{N(f.X)},{N(f.Y)},{N(f.W)},{N(f.H)},{N(f.FontSize)},{f.Align},{f.AutoFit}");

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

    [Theory]
    [InlineData(FurTemplateFormat.Automotor)]
    [InlineData(FurTemplateFormat.Maquinaria)]
    [InlineData(FurTemplateFormat.Remolques)]
    public void Manifest_MatchesFrozenBaseline(FurTemplateFormat format)
    {
        var actual = new HashSet<string>(Manifest(format).Fields.Select(Canon));
        var baseline = new HashSet<string>(
            BaselineFor(format).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // AC2: campos descuadrados (misma id, geometría distinta) o AC3: eliminados.
        var faltantes = baseline.Except(actual).ToList();
        // AC3: campos nuevos sin línea base (o AC2: la nueva geometría de un campo movido).
        var nuevos = actual.Except(baseline).ToList();

        (faltantes.Count + nuevos.Count).Should().Be(0,
            "el manifest {2} cambió respecto a la línea base; regenera la línea base de forma explícita.\n" +
            "  descuadrados/eliminados:\n    {0}\n  nuevos/movidos:\n    {1}",
            string.Join("\n    ", faltantes), string.Join("\n    ", nuevos), format);
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
    [Theory(Skip = "Emisor de línea base; quitar Skip solo para regenerar Baseline tras un cambio deliberado.")]
    [InlineData(FurTemplateFormat.Automotor)]
    [InlineData(FurTemplateFormat.Maquinaria)]
    [InlineData(FurTemplateFormat.Remolques)]
    public void EmitBaseline(FurTemplateFormat format)
    {
        _output.WriteLine($"── {format} ──");
        foreach (var line in Manifest(format).Fields.Select(Canon))
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
    // (HU #10921). Regenerada 2026-08-04 (HU #11256) tras añadir `AutoFit` a la huella `Canon`: solo
    // `observations` cambia de valor (True); el resto queda en False, igual que hoy. Regenerada
    // 2026-08-05 (HU #11257) tras añadir `requested_process_12` (levantamiento de prenda), derivada
    // directamente de `requested_process_11` (mismo `size`, offset rótulo11→rótulo12). Regenerar SOLO
    // de forma deliberada vía EmitBaseline tras recalibrar el manifest.
    private const string Baseline = """
        traffic_secretary_name=Text:525,64,175,11.9,6.5,Left,False
        traffic_secretary_city=Text:500,89,50,11.9,6.5,Left,False
        traffic_secretary_code=Text:551,89,48,11.8,6.5,Left,False
        processing_day=Text:593.4,91,25.7,11.8,7.7,Center,False
        processing_month=Text:619.4,89.8,27.3,12.2,7.8,Center,False
        processing_year=Text:649.3,91.1,30.4,11.9,7.7,Center,False
        plate_letter=Text:704.7,75.9,26.2,11.6,9.7,Center,False
        plate_number=Text:734.1,76,23.7,11.8,9.7,Center,False
        requested_process_1=cb:71.3,119.2,9.9
        requested_process_2=cb:119.5,121.1,9.8
        requested_process_11=cb:286.9,170.9,10.1
        requested_process_12=cb:343.3,170.9,10.1
        vehicle_class_1=cb:53.6,222.4,9.9
        vehicle_class_5=cb:231.5,221.5,10
        vehicle_class_9=cb:101.7,232.7,10
        vehicle_brand=Text:380.8,129.3,70.2,13.3,7.8,Left,False
        vehicle_line=Text:461.9,128.4,70,13,7.7,Left,False
        vehicle_colors=Text:380.5,157.5,229,13.7,7.5,Left,False
        vehicle_model=Text:624.7,156.7,49.9,14.2,7.6,Left,False
        vehicle_displacement=Text:683.9,155.5,65,14,7.6,Left,False
        vehicle_capacity=Text:371.5,178.5,70.4,14,7.6,Center,False
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
        vehicle_bodywork_type=Text:382.3,245,176.7,16.7,7.8,Left,False
        vehicle_engine_number=Text:572.7,226.3,134.4,13.2,7.8,Left,False
        vehicle_chassis_number=Text:572.8,248.3,134.7,16.8,7.8,Left,False
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
        vehicle_serial_number=Text:570,286.5,124.7,14.5,7.8,Left,False
        vehicle_vin_number=Text:569.1,313.7,124.5,14.5,7.8,Left,False
        alert_data_code_5=Text:595.6,338.6,142.1,14.4,7.2,Left,False
        vehicle_owner_first_last_name=Text:30,303.8,128.4,14.3,7.7,Left,False
        vehicle_owner_second_last_name=Text:158.9,304.3,113.6,14.4,7.8,Left,False
        vehicle_owner_name=Text:276.4,303.6,93.5,14.4,7.7,Left,False
        vehicle_owner_document_type_c=cb:34.7,336.9,9.9
        vehicle_owner_document_type_nit=cb:63,338.2,10
        vehicle_owner_document_type_nn=cb:90,337.6,9.9
        vehicle_owner_document_type_p=cb:117.6,338.2,10
        vehicle_owner_document_type_ce=cb:153.4,337.1,10
        vehicle_owner_document_type_ti=cb:192.1,336,10
        vehicle_owner_document_type_nuip=cb:236,338.1,10
        vehicle_owner_document_type_cd=cb:278.9,336.8,9.9
        vehicle_owner_document_number=Text:318,335.1,43,14.3,7.8,Left,False
        vehicle_owner_address=Text:56.6,364.6,154.7,14,7.6,Left,False
        vehicle_owner_city=Text:218.8,363.8,37,14,7.7,Left,False
        vehicle_owner_phone=Text:317,364.9,44.9,14.2,7.7,Left,False
        vehicle_owner_signature=Multiline:34.4,385.6,331.1,35.2,6.5,Left,False
        vehicle_buyer_first_last_name=Text:30.5,451.5,127.3,14,7.6,Left,False
        vehicle_buyer_second_last_name=Text:159.5,451.2,112.7,14.1,7.7,Left,False
        vehicle_buyer_name=Text:274.7,451.4,93.7,14.1,7.7,Left,False
        vehicle_buyer_document_type_c=cb:33.2,486.5,9.9
        vehicle_buyer_document_type_nit=cb:64.7,481.4,9.9
        vehicle_buyer_document_type_nn=cb:91.6,486.4,9.9
        vehicle_buyer_document_type_p=cb:121.2,486.5,9.9
        vehicle_buyer_document_type_ce=cb:154.2,486.4,9.9
        vehicle_buyer_document_type_ti=cb:190.1,486.3,9.9
        vehicle_buyer_document_type_nuip=cb:231.5,486.5,9.9
        vehicle_buyer_document_type_cd=cb:276.6,486.7,9.9
        vehicle_buyer_document_number=Text:314.8,482,42.9,14.1,7.7,Left,False
        vehicle_buyer_address=Text:52.5,508.7,158.1,14.1,7.7,Left,False
        vehicle_buyer_city=Text:215.9,508.7,36.8,14.1,7.7,Left,False
        vehicle_buyer_phone=Text:314.8,509.3,44.9,14.1,7.7,Left,False
        vehicle_buyer_signature=Multiline:33.2,529.7,341.6,35.3,8,Left,False
        observations=Multiline:379.9,472.6,403.1,33,7.2,Left,True
        """;

    // Línea base congelada de la geometría del manifest MAQUINARIA (version 2026-07-24-maquinaria-v2-traspaso-calib,
    // recalibrada para HU #11255 con el desplazamiento de `observations`). Regenerada 2026-08-04
    // (HU #11256) tras añadir `AutoFit` a la huella `Canon`: solo `observations` cambia de valor
    // (True); el resto queda en False, igual que hoy. Regenerada 2026-08-05 (HU #11257) tras añadir
    // `requested_process_11`/`_12` (modalidad de prenda, H2): en MAQUINARIA los rótulos IMPRESOS de
    // prenda son "10"/"11" (no "11"/"12" como en automotor/remolques — ver calibrate-prenda-boxes.py),
    // pero los IDs internos del manifest siguen siendo `requested_process_11`(constitución)/
    // `_12`(levantamiento) en los tres formatos: es un contrato semántico del mapper, no el número
    // impreso. Medido por respaldo (offset desde `requested_process_1`/`_2`), no hay rectángulo
    // vectorial que ancle la casilla en el blank. Regenerar SOLO de forma deliberada vía EmitBaseline
    // tras recalibrar el manifest.
    private const string BaselineMaquinaria = """
        traffic_secretary_name=Text:665,44,280,10,7,Left,False
        traffic_secretary_city=Text:648,66,62,10,7,Left,False
        traffic_secretary_code=Text:715,66,55,10,7,Left,False
        processing_day=Text:776,71,28,10,7,Center,False
        processing_month=Text:808,71,28,10,7,Center,False
        processing_year=Text:842,71,32,10,7,Center,False
        plate_letter=Text:900,59,28,11,7,Center,False
        plate_number=Text:935,59,30,11,7,Center,False
        requested_process_1=cb:101,102,9
        requested_process_2=cb:170,102,9
        requested_process_11=cb:303.6,129,9
        requested_process_12=cb:370,129,9
        vehicle_brand=Text:508,95,66,12,7,Left,False
        vehicle_line=Text:578,95,74,12,7,Left,False
        vehicle_colors=Text:500,134,268,12,7,Left,False
        vehicle_model=Text:778,134,62,12,7,Left,False
        vehicle_fuel_type_1=cb:763,308,9
        vehicle_fuel_type_2=cb:830,308,9
        vehicle_fuel_type_5=cb:915,308,9
        vehicle_fuel_type_3=cb:763,326,9
        vehicle_fuel_type_4=cb:830,326,9
        vehicle_engine_number=Text:736,204,150,11,7,Left,False
        vehicle_vin_number=Text:736,227,240,12,7,Left,False
        vehicle_owner_first_last_name=Text:90,297,150,12,7,Left,False
        vehicle_owner_second_last_name=Text:245,297,150,12,7,Left,False
        vehicle_owner_name=Text:400,297,120,12,7,Left,False
        vehicle_owner_document_type_c=cb:94,323,9
        vehicle_owner_document_type_nit=cb:127,323,9
        vehicle_owner_document_type_nn=cb:159,323,9
        vehicle_owner_document_type_p=cb:193,323,9
        vehicle_owner_document_type_ce=cb:234,323,9
        vehicle_owner_document_type_ti=cb:276,323,9
        vehicle_owner_document_type_nuip=cb:322,323,9
        vehicle_owner_document_type_cd=cb:380,323,9
        vehicle_owner_document_number=Text:428,322,62,12,7,Left,False
        vehicle_owner_address=Text:90,350,190,12,7,Left,False
        vehicle_owner_city=Text:286,350,120,12,7,Left,False
        vehicle_owner_phone=Text:418,350,70,12,7,Left,False
        vehicle_owner_signature=Multiline:90,376,390,28,4,Left,False
        vehicle_buyer_first_last_name=Text:90,448,150,12,7,Left,False
        vehicle_buyer_second_last_name=Text:245,448,150,12,7,Left,False
        vehicle_buyer_name=Text:400,448,120,12,7,Left,False
        vehicle_buyer_document_type_c=cb:94,484,9
        vehicle_buyer_document_type_nit=cb:127,484,9
        vehicle_buyer_document_type_nn=cb:159,484,9
        vehicle_buyer_document_type_p=cb:193,484,9
        vehicle_buyer_document_type_ce=cb:234,484,9
        vehicle_buyer_document_type_ti=cb:276,484,9
        vehicle_buyer_document_type_nuip=cb:322,484,9
        vehicle_buyer_document_type_cd=cb:380,484,9
        vehicle_buyer_document_number=Text:428,483,62,12,7,Left,False
        vehicle_buyer_address=Text:90,510,190,12,7,Left,False
        vehicle_buyer_city=Text:286,510,120,12,7,Left,False
        vehicle_buyer_phone=Text:418,510,70,12,7,Left,False
        vehicle_buyer_signature=Multiline:90,537,390,28,4,Left,False
        observations=Multiline:496,445,490,38,6.5,Left,True
        """;

    // Línea base congelada de la geometría del manifest REMOLQUES (version 2026-07-24-remolques-v1,
    // recalibrada para HU #11255 con el desplazamiento de `observations` y `vehicle_serial_number`).
    // Regenerada 2026-08-04 (HU #11256) tras añadir `AutoFit` a la huella `Canon`: solo `observations`
    // cambia de valor (True); el resto queda en False, igual que hoy. Regenerada 2026-08-05
    // (HU #11257) tras añadir `requested_process_10` (bonus, sin relación con prenda — "DUPLICADO
    // TARJETA DE REGISTRO" impreso; medido en el mismo barrido para no repetir la calibración cuando
    // una HU futura lo necesite, aún sin consumidor en el mapper) y `requested_process_11`/`_12`
    // (modalidad de prenda, H2), medidas por respaldo (offset desde `requested_process_1`/`_2`; no hay
    // rectángulo vectorial que ancle la casilla en el blank). Regenerar SOLO de forma deliberada vía
    // EmitBaseline tras recalibrar el manifest.
    private const string BaselineRemolques = """
        traffic_secretary_name=Text:668,44,280,10,7,Left,False
        traffic_secretary_city=Text:650,64,62,10,7,Left,False
        traffic_secretary_code=Text:718,64,55,10,7,Left,False
        processing_day=Text:779,71,28,10,7,Center,False
        processing_month=Text:811,71,28,10,7,Center,False
        processing_year=Text:845,71,32,10,7,Center,False
        plate_letter=Text:903,56,28,11,9,Center,False
        plate_number=Text:936,56,30,11,9,Center,False
        requested_process_1=cb:86,101,9
        requested_process_2=cb:155,101,9
        requested_process_10=cb:291.9,128,9
        requested_process_11=cb:361.5,128,9
        requested_process_12=cb:421.7,128,9
        vehicle_brand=Text:498,96,160,12,7,Left,False
        vehicle_line=Text:666,96,165,12,7,Left,False
        vehicle_colors=Text:498,132,130,12,7,Left,False
        vehicle_model=Text:633,132,60,12,7,Left,False
        vehicle_serial_number=Text:736,199,240,12,7,Left,False
        vehicle_vin_number=Text:736,228,240,12,7,Left,False
        vehicle_owner_first_last_name=Text:90,297,150,12,7,Left,False
        vehicle_owner_second_last_name=Text:245,297,150,12,7,Left,False
        vehicle_owner_name=Text:400,297,120,12,7,Left,False
        vehicle_owner_document_type_c=cb:94,321,9
        vehicle_owner_document_type_nit=cb:127,321,9
        vehicle_owner_document_type_nn=cb:159,321,9
        vehicle_owner_document_type_p=cb:193,321,9
        vehicle_owner_document_type_ce=cb:234,321,9
        vehicle_owner_document_type_ti=cb:279,321,9
        vehicle_owner_document_type_nuip=cb:329,321,9
        vehicle_owner_document_type_cd=cb:385,321,9
        vehicle_owner_document_number=Text:431,320,62,12,7,Left,False
        vehicle_owner_address=Text:90,348,190,12,7,Left,False
        vehicle_owner_city=Text:286,348,120,12,7,Left,False
        vehicle_owner_phone=Text:418,348,70,12,7,Left,False
        vehicle_owner_signature=Multiline:90,376,390,28,6,Left,False
        vehicle_buyer_first_last_name=Text:90,448,150,12,7,Left,False
        vehicle_buyer_second_last_name=Text:245,448,150,12,7,Left,False
        vehicle_buyer_name=Text:400,448,120,12,7,Left,False
        vehicle_buyer_document_type_c=cb:94,482,9
        vehicle_buyer_document_type_nit=cb:127,482,9
        vehicle_buyer_document_type_nn=cb:159,482,9
        vehicle_buyer_document_type_p=cb:193,482,9
        vehicle_buyer_document_type_ce=cb:234,482,9
        vehicle_buyer_document_type_ti=cb:279,482,9
        vehicle_buyer_document_type_nuip=cb:329,482,9
        vehicle_buyer_document_type_cd=cb:385,482,9
        vehicle_buyer_document_number=Text:431,481,62,12,7,Left,False
        vehicle_buyer_address=Text:90,508,190,12,7,Left,False
        vehicle_buyer_city=Text:286,508,120,12,7,Left,False
        vehicle_buyer_phone=Text:418,508,70,12,7,Left,False
        vehicle_buyer_signature=Multiline:90,537,390,28,6,Left,False
        observations=Multiline:496,417,490,55,6.5,Left,True
        """;

    private static string BaselineFor(FurTemplateFormat format) => format switch
    {
        FurTemplateFormat.Maquinaria => BaselineMaquinaria,
        FurTemplateFormat.Remolques => BaselineRemolques,
        _ => Baseline,
    };
}
