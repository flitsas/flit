using System.Globalization;
using System.Text.Json.Nodes;

namespace Flit.Tramites.Application.Ocr;

/// <summary>
/// MOCK: analizador OCR determinista para desarrollo/tests sin Anthropic (fixtures de desarrollo).
/// No hace IO ni llama a ningún servicio externo: devuelve un JSON de ejemplo válido por tipo
/// (<c>es_factura_valida</c>/<c>es_valido</c> = true). Es el proveedor por defecto (<c>Ocr:Provider = mock</c>);
/// se reemplaza por <c>AnthropicDocumentOcrAnalyzer</c> con <c>Ocr:Provider = anthropic</c> sin tocar los handlers.
/// </summary>
public sealed class MockDocumentOcrAnalyzer : IDocumentOcrAnalyzer
{
    private const string ObservacionMock = "Documento simulado (Ocr:Provider=mock)";

    public Task<DocumentOcrAnalysis> AnalyzeAsync(
        string tipo, ReadOnlyMemory<byte> content, string mediaType, CancellationToken ct)
    {
        var data = BuildFixture(tipo);
        return Task.FromResult(new DocumentOcrAnalysis(true, data));
    }

    private static JsonObject BuildFixture(string tipo)
    {
        var fecha = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return tipo switch
        {
            "factura" => new JsonObject
            {
                ["tipo_documento"] = "factura_electronica",
                ["es_factura_valida"] = true,
                ["paginas_documento"] = new JsonArray(1),
                ["total_paginas"] = 1,
                ["numero_factura"] = "FV-MOCK-001",
                ["fecha"] = fecha,
                ["emisor_nit"] = "900123456",
                ["emisor_nombre"] = "CONCESIONARIO MOCK S.A.S.",
                ["comprador_nombre"] = "JUAN CARLOS PEREZ GOMEZ",
                ["comprador_documento"] = "1000000001",
                ["comprador_tipo_doc"] = "CC",
                ["vehiculo_marca"] = "CHEVROLET",
                ["vehiculo_linea"] = "ONIX",
                ["vehiculo_modelo"] = "2022",
                ["vehiculo_color"] = "BLANCO",
                ["vehiculo_vin"] = "",
                ["vehiculo_motor"] = "MOCKMOTOR001",
                ["subtotal"] = 45000000,
                ["iva"] = 8550000,
                ["total"] = 53550000,
                ["observaciones"] = ObservacionMock,
            },
            "aduana" => new JsonObject
            {
                ["tipo_documento"] = "declaracion_importacion",
                ["es_valido"] = true,
                ["paginas_documento"] = new JsonArray(1),
                ["total_paginas"] = 1,
                ["numero_documento"] = "DI-MOCK-2026-001",
                ["fecha"] = fecha,
                ["aduana"] = "Aduana de Bogota",
                ["importador_nombre"] = "IMPORTADORA MOCK S.A.S.",
                ["importador_nit"] = "900654321",
                ["vehiculo_marca"] = "CHEVROLET",
                ["vehiculo_linea"] = "ONIX",
                ["vehiculo_modelo"] = "2022",
                ["vehiculo_vin"] = "",
                ["vehiculo_motor"] = "MOCKMOTOR001",
                ["valor_cif_cop"] = 53550000,
                ["observaciones"] = ObservacionMock,
            },
            "impronta" => new JsonObject
            {
                ["tipo_documento"] = "certificado_improntas",
                ["es_valido"] = true,
                ["paginas_documento"] = new JsonArray(1),
                ["total_paginas"] = 1,
                ["numero_certificado"] = "IMPR-MOCK-001",
                ["fecha"] = fecha,
                ["entidad_emisora"] = "CDA MOCK BOGOTA",
                ["vehiculo_marca"] = "CHEVROLET",
                ["vehiculo_linea"] = "ONIX",
                ["vehiculo_modelo"] = "2022",
                ["vehiculo_vin"] = "",
                ["vehiculo_motor"] = "MOCKMOTOR001",
                ["estado_motor"] = "coincide",
                ["estado_vin"] = "coincide",
                ["alertas"] = new JsonArray(),
                ["observaciones"] = ObservacionMock,
            },
            "soat" => new JsonObject
            {
                ["tipo_documento"] = "soat",
                ["es_valido"] = true,
                ["paginas_documento"] = new JsonArray(1),
                ["total_paginas"] = 1,
                ["numero_poliza"] = "SOAT-MOCK-001",
                ["aseguradora"] = "SEGUROS MOCK",
                ["fecha_inicio"] = fecha,
                ["fecha_vencimiento"] = DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["estado_poliza"] = "vigente",
                ["vehiculo_marca"] = "CHEVROLET",
                ["vehiculo_linea"] = "ONIX",
                ["vehiculo_vin"] = "",
                ["observaciones"] = ObservacionMock,
            },
            _ => new JsonObject
            {
                ["es_valido"] = true,
                ["es_factura_valida"] = true,
                ["observaciones"] = "Mock generico",
            },
        };
    }
}
