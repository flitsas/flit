using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11357 (Feature #11348) — tabla <c>admin.notification_delivery_logs</c> (bitácora append-only
/// de envíos de notificación) + columna
/// <c>admin.tenant_operational_policies.personalized_documents_enabled</c> (interruptor propio de
/// documentos personalizados). Solo esquema: ni escritura de bitácora (HU #11363) ni consumo del
/// campo (HU #11362).
/// </summary>
/// <remarks>
/// <b>Debe estar aplicada ANTES de desplegar la HU #11362.</b> Si el enrutamiento sale primero, el
/// guard deja de leer <c>notification_channel</c> y el tenant que hoy está en <c>tenant_api</c>
/// pierde sus documentos personalizados: la elegibilidad aún no se habría trasladado al campo propio
/// (backfill <c>20260811120100_BackfillPersonalizedDocumentsEnabled</c>).
///
/// <para>El DDL va en crudo (RLS, comentarios PII, CHECKs) y es idempotente. La bitácora no lleva
/// soft delete, <c>row_version</c> ni trigger de auditoría, igual que su vecina
/// <c>admin.ot_api_call_logs</c>: es append-only y ya <i>es</i> la evidencia del envío.</para>
///
/// <para>Sobre la tensión con el ADR-0042 (que niega el booleano paralelo) la resolución documental
/// es el ADR-0043, entregable de la HU #11362; aquí solo queda la remisión en el DDL y en el
/// <c>COMMENT ON COLUMN</c>.</para>
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20260811120000_NotificationDeliveryLog")]
public partial class NotificationDeliveryLog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("64-notification-delivery-log.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.tenant_operational_policies
              DROP COLUMN IF EXISTS personalized_documents_enabled;
            DROP TABLE IF EXISTS admin.notification_delivery_logs;
            """);
    }
}
