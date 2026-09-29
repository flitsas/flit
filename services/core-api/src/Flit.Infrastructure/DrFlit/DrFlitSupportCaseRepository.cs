using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.SupportCases;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// <c>dr_flit.support_cases</c> por SQL directo (HU #12925), sin entidad EF como el resto del schema
/// <c>dr_flit</c>. Cada método es una sola sentencia: el intento queda registrado aunque el proceso
/// muera entre la inserción y la respuesta del proveedor.
/// </summary>
internal sealed class DrFlitSupportCaseRepository(FlitDbContext db) : IDrFlitSupportCaseRepository
{
    public async Task<Guid> InsertPendingAsync(DrFlitSupportCaseRecord record, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        var t = record.Ticket;
        var environment = t.Environment.ToString();
        var priority = t.Priority.ToString();
        var incidence = t.Frequency.ToWire();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dr_flit.support_cases
                (id, tenant_id, created_by_user_id, status, ado_project, title, environment, affected_module,
                 priority, incidence, requester_name, requester_email, requester_phone, requester_company,
                 problem_detail, expected_result, created_by, updated_by)
            VALUES
                ({id}, {record.TenantId}, {record.UserId}, 'pending', {record.AdoProject}, {t.Title}, {environment}, {record.AffectedModule},
                 {priority}, {incidence}, {t.RequesterName}, {t.RequesterEmail}, {t.RequesterPhone}, {t.Company},
                 {t.Detail}, {t.ExpectedResult}, {record.UserId}, {record.UserId})
            """, ct).ConfigureAwait(false);

        return id;
    }

    public Task MarkCreatedAsync(Guid id, int workItemId, int attachmentCount, int attachmentFailures, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dr_flit.support_cases
               SET status = 'created', ado_work_item_id = {workItemId}, last_error = NULL,
                   attachment_count = {attachmentCount}, attachment_upload_failures = {attachmentFailures}, updated_at = now()
             WHERE id = {id}
            """, ct);

    public Task MarkFailedAsync(Guid id, string errorCode, int attachmentCount, int attachmentFailures, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dr_flit.support_cases
               SET status = 'failed', last_error = {errorCode},
                   attachment_count = {attachmentCount}, attachment_upload_failures = {attachmentFailures}, updated_at = now()
             WHERE id = {id}
            """, ct);
}
