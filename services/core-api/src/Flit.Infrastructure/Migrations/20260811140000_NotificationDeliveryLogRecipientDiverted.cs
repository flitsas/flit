using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11364 (Feature #11348) — columna <c>admin.notification_delivery_logs.recipient_diverted</c>:
/// marca de que el envío se desvió (destinatario real sustituido por uno de desvío antes de enviar,
/// fuera de producción). El destinatario ORIGINAL ya queda en la fila (columna <c>recipient</c>,
/// escrita por la HU #11363 desde <c>EmailMessage</c> antes de que el adaptador lo sustituya); esta
/// columna es únicamente la marca que faltaba (AC2).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260811140000_NotificationDeliveryLogRecipientDiverted")]
public partial class NotificationDeliveryLogRecipientDiverted : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("68-notification-delivery-log-recipient-diverted.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.notification_delivery_logs
              DROP COLUMN IF EXISTS recipient_diverted;
            """);
    }
}
