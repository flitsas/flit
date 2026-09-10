using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11357 (Feature #11348) — backfill 1 de 2: traslada al campo propio
/// (<c>personalized_documents_enabled</c>) la elegibilidad de documentos personalizados que hoy vive
/// implícita en <c>notification_channel = 'tenant_api'</c>.
/// </summary>
/// <remarks>
/// Solo enciende; nunca apaga. Un tenant en <c>flit_smtp</c> se queda en <c>false</c> por el default
/// de la columna, no porque este script lo apague. Idempotente (condición sobre el estado destino),
/// así que re-ejecutarlo no degrada lo que un administrador haya encendido después.
///
/// <para>Corre entre <c>20260811120000_NotificationDeliveryLog</c> (crea la columna) y
/// <c>20260811120200_BackfillNormalizeNotificationChannel</c> (borra la señal que aquí se lee). El
/// orden lo garantiza el timestamp de la migración.</para>
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20260811120100_BackfillPersonalizedDocumentsEnabled")]
public partial class BackfillPersonalizedDocumentsEnabled : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("65-backfill-personalized-documents-enabled.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deshacer el traslado es apagar el interruptor de quien seguía en 'tenant_api' — el estado
        // exacto anterior al Up. No se apaga a quien ya esté en 'flit_smtp': ese true, si existe, lo
        // puso una mano después del backfill y no es de este script.
        migrationBuilder.Sql(
            """
            UPDATE admin.tenant_operational_policies
               SET personalized_documents_enabled = false
             WHERE notification_channel = 'tenant_api'
               AND personalized_documents_enabled = true;
            """);
    }
}
