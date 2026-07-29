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
    SellosIdentidad: sellos,
    FirmasVisibles: true);

var mandato = new MandatoPdfGenerator().GenerateMandato(
    new MandatoData(data, "generico", null, null, new MandatarioFirmante("CARLOS ANDRES RUIZ", "71234567")));
File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "mandato.pdf"), mandato.Content);

var virtual_ = new SolicitudVirtualPdfGenerator().GenerateSolicitudVirtual(data);
File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "virtual.pdf"), virtual_.Content);

var fur = new FurOverlayDocumentGenerator().GenerateFur(data);
File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "fur.pdf"), fur.Content);

Console.WriteLine($"OK {AppContext.BaseDirectory}");
