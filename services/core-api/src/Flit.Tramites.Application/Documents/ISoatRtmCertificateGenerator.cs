namespace Flit.Tramites.Application.Documents;

/// <summary>Datos de un ramo (SOAT o RTM) para el certificado de vigencia (HU #10856). Campos nulos → en blanco.</summary>
public sealed record SoatRtmBlock(
    string? Poliza = null,
    string? FechaVigencia = null,
    string? FechaVencimiento = null,
    string? FechaExpedicion = null,
    string? Entidad = null,
    string? Estado = null);

/// <summary>Una fila de la tabla de avalúo: tipo (FASECOLDA/COMERCIAL) y valor. Nulos → en blanco.</summary>
public sealed record AvaluoRow(string? Tipo, string? Valor);

/// <summary>Bloque de avalúo del certificado (solo traspaso, HU #10856): una fila por fuente de avalúo.</summary>
public sealed record AvaluoInfo(IReadOnlyList<AvaluoRow> Rows);

/// <summary>
/// Datos del "Certificado de vigencia SOAT Y RTM" (HU #10856, punto 6). Un único documento con los
/// bloques SOAT y RTM y, solo en traspaso, el bloque de Avalúo. Cualquier valor que no vino en la
/// consulta se deja en blanco (regla del negocio: no rellenar con marcadores).
/// </summary>
public sealed record SoatRtmCertificateData(
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string? Placa,
    string? FechaConsulta,
    SoatRtmBlock Soat,
    // RTM: null oculta la tabla (p. ej. matrícula inicial no tiene revisión técnico-mecánica).
    SoatRtmBlock? Rtm,
    AvaluoInfo? Avaluo,
    // HU #11305 (ADR-0041) — pie de procedencia por bloque: quién dijo el dato y cuándo.
    // El texto fijo del certificado afirma una consulta al RUNT que puede no haber ocurrido: hay
    // celdas que salen del OCR de un PDF cargado por el operador. Nulo ⇒ no se pinta pie.
    string? FuenteSoat = null,
    string? FuenteRtm = null);

/// <summary>
/// Genera el "Certificado de vigencia SOAT Y RTM" (HU #10856) como PDF real con membrete FLIT, para
/// fusionarse en el expediente consolidado (tipo <c>certificado_soat_rtm</c>). Implementación en
/// Infrastructure.
/// </summary>
public interface ISoatRtmCertificateGenerator
{
    GeneratedDocument GenerateSoatRtmCertificate(SoatRtmCertificateData data);
}
