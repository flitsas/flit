// Render de verificación (HU #11034/#11035): mandato, solicitud virtual y FUR con sello de identidad,
// para comprobar número de páginas, caja del contenido y cuerpo del texto de firma.
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;

var vendedor = new DocumentParte(
    "vendedor",
    "COMERCIALIZADORA DE VEHICULOS DEL NORTE S.A.S.",
    "900123456-7",
    "contacto@comercializadora.com",
    "NIT",
    "6041112233",
    EsJuridica: true,
    RepresentanteLegalNombre: "MARIA FERNANDA GONZALEZ RESTREPO",
    RepresentanteLegalTipoDoc: "CC",
    RepresentanteLegalDocumento: "1038409485");

// Comprador NIT, para ver la marca del tipo de documento de la sección del comprador.
var comprador = new DocumentParte(
    "comprador",
    "INVERSIONES DEL SUR S.A.S.",
    "901555444-2",
    "compras@inversiones.co",
    "NIT",
    "3001112233",
    EsJuridica: true,
    RepresentanteLegalNombre: "JUAN ESTEBAN PEREZ",
    RepresentanteLegalTipoDoc: "CC",
    RepresentanteLegalDocumento: "1020304050");

var sellos = new Dictionary<string, string>
{
    ["vendedor"] = "Validación biométrica CC 1038409485\nUUID kv-123\nFirma ABC-XYZ\nAprob 2026/07/20 · Vence 2026/08/19",
    ["comprador"] = "Validación biométrica CC 1020304050\nUUID kv-456\nFirma DEF-UVW\nAprob 2026/07/21 · Vence 2026/08/20",
};

var data = new FurDocumentData(
    ProcedureInstanceId: Guid.NewGuid(),
    ReferenceNumber: "TRM-2026-000123",
    Modalidad: "traspaso",
    TipologiaCodigo: "traspaso_standard",
    Vehiculo: new VehiculoDatos("TOYOTA", "COROLLA", "2024", "BLANCO", "AUTOMOVIL", "PARTICULAR", "GASOLINA", "VIN123", "ABC123"),
    Organismo: new OrganismoTransito("05001000", "SECRETARIA DE MOVILIDAD DE MEDELLIN", "Medellin"),
    Partes: [vendedor, comprador],
    ValorVenta: 45000000,
    Causal: null,
    SellosFirma: [],
    SellosIdentidad: sellos);

var mandato = new MandatoPdfGenerator().GenerateMandato(
    new MandatoData(data, "generico", null, null, new MandatarioFirmante("CARLOS ANDRES RUIZ", "71234567")));
File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "mandato.pdf"), mandato.Content);

var virtual_ = new SolicitudVirtualPdfGenerator().GenerateSolicitudVirtual(data);
File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "virtual.pdf"), virtual_.Content);

var fur = new FurOverlayDocumentGenerator().GenerateFur(data);
File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "fur.pdf"), fur.Content);

// HU #11170 — variante FIRMADA CON EL BAÚL, para comprobar que la vigencia y el hash acompañan a la
// imagen en los cuatro documentos (antes solo los llevaba el FUR). Requiere una imagen de firma:
// FIRMA_PNG=<ruta a un png>. Sin la variable se omite y el render normal no cambia.
var firmaPng = Environment.GetEnvironmentVariable("FIRMA_PNG");
if (!string.IsNullOrWhiteSpace(firmaPng) && File.Exists(firmaPng))
{
    var imagen = File.ReadAllBytes(firmaPng);
    var metaVendedor = new FirmaBaulMetadata(
        "1038409485", "MARIA FERNANDA GONZALEZ RESTREPO",
        new DateOnly(2026, 1, 15), new DateOnly(2027, 1, 14), Guid.NewGuid(), "BAUL-7F3A21");
    var metaComprador = new FirmaBaulMetadata(
        "1020304050", "JUAN ESTEBAN PEREZ",
        new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), Guid.NewGuid(), "BAUL-9C55E0");

    var conBaul = data with
    {
        FirmaImagenes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["vendedor"] = imagen,
            ["comprador"] = imagen,
        },
        FirmaBaulMetadatos = new Dictionary<string, FirmaBaulMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["vendedor"] = metaVendedor,
            ["comprador"] = metaComprador,
        },
        // Bug #11146 — quien firma por el baúl no lleva además el sello de identidad.
        SellosIdentidad = null,
    };

    var mandatoBaul = new MandatoPdfGenerator().GenerateMandato(new MandatoData(
        conBaul, "generico", null, null,
        new MandatarioFirmante("CARLOS ANDRES RUIZ", "71234567", imagen, null, metaComprador)));
    File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "mandato-baul.pdf"), mandatoBaul.Content);

    var virtualBaul = new SolicitudVirtualPdfGenerator().GenerateSolicitudVirtual(conBaul);
    File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "virtual-baul.pdf"), virtualBaul.Content);

    File.WriteAllBytes(
        Path.Combine(AppContext.BaseDirectory, "compraventa-baul.pdf"),
        FurCompraventaDocumentGenerator.Generate(conBaul));

    var furBaul = new FurOverlayDocumentGenerator().GenerateFur(conBaul);
    File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "fur-baul.pdf"), furBaul.Content);
}

Console.WriteLine($"OK {AppContext.BaseDirectory}");
