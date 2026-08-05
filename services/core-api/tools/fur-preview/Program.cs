using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;

var coreApiRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var outDir = Path.Combine(coreApiRoot, "artifacts", "fur-analysis");
Directory.CreateDirectory(outDir);

var generator = new FurOverlayDocumentGenerator();

// HU #11255 — textos de observación cortos/medios/largos usados por los escenarios 05-09.
const string ObsCorta = "SIN OBSERVACIONES ADICIONALES.";

const string ObsMedia =
    "VEHICULO VERIFICADO CONTRA EL RUNT SIN NOVEDADES. SE ADJUNTA SOAT Y RTM VIGENTES A LA FECHA " +
    "DE RADICACION DEL TRAMITE PARA CONSTANCIA DEL ORGANISMO DE TRANSITO.";

const string ObsLarga =
    "VEHICULO VERIFICADO CONTRA EL RUNT SIN NOVEDADES REPORTADAS A LA FECHA DE RADICACION. SE " +
    "ADJUNTA SOAT Y RTM VIGENTES. EL PROPIETARIO DECLARA BAJO GRAVEDAD DE JURAMENTO QUE LA " +
    "INFORMACION SUMINISTRADA EN EL PRESENTE FORMULARIO ES VERAZ Y COMPLETA, Y QUE EL VEHICULO NO " +
    "SE ENCUENTRA REPORTADO COMO HURTADO NI PRESENTA MEDIDAS CAUTELARES VIGENTES ANTE LA " +
    "AUTORIDAD COMPETENTE. CUALQUIER INCONSISTENCIA SERA INFORMADA AL ORGANISMO DE TRANSITO PARA " +
    "LOS FINES PERTINENTES DENTRO DEL TRAMITE EN CURSO.";

// HU #11256 — observación DESMEDIDA (~2.000 caracteres): fuerza el último recurso de
// `FurTextFitter.FitMultiline` (cuerpo al piso de 5 pt + truncado con elipsis). Verifica CF2/CF3: el
// texto queda dentro del recuadro, sin tinta fuera, por arriba y por abajo.
const string ObsDesmedida =
    "GRAVAMEN / PRENDA A FAVOR DE: BANCO FINANCIERO DE COLOMBIA S.A. - NIT 890900608-1. " +
    "VEHICULO VERIFICADO CONTRA EL RUNT SIN NOVEDADES REPORTADAS A LA FECHA DE RADICACION DEL " +
    "TRAMITE ANTE EL ORGANISMO DE TRANSITO COMPETENTE. SE ADJUNTA SOAT Y RTM VIGENTES A LA FECHA. " +
    "EL PROPIETARIO DECLARA BAJO GRAVEDAD DE JURAMENTO QUE LA INFORMACION SUMINISTRADA EN EL " +
    "PRESENTE FORMULARIO ES VERAZ Y COMPLETA, Y QUE EL VEHICULO NO SE ENCUENTRA REPORTADO COMO " +
    "HURTADO NI PRESENTA MEDIDAS CAUTELARES VIGENTES ANTE LA AUTORIDAD COMPETENTE. CUALQUIER " +
    "INCONSISTENCIA SERA INFORMADA AL ORGANISMO DE TRANSITO PARA LOS FINES PERTINENTES DENTRO DEL " +
    "TRAMITE EN CURSO. TRANSFORMACION REGISTRADA CONFORME ADR-0029: CAMBIO DE COLOR DE BLANCO " +
    "PERLA A NEGRO MATE, CON SOPORTE FOTOGRAFICO Y CERTIFICADO DE TALLER AUTORIZADO ADJUNTO AL " +
    "EXPEDIENTE DIGITAL DEL TRAMITE. EL GESTOR CERTIFICA HABER VERIFICADO FISICAMENTE LA " +
    "CORRESPONDENCIA ENTRE EL NUMERO DE MOTOR, EL NUMERO DE CHASIS Y LOS DATOS REGISTRADOS EN EL " +
    "SISTEMA RUNT, SIN ENCONTRAR NOVEDADES QUE IMPIDAN LA CONTINUACION DEL TRAMITE SOLICITADO POR " +
    "EL INTERESADO ANTE ESTE ORGANISMO DE TRANSITO, DE CONFORMIDAD CON LA NORMATIVIDAD VIGENTE " +
    "APLICABLE EN MATERIA DE TRANSITO Y TRANSPORTE TERRESTRE AUTOMOTOR EN EL TERRITORIO NACIONAL " +
    "COLOMBIANO, SEGUN LO ESTABLECIDO POR EL MINISTERIO DE TRANSPORTE Y LA SUPERINTENDENCIA DE " +
    "TRANSPORTE PARA ESTE TIPO DE TRAMITES DE REGISTRO AUTOMOTOR NACIONAL.";

