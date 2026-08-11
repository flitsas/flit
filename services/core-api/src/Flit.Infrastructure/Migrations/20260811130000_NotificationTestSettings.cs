using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11365 (Feature #11349) — tabla <c>admin.notification_test_settings</c>: configuración de fila
/// única del banco de pruebas de notificaciones (buzón de pruebas + marca del último envío de
/// prueba), sembrada con su única fila y ambos campos en <c>NULL</c>.
/// </summary>
/// <remarks>
/// Solo esquema: ni endpoints (HU #11366) ni envío de prueba/límite de frecuencia (HU #11368). La
/// marca vive en BD, y no en memoria de proceso, para que el límite sobreviva a un reinicio y valga
/// para todas las réplicas.
///
/// <para>La tabla NO lleva <c>tenant_id</c> ni RLS a propósito: el buzón de pruebas es global de
/// plataforma (lo gestiona el SuperAdmin), igual que <c>admin.quipux_settings</c>. El razonamiento
/// completo está en el encabezado del DDL, donde lo verá quien revise el esquema.</para>
///
/// <para>El DDL va en crudo (índice único sobre expresión constante, trigger de <c>row_version</c>,
/// comentarios PII) y es idempotente en sus dos mitades: creación y sembrado. Reaplicarlo deja una
/// sola fila con los mismos valores.</para>
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20260811130000_NotificationTestSettings")]
public partial class NotificationTestSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("67-HU11365-notification-test-settings.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Los triggers y el índice se van con la tabla; se nombran igual por claridad del rollback.
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS tr_notification_test_settings_audit
              ON admin.notification_test_settings;
            DROP TRIGGER IF EXISTS tr_notification_test_settings_row_version
              ON admin.notification_test_settings;
            DROP TABLE IF EXISTS admin.notification_test_settings;
            """);
    }
}
