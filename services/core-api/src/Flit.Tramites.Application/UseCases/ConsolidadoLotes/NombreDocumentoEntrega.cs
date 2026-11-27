using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #14736 (Feature #14733, Épica #14657) — nombre con el que se ENTREGA (lista/descarga) un documento
/// generado por FLIT: <c>{tipo}_{radicado}_{placa}.pdf</c> con la placa ACTUAL del trámite
/// (<see cref="NombreDocumentoTramite.Generar(string, ProcedureInstance)"/>), aunque el adjunto se guardara con otro nombre.
/// No renombra el binario ni reescribe la base: se calcula al entregar. Función pura, aditiva y sin acceso a
/// datos: no amplía ni relaja ninguna autorización.
/// <para><b>Qué se renombra</b> (<see cref="EsGeneradoPorFlit(ProcedureInstanceAttachment)"/>): PDF de
/// <c>Source = "system"</c>, salvo <see cref="TiposQueNoSeRenombran"/>; y los adjuntos <c>rues</c>/<c>impronta</c>
/// generados por FLIT aunque su Source sea <c>user</c> (ver heurística). Lo subido por un usuario/OT/ICT/compañía,
/// los no-PDF y la descarga masiva (<see cref="ConsolidadoLoteNombres"/>) conservan su nombre.</para>
/// <para><b>Versiones históricas</b>: solo se calcula el nombre del adjunto vigente de su tipo
/// (<see cref="EsVigenteDeSuTipo"/>); un <c>IsHistorico</c> o un duplicado más antiguo del mismo tipo conserva su
/// nombre guardado, para que dos adjuntos del mismo tipo no se muestren con el mismo nombre.</para>
/// </summary>
/// <remarks>
/// <code>
/// var nombre = NombreDocumentoEntrega.ParaAdjunto(adjunto, instancia); // fur_FT1-0000102_AAA111.pdf
/// </code>
/// </remarks>
public static class NombreDocumentoEntrega
{
    /// <summary>
    /// Tipos con <c>Source = "system"</c> que NO son documentos generados por FLIT sino la copia de un documento real
    /// (la escritura pública de la compañía, FurCommand: <c>escritura</c> y <c>escritura_comprador</c>): conservan
    /// su nombre. Los documentos personalizados de compañía ya llevan <c>Source = "company"</c> y no se renombran.
    /// </summary>
    public static readonly IReadOnlySet<string> TiposQueNoSeRenombran =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "escritura", "escritura_comprador" };

    /// <summary>
    /// Tipos que FLIT genera pero que pasan por <c>UploadAttachmentHandler</c> y por eso quedan con
    /// <c>Source = "user"</c> (certificado RUES de persona jurídica e impronta del proveedor).
    /// </summary>
    private static readonly string[] TiposGeneradosComoUser = ["rues", "impronta"];

    /// <summary>¿Es un documento generado por FLIT cuyo nombre se calcula al entregar? (sin considerar vigencia).</summary>
    /// <remarks>
    /// Heurística para <c>rues</c>/<c>impronta</c>: <c>Source = "user"</c> pero <c>Tipo</c> igual y <c>Filename</c> que
    /// empieza por <c>{tipo}_</c> y es PDF (el patrón de nombre del generador: <c>rues_{ref}.pdf</c> /
    /// <c>rues_{ref}_{placa}.pdf</c>). Límite: un usuario que suba a mano un PDF de esos tipos y lo nombre
    /// <c>rues_algo.pdf</c> será renombrado; con cualquier otro nombre se conserva. El modelo no tiene hoy una marca
    /// de «generado» (<c>Provider</c> solo lo fija Kyverum), por eso no se cambia el esquema por esto.
    /// </remarks>
    public static bool EsGeneradoPorFlit(ProcedureInstanceAttachment adjunto)
    {
        ArgumentNullException.ThrowIfNull(adjunto);
        return EsGeneradoPorFlit(adjunto.Tipo, adjunto.Source, adjunto.Mimetype, adjunto.Filename);
    }

    /// <inheritdoc cref="EsGeneradoPorFlit(ProcedureInstanceAttachment)"/>
    public static bool EsGeneradoPorFlit(string? tipo, string? source, string? mimetype, string? filename)
    {
        if (!EsPdf(mimetype, filename))
            return false;

        if (string.Equals(source, "system", StringComparison.OrdinalIgnoreCase))
            return tipo is null || !TiposQueNoSeRenombran.Contains(tipo);

        return string.Equals(source, "user", StringComparison.OrdinalIgnoreCase)
               && EsPdfGeneradoComoUser(tipo, filename);
    }

    private static bool EsPdf(string? mimetype, string? filename)
    {
        if (string.Equals(mimetype?.Trim(), "application/pdf", StringComparison.OrdinalIgnoreCase))
            return true;

        // Sin mimetype fiable: el nombre guardado manda (los generadores siempre terminan en .pdf).
        return string.IsNullOrWhiteSpace(mimetype)
               && filename?.TrimEnd().EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool EsPdfGeneradoComoUser(string? tipo, string? filename) =>
        !string.IsNullOrWhiteSpace(tipo)
        && !string.IsNullOrWhiteSpace(filename)
        && TiposGeneradosComoUser.Contains(tipo, StringComparer.OrdinalIgnoreCase)
        && filename.TrimStart().StartsWith(tipo + "_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ¿Es el adjunto vigente de su tipo? No si es una versión histórica, ni si hay otro adjunto no histórico del mismo
    /// tipo más reciente (<c>UploadedAt</c>, desempate por <c>Id</c>). Evita dos nombres iguales para el mismo tipo.
    /// Tras una revocatoria (todos históricos) los adjuntos conservan su nombre guardado.
    /// </summary>
    public static bool EsVigenteDeSuTipo(ProcedureInstanceAttachment adjunto, ProcedureInstance instancia)
    {
        ArgumentNullException.ThrowIfNull(adjunto);
        ArgumentNullException.ThrowIfNull(instancia);

        if (adjunto.IsHistorico)
            return false;

        return !instancia.Attachments.Any(o =>
            !ReferenceEquals(o, adjunto)
            && o.Id != adjunto.Id
            && !o.IsHistorico
            && string.Equals(o.Tipo, adjunto.Tipo, StringComparison.OrdinalIgnoreCase)
            && (o.UploadedAt > adjunto.UploadedAt
                || (o.UploadedAt == adjunto.UploadedAt && o.Id.CompareTo(adjunto.Id) > 0)));
    }

    /// <summary>
    /// Nombre a entregar para <paramref name="adjunto"/> de <paramref name="instancia"/>: el calculado si lo
    /// generó FLIT y es el vigente de su tipo (placa de <see cref="NombreDocumentoTramite.PlacaDe"/>, <c>SIN-PLACA</c>
    /// si no hay); si no, el guardado tal cual.
    /// </summary>
    public static string ParaAdjunto(ProcedureInstanceAttachment adjunto, ProcedureInstance instancia)
    {
        ArgumentNullException.ThrowIfNull(adjunto);
        ArgumentNullException.ThrowIfNull(instancia);

        return EsGeneradoPorFlit(adjunto) && EsVigenteDeSuTipo(adjunto, instancia)
            ? NombreDocumentoTramite.Generar(adjunto.Tipo, instancia)
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