var scenarios = new (string Slug, FurDocumentData Data)[]
{
    // Uno por formato (Feature #10918). TemplateFormat se fija EXPLÍCITO para forzar la plantilla blank
    // correcta sin depender del catálogo/BD; la clase del vehículo es la real que mapearía a ese formato.
    ("01-automotor-camioneta", AutomotorData()),
    ("02-maquinaria-retroexcavadora", MaquinariaData()),
    ("03-remolques-semirremolque", RemolquesData()),
    ("04-automotor-traspaso", AutomotorTraspasoData()),

    // HU #11255 — escenarios con observations/vehicle_serial_number con contenido real, para medir
    // con pymupdf el desplazamiento (-2,-5) de observations y (0,-5) de vehicle_serial_number.
    ("05-automotor-obs-corta", AutomotorData() with
    {
        Vehiculo = AutomotorData().Vehiculo with { NumeroSerie = "SN-AUTO-000001" },
        Observaciones = ObsCorta,
    }),
    ("06-automotor-obs-media", AutomotorData() with
    {
        Vehiculo = AutomotorData().Vehiculo with { NumeroSerie = "SN-AUTO-000002" },
        Observaciones = ObsMedia,
    }),
    ("07-automotor-obs-larga", AutomotorData() with
    {
        Vehiculo = AutomotorData().Vehiculo with { NumeroSerie = "SN-AUTO-000003" },
        Observaciones = ObsLarga,
    }),
    // Maquinaria NO tiene casilla vehicle_serial_number en el manifest (CF10/AC3): aunque el dato
    // venga poblado (como en MaquinariaData()), no debe imprimirse nada en esa zona.
    ("08-maquinaria-obs-media", MaquinariaData() with { Observaciones = ObsMedia }),
    ("09-remolques-obs-media", RemolquesData() with
    {
        Vehiculo = RemolquesData().Vehiculo with { NumeroSerie = "SN-REM-000004" },
        Observaciones = ObsMedia,
    }),

    // HU #11256 — observación DESMEDIDA (~2.000 car.) en los tres formatos: CF2/CF3, último recurso
    // de FitMultiline (piso 5 pt + truncado con elipsis, sin tinta fuera del recuadro).
    ("10-automotor-obs-desmedida", AutomotorData() with { Observaciones = ObsDesmedida }),
    ("11-maquinaria-obs-desmedida", MaquinariaData() with { Observaciones = ObsDesmedida }),
    ("12-remolques-obs-desmedida", RemolquesData() with { Observaciones = ObsDesmedida }),

    // HU #11256 (CF12) — sello "NO FIRMADO" explícito (IdentidadValidada=false) en `vehicle_owner_signature`,
    // multiline sin autoFit: debe salir idéntico antes/después en los tres formatos.
    ("13-automotor-no-firmado", AutomotorData() with { IdentidadValidada = false }),
    ("14-maquinaria-no-firmado", MaquinariaData() with { IdentidadValidada = false }),
    ("15-remolques-no-firmado", RemolquesData() with { IdentidadValidada = false }),
};

foreach (var (slug, data) in scenarios)
{
    var doc = generator.GenerateFur(data);
    var path = Path.Combine(outDir, $"fur-preview-{slug}.pdf");
    await File.WriteAllBytesAsync(path, doc.Content);
    Console.WriteLine($"OK {path} ({doc.Content.Length:N0} bytes)");
}

return 0;

// AUTOMOTOR — matrícula de una camioneta (plantilla histórica HU #10256).
static FurDocumentData AutomotorData() => new(
    ProcedureInstanceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
    ReferenceNumber: "TRM-2026-YYY090",
    Modalidad: "matricula_inicial",
    TipologiaCodigo: "matricula_inicial",
    Vehiculo: new VehiculoDatos(
        Marca: "TESLA",
        Linea: "MODELO Y",
        Modelo: "2026",
        Color: "BLANCO PERLA",
        Clase: "CAMIONETA",
        Combustible: "GASOLINA",
        Cilindraje: "0",
        Vin: "LRWYGCFJ7TC495717",
        Placa: "YYY090",
        NumeroMotor: "TM-495717",
        NumeroChasis: "LRWYGCFJ7TC495717",
        TipoCarroceria: "SUV",
        TipoServicio: "PARTICULAR",
        Capacidad: "5"),
    Organismo: new OrganismoTransito("25286000", "STRIATTOYTTE MCPAL FUNDA", "FUNDA"),
    Partes:
    [
        new DocumentParte(
            "comprador",
            "DANIEL AMADO GARCIA",
            "1193552679",
            "daniel@example.com",
            DocumentType: "CC",
            Phone: "3001234567",
            Address: "CALLE 1 # 2-3",
            City: "FUNZA"),
    ],
    ValorVenta: null,
    Causal: null,
    SellosFirma: [],
    FechaTramite: new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc),
    TemplateFormat: FurTemplateFormat.Automotor);

