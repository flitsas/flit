namespace Flit.Tramites.Application.Ocr;

/// <summary>
/// Puerto del registro de consumo de IA (contrato ia-consumo v1, ADR-0090) para que Application abra el ámbito de
/// negocio de una lectura por OCR sin depender del SDK de plataforma. Mientras el ámbito esté abierto, cada llamada HTTP
/// al proveedor queda registrada con este proceso, la placa y la entidad. Es ambiental: no cambia ninguna firma.
/// </summary>
public interface IIaConsumoAmbito
{
    /// <summary>Abre el ámbito del módulo <c>tramites</c>; al hacer Dispose se restaura el anterior.</summary>
    /// <param name="proceso">Proceso del catálogo del contrato (p. ej. <c>ocr_documento</c>, <c>ocr_licencia_transito</c>).</param>
    /// <param name="placa">Placa del vehículo si se conoce (se normaliza al registrar).</param>
    /// <param name="entidadTipo"><c>tramite</c> u otra entidad del contrato.</param>
    /// <param name="entidadId">Identificador de la entidad.</param>
    IDisposable Abrir(string proceso, string? placa = null, string? entidadTipo = null, string? entidadId = null);
}

/// <summary>Procesos del módulo <c>tramites</c> en el catálogo del contrato ia-consumo v1.</summary>
public static class IaProcesosTramites
{
    public const string OcrDocumento = "ocr_documento";
    public const string OcrLote = "ocr_lote";
    public const string OcrLicenciaTransito = "ocr_licencia_transito";
    public const string EntidadTramite = "tramite";
}
