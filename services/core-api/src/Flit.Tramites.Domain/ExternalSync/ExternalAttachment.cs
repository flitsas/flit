using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Domain.ExternalSync;

/// <summary>
/// HU #13263 (Feature #13261, Épica #12741) — reglas del envío de adjuntos del cliente externo
/// (<c>POST /api/v1/external/tramites/{id}/adjuntos</c>, contrato v3.2 §7). La lista de tipos, los tipos MIME y los
/// estados que aceptan el envío son del cliente externo: NO se derivan de las reglas de carga del gestor.
/// </summary>
public static class ExternalAttachmentRules
{
    /// <summary>Proveedor con el que quedan marcados los adjuntos enviados por el cliente externo (Flito).</summary>
    public const string Provider = "flito";

    /// <summary><c>procedure_instance_field_values.source</c> de la marca de pago puesta por el consumidor.</summary>
    public const string FieldSource = "flito";

    /// <summary>Tipo documental que acepta la v3.2. Ampliar la lista no cambia el endpoint.</summary>
    public const string LiquidacionImpuesto = "liquidacion_impuesto";

    /// <summary>20 MB (20 971 520 bytes), como el contrato.</summary>
    public const long MaxSizeBytes = 20L * 1024 * 1024;

    public static readonly IReadOnlySet<string> AllowedTipos =
        new HashSet<string>(StringComparer.Ordinal) { LiquidacionImpuesto };

    public static readonly IReadOnlySet<string> AllowedMimeTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf", "image/jpeg", "image/png", "image/webp" };

    /// <summary>
    /// Valida lo que se puede saber sin tocar la base: devuelve el código de error del contrato o <c>null</c>.
    /// Orden: falta de archivo, tipo, MIME, tamaño (el mismo del gestor).
    /// </summary>
    public static string? Validate(string? tipo, string? mimeType, long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return "missing_file";
        }

        if (tipo is null || !AllowedTipos.Contains(tipo))
        {
            return "invalid_tipo";
        }

        if (string.IsNullOrWhiteSpace(mimeType) || !AllowedMimeTypes.Contains(mimeType))
        {
            return "invalid_mime";
        }

        return sizeBytes > MaxSizeBytes ? "file_too_large" : null;
    }

    /// <summary>Solo el tipo MIME, sin parámetros (<c>; charset=…</c>) y en minúscula.</summary>
    public static string NormalizeMimeType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        var end = contentType.IndexOf(';', StringComparison.Ordinal);
        return (end >= 0 ? contentType[..end] : contentType).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// ¿El trámite admite el envío en este estado? Aceptan <c>preasignacion</c>, <c>asignado</c>, <c>entregado</c> y
    /// <c>rechazado</c> con subsanación activa. <c>Terminal</c> responde «¿el trámite ya no volverá a un estado que
    /// acepte el envío?»: <c>aprobado</c>, <c>anulado</c> y <c>revocado</c> sí; <c>borrador</c>, <c>preparado</c> y
    /// <c>rechazado</c> sin subsanación pueden volver.
    /// </summary>
    public static (bool Allowed, bool Terminal) EvaluateState(string? status, bool subsanacionActiva)
    {
        if (Is(status, TramiteEstado.Preasignacion) || Is(status, TramiteEstado.Asignado) || Is(status, TramiteEstado.Entregado)
            || (Is(status, TramiteEstado.Rechazado) && subsanacionActiva))
        {
            return (true, false);
        }

        var terminal = Is(status, TramiteEstado.Aprobado) || Is(status, TramiteEstado.Anulado) || Is(status, TramiteEstado.Revocado);
        return (false, terminal);
    }

    private static bool Is(string? status, string expected) =>
        string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Un adjunto vigente del tipo, tal como lo ve la decisión del envío.</summary>
public sealed record ExternalAttachmentExisting(Guid Id, string? Provider, string Sha256, string StoragePath)
{
    public bool IsFromExternalClient => string.Equals(Provider, ExternalAttachmentRules.Provider, StringComparison.OrdinalIgnoreCase);
}

/// <summary>El trámite sobre el que se envía, leído con su fila bloqueada.</summary>
/// <param name="PagadoMarcado">
/// Marca VIGENTE del consumidor: <c>impuesto_departamental_pagado = true</c> con <c>source = flito</c>. En esta HU solo se lee.
/// </param>
/// <param name="Vigentes">Adjuntos vigentes del tipo enviado, el más reciente primero.</param>
public sealed record ExternalAttachmentTarget(
    Guid ProcedureId,
    Guid TenantId,
    string Status,
    bool SubsanacionActiva,
    Guid ProcedureTypeId,
    Guid? TransitOfficeId,
    bool PagadoMarcado,
    IReadOnlyList<ExternalAttachmentExisting> Vigentes);

/// <summary>El adjunto a archivar (el archivo ya está en el almacenamiento).</summary>
public sealed record NewExternalAttachment(string Tipo, string Filename, string Mimetype, long SizeBytes, string Sha256, string StoragePath);

/// <summary>Escritura dentro de la transacción que bloquea el trámite.</summary>
public interface IExternalAttachmentWriter
{
    /// <summary>
    /// Inserta el adjunto (<c>Source = user</c>, <c>Provider = flito</c>) y retira <paramref name="retire"/> con el mismo
    /// borrado de fila que usa el gestor al reemplazar. Devuelve el id del adjunto nuevo.
    /// </summary>
    Task<Guid> ReplaceAsync(NewExternalAttachment attachment, IReadOnlyCollection<Guid> retire, CancellationToken cancellationToken);
}

/// <summary>
/// HU #13263 — acceso a datos del envío de adjuntos externo. Es el único componente que escribe adjuntos para el cliente
/// externo: resuelve el trámite con el mismo alcance que el feed (radicado alguna vez, no migrado, no eliminado), bloquea
/// su fila y ejecuta el trabajo en UNA transacción, de modo que dos envíos concurrentes al mismo trámite se serializan y
/// nunca dejan dos adjuntos vigentes de Flito.
/// </summary>
public interface IExternalAttachmentRepository
{
    /// <param name="work">
    /// Recibe <c>null</c> si el trámite no existe o está fuera de alcance. Si termina sin excepción la transacción se
    /// confirma; si lanza, se revierte.
    /// </param>
    Task<T> RunLockedAsync<T>(
        Guid procedureId,
        string tipo,
        Func<ExternalAttachmentTarget?, IExternalAttachmentWriter, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