// AUTOMOTOR traspaso — vendedor (propietario) + comprador con sellos de identidad, para verificar
// las secciones 21/22 y las firmas en la plantilla nueva (HU #10921).
static FurDocumentData AutomotorTraspasoData() => AutomotorData() with
{
    ReferenceNumber = "TRM-2026-IWL38D",
    Modalidad = "traspaso",
    TipologiaCodigo = "traspaso_standard",
    Vehiculo = AutomotorData().Vehiculo with
    {
        Marca = "BAJAJ", Linea = "PULSAR 200", Modelo = "2023", Color = "NEGRO",
        Clase = "MOTOCICLETA", Combustible = "GASOLINA", Cilindraje = "200",
        Placa = "IWL38D", Vin = "MD2BRYDZ8NWC12345", NumeroChasis = "MD2BRYDZ8NWC12345",
        TipoCarroceria = "SIN CARROCERIA", Capacidad = "2",
    },
    Partes =
    [
        new DocumentParte("vendedor", "AMOR JIMENEZ GUERRA", "1000445459", null, DocumentType: "CC",
            Phone: "3109876543", Address: "CRA 10 # 20-30", City: "BOGOTA"),
        new DocumentParte("comprador", "STEFFEN REICHERT", "C27WKYL7", null, DocumentType: "PAS",
            Phone: "3201112233", Address: "AV 68 # 45-12", City: "MEDELLIN"),
    ],
    IdentidadValidada = true,
    SellosIdentidad = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["vendedor"] = "Validacion biometrica CC 1000445459\nUUID ven-001\nFirma a1b2c3\nAprob 25/06/2026 · Vence 25/07/2026",
        ["comprador"] = "Validacion biometrica PAS C27WKYL7\nUUID com-002\nFirma d4e5f6\nAprob 25/06/2026 · Vence 25/07/2026",
    },
};

// MAQUINARIA — matrícula de una retroexcavadora (plantilla MAQUINARIA, HU #10922).
static FurDocumentData MaquinariaData() => AutomotorData() with
{
    ReferenceNumber = "TRM-2026-MAQ001",
    Vehiculo = AutomotorData().Vehiculo with
    {
        Marca = "CATERPILLAR",
        Linea = "420F2",
        Modelo = "2024",
        Color = "AMARILLO",
        Clase = "RETROEXCAVADORA",
        Combustible = "DIESEL",
        Cilindraje = "4400",
        Vin = "CAT0420FLKMH12345",
        Placa = "MAQ001",
        NumeroMotor = "MT-CAT420F2",
        NumeroChasis = "CAT0420FLKMH12345",
        NumeroSerie = "SN-420F2-9981",
        TipoCarroceria = "SIN CARROCERIA",
        TipoServicio = "PARTICULAR",
        Capacidad = "1",
        PesoBruto = "8200",
    },
    Partes =
    [
        new DocumentParte(
            "comprador",
            "CONSTRUCCIONES ANDINAS S.A.S.",
            "9007654321",
            "compras@construandinas.com",
            DocumentType: "NIT",
            Phone: "6041234567",
            Address: "CRA 43A # 14-27",
            City: "MEDELLIN",
            EsJuridica: true),
    ],
    TemplateFormat = FurTemplateFormat.Maquinaria,
};

// REMOLQUES — traspaso de un semirremolque (plantilla REMOLQUES, HU #10923).
static FurDocumentData RemolquesData() => AutomotorData() with
{
    ReferenceNumber = "TRM-2026-REM001",
    Modalidad = "traspaso",
    TipologiaCodigo = "traspaso_standard",
    Vehiculo = AutomotorData().Vehiculo with
    {
        Marca = "PLANATRAILER",
        Linea = "PORTACONTENEDOR 40FT",
        Modelo = "2022",
        Color = "GRIS",
        Clase = "SEMIREMOLQUE",
        Combustible = "NO APLICA",
        Cilindraje = "0",
        Vin = "3H3V532C1NT123456",
        Placa = "R12345",
        NumeroMotor = "-",
        NumeroChasis = "3H3V532C1NT123456",
        TipoCarroceria = "PLATAFORMA",
        TipoServicio = "CARGA",
        Capacidad = "0",
        PesoBruto = "34000",
        NumeroEjes = "3",
    },
    Partes =
    [
        new DocumentParte(
            "vendedor",
            "TRANSPORTES DEL NORTE LTDA",
            "8301112223",
            "flota@transnorte.com",
            DocumentType: "NIT",
            Phone: "6057778899",
            Address: "VIA 40 # 72-15",
            City: "BARRANQUILLA",
            EsJuridica: true),
        new DocumentParte(
            "comprador",
            "LOGISTICA CARIBE S.A.S.",
            "9012223334",
            "activos@logcaribe.com",
            DocumentType: "NIT",
            Phone: "6053334455",
            Address: "CALLE 30 # 8-40",
            City: "CARTAGENA",
            EsJuridica: true),
    ],
    TemplateFormat = FurTemplateFormat.Remolques,
};
