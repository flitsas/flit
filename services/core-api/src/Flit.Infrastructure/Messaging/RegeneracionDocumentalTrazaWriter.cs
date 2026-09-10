using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Messaging;

/// <summary>
/// Bug #11613 — escribe el evento <c>regeneracion_documental_fallida</c> en
/// <c>tramites.procedure_instance_events</c>.
///
/// <para>Inserta con SQL PARAMETRIZADO en vez de <c>db.Add</c> + <c>SaveChangesAsync</c> a propósito: la
/// traza se escribe justo después de un intento de regeneración fallido y el change tracker puede
/// arrastrar cambios a medio hacer de ese intento (adjuntos borrados o insertados). Un
/// <c>SaveChanges</c> ahí los volcaría a la base y convertiría un fallo best-effort en corrupción.</para>
///
/// <para><b>Aislamiento por tenant.</b> El INSERT viaja por la misma conexión que abrió el scope del
/// tenant cliente, pero la garantía NO se apoya en el RLS: en este despliegue las policies no llegan a
/// evaluarse (la aplicación es owner de las tablas y no hay <c>FORCE ROW LEVEL SECURITY</c>), así que
/// el aislamiento real es el <c>tenant_id</c> explícito. Por eso la fila se escribe con
/// <c>INSERT ... SELECT ... WHERE EXISTS</c>: solo entra si el trámite pertenece de verdad a ese
/// tenant, y un llamador que pasara el tenant equivocado (p. ej. el del OT en vez del cliente) escribe
/// CERO filas en lugar de contaminar la bitácora de otro tenant.</para>
/// </summary>
internal sealed class RegeneracionDocumentalTrazaWriter(FlitDbContext db)
    : IRegeneracionDocumentalTrazaWriter
{
    public async Task<bool> EscribirFalloAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        string origen,
        string codigoError,
        string? detalle,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || procedureInstanceId == Guid.Empty)
            return false;

        var id = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            origen,
            error = codigoError,
            detalle,
            tenant_id = tenantId,
        });

        // Proveedor no relacional (tests con InMemory): no hay SQL que ejecutar. El tracker de esos
        // tests no arrastra un intento fallido de generación, así que aquí sí es seguro usarlo.
        if (!db.Database.IsRelational())
        {
            db.Add(new ProcedureInstanceEvent
            {
                Id = id,
                TenantId = tenantId,
                ProcedureInstanceId = procedureInstanceId,
                Tipo = RegenerarDocumentosTrazadoHandler.EventoFallo,
                Payload = payload,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        // El WHERE EXISTS ata la fila al trámite Y a su tenant: sin él, un llamador que pasara otro
        // tenant escribiría en la bitácora ajena y el RLS de este despliegue no lo frenaría (policies
        // no evaluadas: app owner y sin FORCE ROW LEVEL SECURITY). Todo sigue parametrizado.
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO tramites.procedure_instance_events
                 (id, tenant_id, procedure_instance_id, tipo, payload, created_at)
             SELECT {id}, {tenantId}, {procedureInstanceId},
                    {RegenerarDocumentosTrazadoHandler.EventoFallo}, {payload}::jsonb, {now}
             WHERE EXISTS (
                 SELECT 1 FROM tramites.procedure_instances
                 WHERE id = {procedureInstanceId} AND tenant_id = {tenantId})
             """,
            cancellationToken).ConfigureAwait(false);

        return rows > 0;
    }
}
