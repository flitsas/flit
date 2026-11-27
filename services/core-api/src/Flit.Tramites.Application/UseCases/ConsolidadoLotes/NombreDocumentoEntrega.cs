using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #14736 (Feature #14733, Épica #14657) — nombre con el que se ENTREGA (lista/descarga) un documento
/// generado por FLIT: <c>{tipo}_{radicado}_{placa}.pdf</c> con la placa ACTUAL del trámite
/// (<see cref="NombreDocumentoTramite.Generar"/>), aunque el adjunto se guardara con otro nombre.
/// No renombra el binario ni reescribe la base: se calcula al entregar. Solo cambian los adjuntos
/// generados por el sistema (<c>Source = "system"</c>) que son PDF; lo subido por un usuario/OT/ICT/compañía,
/// los no-PDF y la descarga masiva (<see cref="ConsolidadoLoteNombres"/>) conservan su nombre.
/// Función pura, aditiva y sin acceso a datos: no amplía ni relaja ninguna autorización.
/// </summary>
/// <remarks>
/// <code>
/// var nombre = NombreDocumentoEntrega.ParaAdjunto(adjunto, instancia); // fur_FT1-0000102_AAA111.pdf
/// </code>
/// </remarks>
public static class NombreDocumentoEntrega
{
    /// <summary>¿Es un documento generado por FLIT cuyo nombre se calcula al entregar?</summary>
    public static bool EsGeneradoPorFlit(string? source, string? mimetype, string? filename)
    {
        if (!string.Equals(source, "system", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(mimetype?.Trim(), "application/pdf", StringComparison.OrdinalIgnoreCase))
            return true;

        // Sin mimetype fiable: el nombre guardado manda (los generadores siempre terminan en .pdf).
        return string.IsNullOrWhiteSpace(mimetype)
               && filename?.TrimEnd().EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Nombre a entregar para <paramref name="adjunto"/> de <paramref name="instancia"/>: el calculado si lo
    /// generó FLIT (placa vigente de <see cref="ProcedureInstance.Plate"/>, <c>SIN-PLACA</c> si no hay);
    /// si no, el guardado tal cual.
    /// </summary>
    public static string ParaAdjunto(ProcedureInstanceAttachment adjunto, ProcedureInstance instancia)
    {
        ArgumentNullException.ThrowIfNull(adjunto);
        ArgumentNullException.ThrowIfNull(instancia);

        return EsGeneradoPorFlit(adjunto.Source, adjunto.Mimetype, adjunto.Filename)
            ? NombreDocumentoTramite.Generar(adjunto.Tipo, instancia.ReferenceNumber, instancia.Plate)
            : adjunto.Filename;
    }

    /// <summary>Proyección <see cref="ConsolidadoDocumentDto"/> con el nombre de entrega.</summary>
    public static ConsolidadoDocumentDto ParaDocumento(ProcedureInstanceAttachment adjunto, ProcedureInstance instancia) =>
        new(adjunto.Id, adjunto.Tipo, ParaAdjunto(adjunto, instancia), adjunto.Sha256);

    /// <summary>
    /// Re-nombra el documento de un resultado ya calculado (consolidado/maestro) buscando su adjunto en
    /// <paramref name="instancia"/>; si el adjunto no está cargado o no es generado, devuelve el DTO intacto.
    /// </summary>
    public static ConsolidadoDocumentDto ParaDocumento(ConsolidadoDocumentDto dto, ProcedureInstance instancia)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(instancia);

        var adjunto = instancia.Attachments.FirstOrDefault(a => a.Id == dto.AttachmentId);
        return adjunto is null ? dto : dto with { Filename = ParaAdjunto(adjunto, instancia) };
    }
}
