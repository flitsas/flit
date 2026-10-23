using Flit.DrFlit.Application.Abstractions;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Storage;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Adjuntos previos de los casos de soporte (HU #12924). El binario va al mismo almacenamiento que los
/// adjuntos de trámites (<see cref="IAttachmentStorage"/>, file-manager), etiquetado
/// <see cref="StorageTag"/>; la metadata a <c>dr_flit.support_case_attachments</c> por SQL directo (sin
/// entidad EF, como el resto del schema <c>dr_flit</c>).
/// </summary>
internal sealed class DrFlitSupportAttachmentStore(FlitDbContext db, IAttachmentStorage storage) : IDrFlitSupportAttachmentStore
{
    /// <summary>Etiqueta del archivo en el almacenamiento: separa estos adjuntos de los de trámites.</summary>
    public const string StorageTag = "dr-flit-support";

    public async Task<DrFlitStoredAttachment> SaveAsync(
        Guid tenantId, Guid userId, string fileName, string contentType, Stream content, DateTimeOffset expiresAt, CancellationToken ct)
    {
        // El id se genera aquí para usarlo también como referencia del archivo en el almacenamiento: el
        // parámetro "procedureInstanceId" de IAttachmentStorage es metadata libre del file-manager.
        var id = Guid.CreateVersion7();
        var stored = await storage.SaveAsync(id, StorageTag, fileName, content, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dr_flit.support_case_attachments
                (id, tenant_id, uploaded_by_user_id, storage_path, filename, content_type, size_bytes, sha256, expires_at, created_by, updated_by)
            VALUES
                ({id}, {tenantId}, {userId}, {stored.StoragePath}, {fileName}, {contentType}, {stored.SizeBytes}, {stored.Sha256}, {expiresAt}, {userId}, {userId})
            """, ct).ConfigureAwait(false);

        return new DrFlitStoredAttachment(id, fileName, stored.SizeBytes);
    }

    public async Task<int> PurgeExpiredAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var expired = await db.Database
            .SqlQuery<string>($"""
                DELETE FROM dr_flit.support_case_attachments
                 WHERE tenant_id = {tenantId} AND uploaded_by_user_id = {userId}
                   AND support_case_id IS NULL AND expires_at <= {now}
                RETURNING storage_path AS "Value"
                """)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // El binario se borra después de la fila: si el borrado del almacenamiento falla o es no-op, lo
        // recupera el ciclo de vida del backend; nunca queda una fila apuntando a un archivo ausente.
        foreach (var path in expired)
            storage.Delete(path);

        return expired.Count;
    }

    public async Task<IReadOnlyList<DrFlitPendingAttachment>> GetPendingAsync(
        Guid tenantId, Guid userId, IReadOnlyCollection<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        var idArray = ids.Distinct().ToArray();
        return await db.Database
            .SqlQuery<DrFlitPendingAttachment>($"""
                -- Alias en snake_case: el contexto mapea las propiedades de DrFlitPendingAttachment con esa convención.
                SELECT id, filename AS file_name, content_type, storage_path
                  FROM dr_flit.support_case_attachments
                 WHERE id = ANY({idArray})
                   AND tenant_id = {tenantId} AND uploaded_by_user_id = {userId}
                   AND support_case_id IS NULL AND deleted_at IS NULL AND expires_at > {now}
                 ORDER BY created_at, id
                """)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task LinkToCaseAsync(IReadOnlyCollection<Guid> attachmentIds, Guid supportCaseId, CancellationToken ct)
    {
        if (attachmentIds.Count == 0)
            return;

        var idArray = attachmentIds.ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dr_flit.support_case_attachments
               SET support_case_id = {supportCaseId}, updated_at = now()
             WHERE id = ANY({idArray}) AND support_case_id IS NULL
            """, ct).ConfigureAwait(false);
    }

    public Task<Stream?> OpenAsync(string storagePath, CancellationToken ct) => storage.OpenReadAsync(storagePath, ct);
}
