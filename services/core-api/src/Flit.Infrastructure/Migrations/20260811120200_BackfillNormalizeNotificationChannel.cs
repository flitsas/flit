using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11357 (Feature #11348) — backfill 2 de 2: normaliza <c>notification_channel</c> a
/// <c>'flit_smtp'</c> en todos los tenants, deshaciendo la doble semántica del campo.
/// </summary>
/// <remarks>
/// <c>notification_channel</c> nunca enrutó un correo (Bug #11311): su único consumidor real era el
/// guard de documentos personalizados, así que <c>'tenant_api'</c> era de facto el interruptor de esa
/// funcionalidad. Cuando la HU #11362 haga que el canal enrute de verdad —con el canal de API del
/// cliente desplegado deshabilitado por configuración y la HU #11359 devolviendo «fallido por
/// configuración incompleta»—, esos tenants dejarían de recibir sus informes y alertas en silencio,
/// sin haber cambiado nada.
///
/// <para>Es seguro porque el campo hoy no hace nada, y la capacidad de documentos personalizados ya
/// quedó preservada en <c>personalized_documents_enabled</c> por
/// <c>20260811120100_BackfillPersonalizedDocumentsEnabled</c>, que corre antes por timestamp. A
/// partir de aquí un tenant está en <c>tenant_api</c> solo si alguien lo puso a propósito.</para>
///
/// <para>El <c>Down</c> reconstruye el canal desde el interruptor propio, que es exactamente la
/// señal de la que se derivó.</para>
/// </remarks>
[DbContext(typeof(FlitDbContext))]
[Migration("20260811120200_BackfillNormalizeNotificationChannel")]
public partial class BackfillNormalizeNotificationChannel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("66-backfill-normalize-notification-channel.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE admin.tenant_operational_policies
               SET notification_channel = 'tenant_api'
             WHERE personalized_documents_enabled = true
               AND notification_channel = 'flit_smtp';
            """);
    }
}
