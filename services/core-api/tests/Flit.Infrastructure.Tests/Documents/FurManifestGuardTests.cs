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
    /// Huella canónica de un campo: id + tipo + geometría + <c>AutoFit</c> (HU #11256) +
    /// <c>MinFontSize</c> (HU sin ADO 2026-08-11, tercera tanda). Cualquier cambio la altera,
    /// incluidas las banderas que cambian COMPORTAMIENTO de encaje sin mover geometría: un
    /// <c>autoFit</c> que se cuele sin querer en un sello de firma, o un <c>minFontSize</c> que
    /// alguien baje en silencio (perdiendo la legibilidad mínima de un campo), deben fallar la
    /// guardia igual que un descuadre.
    /// </summary>
    private static string Canon(FurFieldDefinition f) =>
        f.Type == FurFieldType.Checkbox
            ? string.Create(CultureInfo.InvariantCulture, $"{f.Id}=cb:{N(f.X)},{N(f.Y)},{N(f.Size)}")
            : string.Create(CultureInfo.InvariantCulture,
                $"{f.Id}={f.Type}:{N(f.X)},{N(f.Y)},{N(f.W)},{N(f.H)},{N(f.FontSize)},{f.Align},{f.AutoFit}," +
                $"{(f.MinFontSize.HasValue ? N(f.MinFontSize.Value) : "null")}");

    // ── Integridad estructural ────────────────────────────────────────────────

    /// <summary>
    /// Casillas del "documento de origen" (sección 5 del FUR) declaradas en el manifiesto desde su
    /// calibración inicial y que el mapper NUNCA ha emitido: el trámite no captura si el vehículo
    /// entró por importación o por remate, así que no hay dato con el que marcarlas.
    ///
    /// <para>Se listan de forma explícita, y no se ignoran por categoría, para que la guardia siga
    /// detectando cualquier casilla huérfana NUEVA. Detectadas al introducir la guardia (HU #11641):
    /// son deuda previa, no una regresión, y conectarlas exige antes decidir en producto cómo se
    /// declara el origen del vehículo.</para>
    /// </summary>
    private static readonly HashSet<string> HuerfanasConocidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "importacion_manifest",
        "importacion_declaracion",
        "remate_acta",
        "remate_entidad",
        "remate_lugar",
    };

    /// <summary>
    /// HU #11641 (AC6) — la dirección INVERSA de <see cref="Mapper_EmitsOnlyTokensDefinedInManifest"/>:
    /// ninguna casilla puede quedar declarada en el manifiesto sin que el mapper la emita nunca.
    ///
    /// <para>Una casilla huérfana es configuración muerta que aparenta cobertura: alguien la lee en el
    /// manifiesto y da por hecho que el formulario la marca. Peor aún, ocupa una celda —si más adelante
    /// se declara otra casilla sobre la misma celda, dos marcas se pisan y el formulario deja de decir
    /// qué trámite se solicitó—. El comentario del manifiesto de maquinaria ya advierte de ese riesgo.</para>
    /// </summary>
    [Theory]
    [InlineData(FurTemplateFormat.Automotor)]
    [InlineData(FurTemplateFormat.Maquinaria)]
    [InlineData(FurTemplateFormat.Remolques)]
    public void Manifest_NoDeclaraCasillasHuerfanas(FurTemplateFormat format)
    {
        var sample = SampleMatricula() with { TemplateFormat = format };
        var traspaso = SampleTraspaso() with { TemplateFormat = format };
        var emitidos = new HashSet<string>(
            FurFieldMapper.Map(sample).Keys.Concat(FurFieldMapper.Map(traspaso).Keys),
            StringComparer.OrdinalIgnoreCase);

        var huerfanas = Manifest(format).Fields
            .Where(f => f.Type == FurFieldType.Checkbox && !emitidos.Contains(f.Id))
            .Select(f => f.Id)
            .Where(id => !HuerfanasConocidas.Contains(id))
            .ToList();

        huerfanas.Should().BeEmpty(
            "toda casilla declarada en {0} debe tener quien la emita, o es configuración muerta: {1}",
            format, string.Join(", ", huerfanas));
    }

    /// <summary>
    /// HU #11641 (AC6) — ningún campo que el mapper CALCULA puede descartarse en silencio por no estar
    /// declarado en el manifiesto del formato.
    ///
    /// <para><see cref="FurOverlayRenderer"/> recorre los campos del manifiesto, no las claves del
    /// diccionario: un token sin placement se tira sin log ni excepción. Así es como los formatos de
    /// maquinaria y remolques llevan tiempo imprimiéndose sin tipo de servicio ni empresa vinculadora
    /// pese a que el sistema los calcula.</para>
    ///
    /// <para>Para AUTOMOTOR la ausencia es un FALLO. Para maquinaria y remolques la corrección está
    /// diferida (decisión del supervisor, 2026-08-19: esos formatos aún no están en operación), así que
    /// la prueba se salta DECLARANDO la brecha en el motivo, en vez de desaparecer. Es la diferencia
    /// entre una deuda visible en cada ejecución y un descarte silencioso.</para>
    /// </summary>
    [Theory]
    [InlineData(FurTemplateFormat.Automotor)]
    [InlineData(FurTemplateFormat.Maquinaria)]
    [InlineData(FurTemplateFormat.Remolques)]
    public void Manifest_DeclaraTodoLoQueElMapperCalcula(FurTemplateFormat format)
    {
        var declarados = new HashSet<string>(
            Manifest(format).Fields.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);

        var descartados = FurFieldMapper.Map(SampleMatricula()).Keys
            .Concat(FurFieldMapper.Map(SampleTraspaso()).Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(t => !declarados.Contains(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        if (format != FurTemplateFormat.Automotor && descartados.Count > 0)
        {
            Assert.Skip(
                $"DEUDA CONOCIDA ({format}): el manifiesto descarta en silencio {descartados.Count} " +
                $"campos que el mapper calcula — {string.Join(", ", descartados)}. Corrección diferida " +
                "hasta que el formato entre en operación (HU #11641, AC5 diferido).");
        }

        descartados.Should().BeEmpty(
            "el formato {0} descarta en silencio campos ya calculados: {1}",
            format, string.Join(", ", descartados));
    }

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
    // directamente de `requested_process_11` (mismo `size`, offset rótulo11→rótulo12). Regenerada
    // 2026-08-11 tras añadir `linked_company_name`/`linked_company_nit` (casilla 19 "EMPRESA
    // VINCULADORA"): x/y medidos con PyMuPDF sobre las etiquetas "NOMBRE"/"NIT" y los bordes
    // vectoriales del recuadro del blank AUTOMOTOR (calibrate-labels.py); solo existe en este formato
    // (maquinaria/remolques no tienen esa casilla en su formulario oficial). Regenerada de nuevo el
    // mismo día (tercera tanda, PLAN B) — primer intento con `linked_company_name` como `multiline` +
    // `autoFit: true` (2 líneas); descartado tras medir con PyMuPDF que, aunque el recuadro interior
    // (~32pt libres bajo "NOMBRE": label hasta 403,45, borde inferior ~435,6/435,7 compartido con la
    // casilla 20) sobra para 2 líneas a 7,6pt, para razones sociales realmente largas
    // `FurTextFitter.FitMultiline` NO se queda en 2 líneas: sigue bajando de cuerpo hasta encontrar un
    // tamaño donde 3 líneas SÍ entran (~5,1pt, casi ilegible) — correcto para `observations` (párrafo
    // libre) pero no aquí. Pasó a `text` de una sola línea con `minFontSize: 6.5`.
    //
    // Regenerada UNA CUARTA VEZ el mismo día (cuarta tanda) — el coordinador corrigió el rumbo: el
    // criterio no era "2 líneas", era "nunca bajar de 7pt"; con el alto real medido (~32pt) caben 3
    // renglones a 7pt. Verificación pedida: `FitMultiline` NO honraba ningún piso explícito (usaba
    // `MinMultilineFontSize = 5` fijo) — se extendió con el mismo parámetro opcional `minFontSize` que
    // ya tenía `Fit`. Pero terminó usándose `Fit` (no `FitMultiline`) para este campo: probado con
    // PdfSharpCore real, `FitMultiline` SIEMPRE prefiere envolver a 2 líneas al cuerpo base antes que
    // encoger a 1 línea (su diseño, el mismo que habilita las 3 líneas de las razones sociales largas)
    // — "TRANSPORTES DEL NORTE S.A.S." (el caso corto de referencia) pasaba de 1 línea a ~6,85pt a 2
    // líneas a 7,6pt, violando "el caso corto no cambia de aspecto". `Fit` sí prioriza 1 línea encogida
    // antes de partir (su diseño original, HU #11048), pero tenía el mismo problema de piso fuera de
    // la rejilla de pasos de 0,25 — se añadieron pruebas explícitas AL piso exacto en los pasos (2) y
    // (3), y el paso (4) de último recurso ahora aprovecha todas las líneas que el alto admite antes
    // de truncar, en vez de colapsar a 1 sola. Con `Fit` + `minFontSize: 7.0` + `h` para 3 líneas
    // (`MaxLines(27, 7.0) = 3`, nunca 4: el piso de 4 líneas está en h=35) + `w` subido de 128 a 130,5
    // (necesario para que "TRANSPORTES DEL NORTE S.A.S." entre en 1 línea exactamente a 7,0pt —
    // measure() real da 130,1pt; 130,5 deja 1,1pt de margen antes del divisor de la casilla NIT en
    // x=703,6, nunca lo toca) el caso corto queda IDÉNTICO en posición (x/y sin cambios) y
    // prácticamente idéntico en tamaño (7,0 vs 6,85pt de antes, imperceptible). Límite real medido:
    // las razones sociales de 69-79 caracteres (los ejemplos de referencia) entran en 3 líneas pero NO
    // completas ni con w=130,5 — el ancho que haría falta es ~133-134pt, que cruzaría el divisor de la
    // casilla NIT (inaceptable); a 7,0pt fijo, 3 líneas de ~130pt no alcanzan por la ineficiencia del
    // ajuste de palabras (huecos al final de cada línea). Truncan con elipsis, que es exactamente el
    // comportamiento que el propio coordinador definió como correcto para "cuando de verdad no cabe a
    // 7pt". `linked_company_nit` no cambia. La huella `Canon` suma `MinFontSize` (null en el resto de
    // campos del manifest, que no lo declaran).
    //
    // QUINTA TANDA (2026-08-12) — `minFontSize` baja de 7,0 a 6,0 SOLO en `linked_company_name`. El
    // caso real que lo motivó es el que la cuarta tanda dio por bueno truncar: la razón social que
    // devuelve el RUES para el NIT 890903938 — "BANCOLOMBIA S.A, ADEMÁS PODRÁ GIRAR BAJO LA
    // DENOMINACIÓN BANCO DE COLOMBIA S.A." (79 caracteres) — salía cortada en "BANCO DE…". El
    // criterio del coordinador cambió con razón: ese texto NO es adorno, es lo que el RUES declara
    // como razón social, así que la casilla 19 debe mostrarlo íntegro aunque cueste cuerpo.
    //
    // Medido replicando `Fit` con las métricas reales (el resolutor mapea "Arial" a la TrueType
    // EMBEBIDA, ~11% más ancha que Helvetica: 130,1pt para "TRANSPORTES DEL NORTE S.A.S." a 7,0pt):
    // con el piso en 7,0 el fitter se queda en 7,00pt / 3 líneas y trunca; bajándolo, aterriza en
    // 6,60pt / 3 líneas COMPLETAS. Basta 6,5, pero el piso queda en 6,0 a propósito: es el suelo
    // deliberado —el campo más pequeño del formulario (`traffic_secretary_name`) está a 6,5, y por
    // debajo de 6 el FUR deja de resistir un escaneo—, y da margen a razones sociales aún más largas
    // antes de rendirse a la elipsis.
    //
    // CORRECCIÓN dentro de la misma tanda: bajar el piso SÍ encogía casos que ya cabían, al contrario
    // de lo que decía este comentario. `Fit` elige el mayor cuerpo que quepa EN UNA LÍNEA antes de
    // partir, así que ampliar el rango de encogido le daba de comer nombres medianos: medido con la
    // fuente embebida, "DISTRIBUIDORA NACIONAL DE CARGA" pasaba de 7,60pt / 2 líneas a 6,10pt / 1, y
    // "INVERSIONES EL PORVENIR S.A.S." de 7,60 / 2L a 6,60 / 1L — un 20% menos de cuerpo en un
    // recuadro con sitio para tres renglones, lo contrario de lo que buscaba la tanda. El piso se
    // queda en 6,0 y quien acota es `FurTextFitter.MaxSingleLineShrinkRatio`: el encogido en una sola
    // línea se detiene en lo cosmético y de ahí en adelante parte el texto, de modo que el piso bajo
    // solo lo gastan los nombres que ni partidos caben. Fijado en `FurCasilla19FitTests` con la
    // fuente real (caso corto en 1 línea, mediano partido a cuerpo casi declarado, largo entero).
    //
    // SEXTA TANDA (2026-08-12) — casilla 23 (`observations`) recalibrada: `w` 403,1 → 392 y cuerpo
    // 7,2 → 6,5. El recuadro se estaba llenando hasta el filo. Medido con la fuente embebida, el caso
    // reportado —"Cambio de color: ABANO BLANCO. Servicio: PÚBLICO. Empresa vinculadora: BANCOLOMBIA
    // S.A.S, NIT 890903938."— rendía una primera línea de 396,1 pt contra un ancho declarado de
    // 403,1: el auto-encaje la daba por buena (cabía, según el manifiesto) y no encogía nada, pero al
    // imprimir el texto tocaba la línea vertical del recuadro. El problema no era el fitter sino que
    // el campo no declaraba NINGÚN margen respecto al borde dibujado; bajar solo el cuerpo lo habría
    // disimulado para este texto y dejado el filo intacto para el siguiente. Con 392 hay ~11 pt de
    // aire y con 6,5 el caso real entra holgado (el alto de 33 pt admite 4 renglones a ese cuerpo, y
    // se usan 2). 6,5 es además el cuerpo de los manifiestos de observaciones de los otros formatos,
    // así que la casilla queda alineada con ellos. Fijado en `FurCasilla23FitTests` con la fuente
    // real: ninguna línea alcanza el borde, el texto sale entero y el margen no se puede volver a
    // perder sin que el test lo diga.
    //
    // Regenerada 2026-08-19 (HU #11640) para CORREGIR un descuadre que esta misma línea base había
    // congelado. `requested_process_11`/`_12` estaban en y=170,9. La rejilla "3. TRÁMITE SOLICITADO"
    // del blank AUTOMOTOR es de 6 columnas × 3 filas, y sus bordes horizontales vectoriales están en
    // y=116,1 / 141,9 / 168,6 / 195,4: y=170,9 cae en la FILA 3, donde el formulario imprime «17 CAMBIO
    // DE CARROCERÍA» y «18 OTROS». Es decir, toda constitución de prenda se estampaba como cambio de
    // carrocería y todo levantamiento como «otros». El descuadre entró en la recalibración automática
    // por anclas (MLS) del blank oficial (2026-07-24) y se propagó a `_12` al derivarlo por offset
    // desde `_11` (HU #11257): ninguna de las dos veces se contrastó contra el rótulo impreso, y al
    // regenerar esta línea base el error quedó fijado como esperado. Las columnas (x) siempre
    // estuvieron bien; solo cambia y → 150,2, que centra la tinta en la FILA 2 (celdas «11 INSCRIPC.
    // PRENDA» y «12 LEVANTA PRENDA»): con size 10,1 y cuerpo 10 negrita, la X rinde en y 150,4–160,9,
    // holgada dentro de 141,9–168,6. Verificado generando el PDF con `tools/fur-preview` y midiendo la
    // tinta resultante con PyMuPDF, no por cálculo.
    //
    // MAQUINARIA y REMOLQUES se verificaron con el mismo método y NO estaban afectados: sus casillas
    // de prenda caen en la celda correcta de su propio formulario. Solo AUTOMOTOR estaba descuadrado,
    // que es el formato de la inmensa mayoría de los trámites.
    //
    // Regenerada 2026-08-19 (HU #11641) al declarar las casillas de subtrámite simultáneo
    // `requested_process_5` (CAMBIO DE COLOR), `_17` (CAMBIO DE CARROCERÍA) y `_18` (OTROS, donde por
    // decisión de negocio se marca el cambio de combustible). Situadas por relación con las casillas
    // de prenda, ya ancladas al blank en la HU #11640, y verificadas contra los trazos impresos en
    // FurGeometriaCasillasTests: comparten columna con 11/12 y ocupan la fila inmediatamente anterior
    // o posterior. La casilla 6 (CAMBIO DE SERVICIO) NO se declara: no hay dato que la alimente (ver
    // FurFieldMapper.MarkTramite).
    //
    // Regenerada 2026-08-21 para recalibrar el numeral 20 "DATOS DE ALERTA", declarado el mismo día
    // con coordenadas heredadas de una zona equivocada: las cuatro casillas caían en y=343 y x=406..530,
    // que en el blank AUTOMOTOR es la banda de los numerales 17-18 (una fila por encima del numeral 20 y
    // corrida a la derecha), y `alert_data_code_5` escribía el acreedor en x=595,6 — dentro del recuadro
    // de la casilla 19 "EMPRESA VINCULADORA". Medido sobre los trazos Y los rótulos impresos del propio
    // blank: la fila del numeral 20 va de y=393,6 a y=435,6 y sus columnas son 370,7 | 398,2 | 448,5 |
    // 479,2 | 507,1 | 561,1 (HURTO, LIM. PROPIEDAD, EMBARGO, OTRO y A FAVOR DE). Cada X queda centrada en
    // su columna y en y=411,5, entre el rótulo (línea base 408,7) y el dígito identificador de la casilla
    // (línea base 425,8), sin tocar ninguno de los dos.
    //
    // `alert_data_code_5` pasa de `text` a `multiline` con `autoFit`: su columna mide ~54 pt de ancho
    // —el campo más estrecho del formulario en el que se escribe un nombre propio— y el hueco libre bajo
    // el rótulo es alto, así que la razón social del acreedor tiene que crecer HACIA ABAJO en vez de a lo
    // ancho. El cuerpo baja de 7,2 a 4,2 (los 3 pt que pidió el coordinador) con piso en 3,4, que es lo
    // que permite entrar entera una razón social bancaria de ~35 caracteres en 2-3 renglones.
    //
    // Regenerar SOLO de forma deliberada vía EmitBaseline tras recalibrar el manifest.
    private const string Baseline = """
        traffic_secretary_name=Text:512,64,248,20,5.5,Left,False,null
        traffic_secretary_city=Text:488,89,58,11.9,5.5,Left,False,null
        traffic_secretary_code=Text:540,89,55,11.8,5.5,Left,False,null
        processing_day=Text:593.4,90.2,25.7,12.4,7.7,Center,False,null
        processing_month=Text:619.4,90.2,27.3,12.4,7.7,Center,False,null
        processing_year=Text:649.3,90.2,30.4,12.4,7.7,Center,False,null
        plate_letter=Text:704.7,85.5,26.2,14,9.7,Center,False,null
        plate_number=Text:734.1,85.5,23.7,14,9.7,Center,False,null
        requested_process_1=cb:71.3,119.2,9.9
        requested_process_2=cb:119.5,121.1,9.8
        requested_process_3=cb:167.7,121.1,10.1
        requested_process_4=cb:215.9,121.1,10.1
        requested_process_5=cb:286.9,124,10.1
        requested_process_7=cb:71.3,150.2,10.1
        requested_process_8=cb:119.5,150.2,10.1
        requested_process_10=cb:215.9,150.2,10.1
        requested_process_17=cb:286.9,177,10.1
        requested_process_18=cb:343.3,177,10.1
        requested_process_11=cb:286.9,150.2,10.1
        requested_process_12=cb:343.3,150.2,10.1
        requested_process_13=cb:71.3,177,10.1
        requested_process_15=cb:167.7,177,10.1
        requested_process_16=cb:215.9,177,10.1
        vehicle_class_AUTOMOVIL=cb:56.5,226.4,8
        vehicle_class_BUS=cb:101.9,226.4,8
        vehicle_class_BUSETA=cb:147.3,226.4,8
        vehicle_class_CAMION=cb:189.2,226.4,8
        vehicle_class_CAMIONETA=cb:234.5,225.5,8
        vehicle_class_CAMPERO=cb:278.4,226.4,8
        vehicle_class_MICROBUS=cb:333.3,226.4,8
        vehicle_class_TRACTOCAMION=cb:52.2,248.9,8
        vehicle_class_MOTOCICLETA=cb:101.2,248.9,8
        vehicle_class_MOTOCARRO=cb:146.3,248.9,8
        vehicle_class_MOTOTRICICLO=cb:188.9,248.9,8
        vehicle_class_CUATRIMOTO=cb:230.2,248.9,8
        vehicle_class_VOLQUETA=cb:276.8,248.9,8
        vehicle_class_OTRO=cb:332.9,248.9,8
        vehicle_brand=Text:380.8,129.3,70.2,13.3,7.8,Left,False,null
        vehicle_line=Text:461.9,128.4,70,13,7.7,Left,False,null
        vehicle_colors=Text:380.5,157.5,229,13.7,7.5,Left,False,null
        vehicle_model=Text:624.7,156.7,49.9,14.2,7.6,Left,False,null
        vehicle_displacement=Text:683.9,155.5,65,14,7.6,Left,False,null
        vehicle_capacity=Text:378,178.5,70,14,7.6,Left,False,null
        vehicle_fuel_type_1=cb:551.8,133,8
        vehicle_fuel_type_2=cb:580.5,133,8
        vehicle_fuel_type_3=cb:607.4,133,8
        vehicle_fuel_type_4=cb:634.2,133,8
        vehicle_fuel_type_5=cb:661,133,8
        vehicle_fuel_type_6=cb:689.8,133,8
        vehicle_fuel_type_7=cb:716.6,133,8
        vehicle_fuel_type_8=cb:743.4,133,8
        is_armored_vehicle_yes=cb:515.7,174.3,5
        is_armored_vehicle_no=cb:538,174.3,5
        is_dismantling_armor_no=cb:669,172,8
        vehicle_bodywork_type=Text:382.3,245,176.7,16.7,7.8,Left,False,null
        vehicle_engine_number=Text:572.7,226.3,134.4,13.2,7.8,Left,False,null
        vehicle_chassis_number=Text:572.8,248.3,134.7,16.8,7.8,Left,False,null
        importacion_manifest=cb:382.8,309.6,10.1
        importacion_declaracion=cb:404.4,309.7,10.1
        remate_acta=cb:431.3,310.1,10
        remate_entidad=cb:460.7,310.1,10.1
        remate_lugar=cb:488.8,309.7,10.3
        vehicle_service_type_1=cb:577.3,366.6,10
        vehicle_service_type_2=cb:600.9,366.2,9.9
        vehicle_service_type_3=cb:624.3,366.4,9.9
        vehicle_service_type_4=cb:658.2,368,10.2
        vehicle_service_type_5=cb:687.9,368.1,9.8
        vehicle_service_type_6=cb:726.5,368.5,9.8
        linked_company_name=Text:572,411,130.5,27,7.6,Left,False,6
        linked_company_nit=Text:705.3,411,48,14,7.6,Left,False,null
        vehicle_serial_number=Text:570,286.5,124.7,14.5,7.8,Left,False,null
        vehicle_vin_number=Text:569.1,313.7,124.5,14.5,7.8,Left,False,null
        alert_data_code_1=cb:380.8,411.5,8
        alert_data_code_2=cb:419.7,411.5,8
        alert_data_code_3=cb:460.2,411.5,8
        alert_data_code_4=cb:489.5,411.5,8
        alert_data_code_5=Multiline:509.5,411.5,50,23,4.2,Left,True,3.4
        vehicle_owner_first_last_name=Text:35,301.8,128.4,14.3,7.7,Left,False,null
        vehicle_owner_second_last_name=Text:140,301.3,114,14.4,7.8,Left,False,null
        vehicle_owner_name=Text:258,303,108,20,7,Left,False,5.5
        vehicle_owner_document_type_c=cb:40.7,338.2,8
        vehicle_owner_document_type_nit=cb:63,338.2,8
        vehicle_owner_document_type_nn=cb:96,338.2,8
        vehicle_owner_document_type_p=cb:125.6,338.2,8
        vehicle_owner_document_type_ce=cb:161.4,338.2,8
        vehicle_owner_document_type_ti=cb:200.1,338.2,8
        vehicle_owner_document_type_nuip=cb:244,338.2,8
        vehicle_owner_document_type_cd=cb:286.9,338.2,8
        vehicle_owner_document_number=Multiline:318,332.1,43,26,6.5,Left,True,4.5
        vehicle_owner_address=Text:36,364.6,152,14,7.6,Left,False,null
        vehicle_owner_city=Text:196,363.8,50,14,7.7,Left,False,null
        vehicle_owner_phone=Text:317,364.9,44.9,14.2,7.7,Left,False,null
        vehicle_owner_signature=Multiline:102,378,262,32,6.5,Left,False,null
        vehicle_buyer_first_last_name=Text:33.5,445.5,127.3,14,7.6,Left,False,null
        vehicle_buyer_second_last_name=Text:141,445.2,114,14.1,7.7,Left,False,null
        vehicle_buyer_name=Text:258,446,108,20,7,Left,False,5.5
        vehicle_buyer_document_type_c=cb:41.2,481.4,8
        vehicle_buyer_document_type_nit=cb:64.7,481.4,8
        vehicle_buyer_document_type_nn=cb:99.6,481.4,8
        vehicle_buyer_document_type_p=cb:129.2,481.4,8
        vehicle_buyer_document_type_ce=cb:162.2,481.4,8
        vehicle_buyer_document_type_ti=cb:198.1,481.4,8
        vehicle_buyer_document_type_nuip=cb:239.5,481.4,8
        vehicle_buyer_document_type_cd=cb:284.6,481.4,8
        vehicle_buyer_document_number=Multiline:314.8,476,42.9,24,6.5,Left,True,4.5
        vehicle_buyer_address=Text:36,508.7,152,14.1,7.7,Left,False,null
        vehicle_buyer_city=Text:196,508.7,50,14.1,7.7,Left,False,null
        vehicle_buyer_phone=Text:314.8,509.3,44.9,14.1,7.7,Left,False,null
        vehicle_buyer_signature=Multiline:102,522,262,32,8,Left,False,null
        observations=Multiline:382,470,365,35,6.5,Left,True,5
        """;

    // Línea base MAQUINARIA. Regenerada 2026-09-01: alineación visual (OT, placa, trámite,
    // clase, marca/línea/colores/modelo, motor/VIN, combustible, propietario/comprador).
    // Los IDs `requested_process_11`/`_12` siguen siendo semánticos (constitución/levantamiento):
    // en esta plantilla el rótulo impreso "10"/"11" es prenda, no el número del id.
    private const string BaselineMaquinaria = """
        traffic_secretary_name=Text:658,44,230,22,5.5,Left,False,null
        traffic_secretary_city=Text:632,69,60,11,5.5,Left,False,null
        traffic_secretary_code=Text:699,69,64,11,5.5,Left,False,null
        processing_day=Text:769.5,71.5,27,7,7,Center,False,null
        processing_month=Text:802.5,71.5,27,7,7,Center,False,null
        processing_year=Text:835.5,71.5,29,7,7,Center,False,null
        plate_letter=Text:900,60,28,12,9,Center,False,null
        plate_number=Text:935,60,30,12,9,Center,False,null
        requested_process_1=cb:120.8,101.9,8
        requested_process_2=cb:182.6,101.9,8
        requested_process_11=cb:325.6,125,8
        requested_process_12=cb:389.6,125,8
        vehicle_class_AGRICOLA=cb:98,211,8
        vehicle_class_INDUSTRIAL=cb:195,211,8
        vehicle_class_CONSTRUCCION=cb:315,211,8
        vehicle_class_OTROS=cb:431,211,8
        vehicle_brand=Text:498,105,58,12,7,Left,False,null
        vehicle_line=Text:564,105,61,12,7,Left,False,null
        vehicle_colors=Text:502,135,258,12,7,Left,False,null
        vehicle_model=Text:780,135,60,12,7,Left,False,null
        vehicle_length=Text:498,162,90,12,7,Left,False,null
        vehicle_width=Text:595,162,130,12,7,Left,False,null
        vehicle_height=Text:737,162,60,12,7,Left,False,null
        vehicle_axles=Text:782,250,50,12,7,Left,False,null
        vehicle_traction_llantas=cb:726.7,109.4,8
        vehicle_traction_orugas=cb:795.2,109.4,8
        vehicle_traction_cilindros=cb:861.5,109.4,8
        vehicle_traction_otros=cb:930.1,109.4,8
        vehicle_cabin_cerrada=cb:507.5,218.5,8
        vehicle_cabin_parasol=cb:575.2,218.5,8
        vehicle_cabin_sin=cb:640,218.5,8
        vehicle_cabin_otros=cb:696.8,218.5,8
        vehicle_fuel_maq_1=cb:751.1,292.3,8
        vehicle_fuel_maq_2=cb:822.1,292.3,8
        vehicle_fuel_maq_3=cb:901.9,292.3,8
        vehicle_fuel_maq_4=cb:758.6,345.9,8
        vehicle_fuel_maq_5=cb:823.1,345.9,8
        vehicle_fuel_maq_6=cb:907.3,345.9,8
        vehicle_engine_number=Text:738,205,148,12,7,Left,False,null
        vehicle_vin_number=Text:738,228,228,12,7,Left,False,null
        vehicle_owner_first_last_name=Text:88,294,150,14,7,Left,False,null
        vehicle_owner_second_last_name=Text:242,294,148,14,7,Left,False,null
        vehicle_owner_name=Text:394,292,92,20,7,Left,False,5.5
        vehicle_owner_document_type_c=cb:93.5,324.5,8
        vehicle_owner_document_type_nit=cb:126.5,324.5,8
        vehicle_owner_document_type_nn=cb:158.5,324.5,8
        vehicle_owner_document_type_p=cb:192,324.5,8
        vehicle_owner_document_type_ce=cb:233,324.5,8
        vehicle_owner_document_type_ti=cb:275,324.5,8
        vehicle_owner_document_type_nuip=cb:321.5,324.5,8
        vehicle_owner_document_type_cd=cb:379,324.5,8
        vehicle_owner_document_number=Multiline:426,323,58,24,6.5,Left,True,4.5
        vehicle_owner_address=Text:88,348,188,13,7,Left,False,null
        vehicle_owner_city=Text:286,348,118,13,7,Left,False,null
        vehicle_owner_phone=Text:418,348,66,13,7,Left,False,null
        vehicle_owner_signature=Multiline:88,376,398,21,6.5,Center,False,null
        vehicle_buyer_first_last_name=Text:88,445,150,14,7,Left,False,null
        vehicle_buyer_second_last_name=Text:242,445,148,14,7,Left,False,null
        vehicle_buyer_name=Text:394,443,92,20,7,Left,False,5.5
        vehicle_buyer_document_type_c=cb:93.5,487,8
        vehicle_buyer_document_type_nit=cb:126.5,487,8
        vehicle_buyer_document_type_nn=cb:158.5,487,8
        vehicle_buyer_document_type_p=cb:192,487,8
        vehicle_buyer_document_type_ce=cb:233,487,8
        vehicle_buyer_document_type_ti=cb:275,487,8
        vehicle_buyer_document_type_nuip=cb:321.5,487,8
        vehicle_buyer_document_type_cd=cb:379,487,8
        vehicle_buyer_document_number=Multiline:426,484,58,24,6.5,Left,True,4.5
        vehicle_buyer_address=Text:88,508,188,13,7,Left,False,null
        vehicle_buyer_city=Text:286,508,118,13,7,Left,False,null
        vehicle_buyer_phone=Text:418,508,66,13,7,Left,False,null
        vehicle_buyer_signature=Multiline:88,536,398,23,6.5,Center,False,null
        alert_data_code_1=cb:521.5,411.5,8
        alert_data_code_2=cb:605,411.5,8
        alert_data_code_3=cb:689,411.5,8
        alert_data_code_4=cb:758,411.5,8
        alert_data_code_5=Text:848,404,140,18,5.5,Left,False,4
        observations=Multiline:498,453,468,40,6.5,Left,True,5
        """;

    // Línea base REMOLQUES. Regenerada 2026-09-01: misma tanda de alineación visual que
    // maquinaria, coordenadas propias. Plantilla oficial intacta. Automotor no se toca.
    private const string BaselineRemolques = """
        traffic_secretary_name=Text:662,44,226,22,5.5,Left,False,null
        traffic_secretary_city=Text:635,69,60,11,5.5,Left,False,null
        traffic_secretary_code=Text:702,69,64,11,5.5,Left,False,null
        processing_day=Text:772.5,71.5,27,7,7,Center,False,null
        processing_month=Text:805.5,71.5,27,7,7,Center,False,null
        processing_year=Text:838.5,71.5,29,7,7,Center,False,null
        plate_letter=Text:903,58,28,12,9,Center,False,null
        plate_number=Text:936,58,30,12,9,Center,False,null
        requested_process_1=cb:120.8,101.9,8
        requested_process_2=cb:182.6,101.9,8
        requested_process_10=cb:332.1,120.9,8
        requested_process_11=cb:394.4,125,8
        requested_process_12=cb:460.1,128.9,8
        vehicle_class_REMOLQUE=cb:96,211,8
        vehicle_class_SEMIREMOLQUE=cb:186,211,8
        vehicle_class_MULTIMODULAR=cb:317,211,8
        vehicle_class_SIMILAR=cb:433,211,8
        vehicle_brand=Text:502,99,154,12,7,Left,False,null
        vehicle_line=Text:670,99,156,12,7,Left,False,null
        vehicle_colors=Text:502,133,124,12,7,Left,False,null
        vehicle_model=Text:637,133,58,12,7,Left,False,null
        vehicle_axles=Text:838,99,80,12,7,Left,False,null
        vehicle_height=Text:702,133,66,12,7,Left,False,null
        vehicle_length=Text:772,133,96,12,7,Left,False,null
        vehicle_width=Text:875,133,90,12,7,Left,False,null
        vehicle_serial_number=Text:739,203,228,12,7,Left,False,null
        vehicle_vin_number=Text:739,229,228,12,7,Left,False,null
        vehicle_owner_first_last_name=Text:88,294,150,14,7,Left,False,null
        vehicle_owner_second_last_name=Text:242,294,148,14,7,Left,False,null
        vehicle_owner_name=Text:394,292,92,20,7,Left,False,5.5
        vehicle_owner_document_type_c=cb:93.5,324.5,8
        vehicle_owner_document_type_nit=cb:126.5,324.5,8
        vehicle_owner_document_type_nn=cb:158.5,324.5,8
        vehicle_owner_document_type_p=cb:192,324.5,8
        vehicle_owner_document_type_ce=cb:233,324.5,8
        vehicle_owner_document_type_ti=cb:278,324.5,8
        vehicle_owner_document_type_nuip=cb:328,324.5,8
        vehicle_owner_document_type_cd=cb:384,324.5,8
        vehicle_owner_document_number=Multiline:428,322,58,24,6.5,Left,True,4.5
        vehicle_owner_address=Text:88,349,188,13,7,Left,False,null
        vehicle_owner_city=Text:286,349,118,13,7,Left,False,null
        vehicle_owner_phone=Text:421,349,66,13,7,Left,False,null
        vehicle_owner_signature=Multiline:90,376,398,21,6.5,Center,False,null
        vehicle_buyer_first_last_name=Text:88,445,150,14,7,Left,False,null
        vehicle_buyer_second_last_name=Text:242,445,148,14,7,Left,False,null
        vehicle_buyer_name=Text:394,443,92,20,7,Left,False,5.5
        vehicle_buyer_document_type_c=cb:93.5,487,8
        vehicle_buyer_document_type_nit=cb:126.5,487,8
        vehicle_buyer_document_type_nn=cb:158.5,487,8
        vehicle_buyer_document_type_p=cb:192,487,8
        vehicle_buyer_document_type_ce=cb:233,487,8
        vehicle_buyer_document_type_ti=cb:278,487,8
        vehicle_buyer_document_type_nuip=cb:328,487,8
        vehicle_buyer_document_type_cd=cb:384,487,8
        vehicle_buyer_document_number=Multiline:428,483,58,24,6.5,Left,True,4.5
        vehicle_buyer_address=Text:88,509,188,13,7,Left,False,null
        vehicle_buyer_city=Text:286,509,118,13,7,Left,False,null
        vehicle_buyer_phone=Text:421,509,66,13,7,Left,False,null
        vehicle_buyer_signature=Multiline:90,536,398,23,6.5,Center,False,null
        alert_data_code_1=cb:524.5,370,8
        alert_data_code_2=cb:608,370,8
        alert_data_code_3=cb:692,370,8
        alert_data_code_4=cb:761,370,8
        alert_data_code_5=Text:850,362,140,18,5.5,Left,False,4
        observations=Multiline:500,420,478,70,6.5,Left,True,5
        """;

    private static string BaselineFor(FurTemplateFormat format) => format switch
    {
        FurTemplateFormat.Maquinaria => BaselineMaquinaria,
        FurTemplateFormat.Remolques => BaselineRemolques,
        _ => Baseline,
    };
}
