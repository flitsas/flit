using System.Globalization;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances.ManualCapture;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Estado de una de las 4 imágenes (contrato <c>ManualDetail.images</c>). Nunca lleva ruta ni URL.</summary>
public sealed record ManualImageInfo(string Kind, bool Available);

/// <summary>
/// HU #13297 (Feature #13282 C) — contrato <c>ManualDetail</c>: <c>ManualListItem</c> + constancia de consentimiento, las 4
/// imágenes (solo si existen en el ciclo ACTUAL), la revisión y el vencimiento del enlace de captura.
/// <para>
/// Tras un rechazo (HU #13299) la validación queda en <c>rechazado</c> con <c>rejectionReasonCode</c>/<c>reviewedAt</c>/
/// <c>reviewedBy</c>, las imágenes rechazadas (siguen hasta que llegue la captura nueva) y <c>linkExpiresAt</c> del enlace nuevo
/// con el que el cliente repite la captura. Sin captura aún (<c>manual_activo</c>) todas las imágenes traen
/// <c>available = false</c>.
/// </para>
/// </summary>
public sealed record ManualDetail(
    Guid Id,
    string FullName,
    string DocumentNumber,
    string TenantName,
    string Origin,
    string Status,
    DateTimeOffset? ActivatedAt,
    int? WaitingMinutes,
    DateTimeOffset? ConsentAt,
    string? ConsentTextVersion,
    IReadOnlyList<ManualImageInfo> Images,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy,
    string? RejectionReasonCode,
    DateTimeOffset? LinkExpiresAt);

/// <summary>Consulta del detalle: <paramref name="ConsultedByUserId"/> es quien lo pidió (queda en la auditoría).</summary>
public sealed record GetManualDetailQuery(Guid ValidationId, Guid ConsultedByUserId);

/// <summary>
/// Detalle de una validación manual de cualquier compañía. No comprueba el rol: el endpoint (solo Super Admin) lo exige antes.
/// Cada consulta queda auditada con la etapa <c>manual_imagenes_consultadas</c> (<c>recurso=detalle</c>), sin PII ni rutas.
/// <para>Errores: <c>not_found</c> (404).</para>
/// </summary>
public sealed class GetManualDetailHandler(
    IManualIdentityReviewReadRepository repo,
    IIdentityValidationAuditLog audit,
    TimeProvider? clock = null)
{
    public const string NoEncontrada = "not_found";

    public async Task<(ManualDetail? Result, string? Error)> HandleAsync(GetManualDetailQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var r = await repo.GetDetailAsync(query.ValidationId, ct).ConfigureAwait(false);
        if (r is null)
            return (null, NoEncontrada);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.ManualImagenesConsultadas, IdentityValidationAuditOutcomes.Ok,
            TenantId: r.TenantId, ProcedureInstanceId: r.ProcedureInstanceId, ValidationId: r.Id, PartyRole: r.PartyRole,
            Message: $"El usuario {query.ConsultedByUserId} consultó el detalle de la validación manual.",
            Detail: $"usuario={query.ConsultedByUserId}; recurso=detalle"), ct).ConfigureAwait(false);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        IReadOnlyList<ManualImageInfo> images =
        [
            new(ManualImageKinds.Rostro, r.HasRostro),
            new(ManualImageKinds.Anverso, r.HasAnverso),
            new(ManualImageKinds.Reverso, r.HasReverso),
            new(ManualImageKinds.Firma, r.HasFirma),
        ];

        return (new ManualDetail(
            r.Id, r.FullName, r.DocumentNumber, r.TenantName, r.Origin, r.Status, r.ActivatedAt,
            ListManualIdentityValidationsHandler.WaitingMinutes(r.Status, r.WaitingSince, now),
            r.ConsentAt, r.ConsentTextVersion, images, r.ReviewedAt, r.ReviewedBy, r.RejectionReasonCode, r.LinkExpiresAt), null);
    }
}

/// <summary>Consulta de una imagen: <paramref name="Kind"/> ∈ <see cref="ManualImageKinds.Todos"/>.</summary>
public sealed record GetManualImageQuery(Guid ValidationId, string Kind, Guid ConsultedByUserId);

/// <summary>Contenido de la imagen y su Content-Type real (detectado por los bytes, no por la extensión).</summary>
public sealed record ManualImageContent(byte[] Content, string ContentType);

/// <summary>
/// Entrega una imagen del ciclo ACTUAL de una validación manual desde <see cref="IAttachmentStorage"/>. Nunca devuelve una URL
/// ni la ruta del storage: el endpoint autenticado entrega los bytes con <c>Cache-Control: no-store</c>. Audita cada entrega con
/// <c>manual_imagenes_consultadas</c> (<c>recurso=imagen:&lt;kind&gt;</c>, sin rutas ni PII). El Super Admin ve imágenes de TODAS
/// las compañías; el rol lo exige el endpoint.
/// <para>Errores: <c>not_found</c> (404): kind distinto de los 4, validación inexistente o sin esa imagen en el ciclo actual.</para>
/// </summary>
public sealed class GetManualImageHandler(
    IManualIdentityReviewReadRepository repo,
    IAttachmentStorage storage,
    IIdentityValidationAuditLog audit)
{
    public const string NoEncontrada = "not_found";

    /// <summary>Tope de lectura (la firma y las fotos se aceptan hasta <see cref="ManualCaptureImages.MaxImageBytes"/>).</summary>
    private const long MaxBytes = ManualCaptureImages.MaxImageBytes;

    public async Task<(ManualImageContent? Result, string? Error)> HandleAsync(GetManualImageQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var kind = query.Kind?.Trim().ToLowerInvariant();
        if (!ManualImageKinds.IsValid(kind))
            return (null, NoEncontrada);

        var imageRef = await repo.GetImageRefAsync(query.ValidationId, kind!, ct).ConfigureAwait(false);
        if (imageRef?.StoragePath is not { } path)
            return (null, NoEncontrada);

        var bytes = await ReadAsync(path, ct).ConfigureAwait(false);
        if (bytes is null)
            return (null, NoEncontrada);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.ManualImagenesConsultadas, IdentityValidationAuditOutcomes.Ok,
            TenantId: imageRef.TenantId, ProcedureInstanceId: imageRef.ProcedureInstanceId, ValidationId: imageRef.ValidationId,
            PartyRole: imageRef.PartyRole,
            Message: $"El usuario {query.ConsultedByUserId} consultó una imagen de la validación manual.",
            Detail: string.Create(CultureInfo.InvariantCulture, $"usuario={query.ConsultedByUserId}; recurso=imagen:{kind}")), ct)
            .ConfigureAwait(false);

        return (new ManualImageContent(bytes, ManualCaptureImages.ContentType(ManualCaptureImages.Detect(bytes))), null);
    }

    /// <summary>Lee el archivo acotado; <c>null</c> si el binario no existe en el storage, está vacío o excede el máximo.</summary>
    private async Task<byte[]?> ReadAsync(string path, CancellationToken ct)
    {
        var stream = await storage.OpenReadAsync(path, ct).ConfigureAwait(false);
        if (stream is null)
            return null;

        await using (stream.ConfigureAwait(false))
        {
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (ms.Length + read > MaxBytes)
                    return null;
                ms.Write(buffer, 0, read);
            }

            return ms.Length == 0 ? null : ms.ToArray();
        }
    }
}
