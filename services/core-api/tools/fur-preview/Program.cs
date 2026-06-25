using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;

var coreApiRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var outDir = Path.Combine(coreApiRoot, "artifacts", "fur-analysis");
Directory.CreateDirectory(outDir);

var generator = new FurOverlayDocumentGenerator();

var scenarios = new (string Slug, FurDocumentData Data)[]
{
    ("matricula-YYY090", MatriculaData()),
    ("traspaso-IWL38D", TraspasoData()),
};

foreach (var (slug, data) in scenarios)
{
    var doc = generator.GenerateFur(data);
    var path = Path.Combine(outDir, $"fur-preview-{slug}.pdf");
    await File.WriteAllBytesAsync(path, doc.Content);
    Console.WriteLine($"OK {path} ({doc.Content.Length:N0} bytes)");
}

return 0;

static FurDocumentData MatriculaData() => new(
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
    FechaTramite: new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc));

static FurDocumentData TraspasoData() => MatriculaData() with
{
    ReferenceNumber = "TRM-2026-IWL38D",
    Modalidad = "traspaso",
    TipologiaCodigo = "traspaso_standard",
    Vehiculo = MatriculaData().Vehiculo with
    {
        Marca = "BAJAJ",
        Linea = "PULSAR 200 NS",
        Modelo = "2023",
        Color = "NEGRO",
        Clase = "MOTOCICLETA",
        Combustible = "GASOLINA",
        Cilindraje = "200",
        Placa = "IWL38D",
        Vin = "MD2BRYDZ8NWC12345",
        NumeroChasis = "MD2BRYDZ8NWC12345",
        TipoCarroceria = "SIN CARROCERIA",
        Capacidad = "2",
    },
    Partes =
    [
        new DocumentParte(
            "vendedor",
            "AMOR Y CERVEZA JIMENEZ GUERRA",
            "1000445459",
            "vendedor@example.com",
            DocumentType: "CC",
            Phone: "3109876543",
            Address: "CRA 10 # 20-30",
            City: "BOGOTA"),
        new DocumentParte(
            "comprador",
            "STEFFEN REICHERT",
            "C27WKYL7",
            "comprador@example.com",
            DocumentType: "PAS",
            Phone: "3201112233",
            Address: "AV 68 # 45-12",
            City: "MEDELLIN"),
    ],
};
