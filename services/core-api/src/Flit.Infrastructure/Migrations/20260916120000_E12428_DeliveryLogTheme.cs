using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12428 (Feature #12405, Épica #12237 Marca Blanca, ADR-0060) — traza del tema de correo
/// aplicado a cada entrega: <c>theme_kind</c>, <c>theme_version</c> en
/// <c>admin.notification_delivery_logs</c> (AC5). Añade también <c>sender_name</c>/<c>sender_email</c>
/// que consume la HU #12430 (mismo DDL 117, ambas historias comparten el ALTER porque son columnas
/// del mismo hecho de negocio: "qué se aplicó a este envío"). DDL en
/// <c>117-HU12428-notification-delivery-theme.sql</c>, que es la fuente de verdad; esta migración solo
/// lo ejecuta. Columnas nullable — sin backfill de filas anteriores (AC5: "correos ya enviados no
/// cambian"). La entidad <c>NotificationDeliveryLogEntity</c> sigue <c>ExcludeFromMigrations</c> (DDL
/// gestionado a mano desde la HU #11357); esto solo suma columnas a una tabla ya existente.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260916120000_E12428_DeliveryLogTheme")]
public partial class E12428_DeliveryLogTheme : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("117-HU12428-notification-delivery-theme.sql"));

    /// <inheritdoc />
    /// <remarks>Reversible sin pérdida para nadie: las cuatro columnas nacen nullable y sin backfill.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE admin.notification_delivery_logs
                DROP CONSTRAINT IF EXISTS ck_notification_delivery_logs_theme_kind;

            ALTER TABLE admin.notification_delivery_logs
                DROP COLUMN IF EXISTS sender_email,
                DROP COLUMN IF EXISTS sender_name,
                DROP COLUMN IF EXISTS theme_version,
                DROP COLUMN IF EXISTS theme_kind;
            """);
}
