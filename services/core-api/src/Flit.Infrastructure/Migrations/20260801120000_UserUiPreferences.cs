using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Base compartida de preferencias de UI por usuario (<c>admin.user_ui_preferences</c>):
    /// permite que cada usuario elija qué ve (p. ej. columnas de una tabla) y esa elección
    /// persista entre sesiones, por tenant + usuario + scope. RLS por tenant igual que el resto
    /// de <c>admin.*</c> (patrón <c>admin.ot_feature_flags</c>). Ver DDL espejo en
    /// <c>Persistence/Sql/Ddl/46-user-ui-preferences.sql</c> para el detalle comentado.
    /// </summary>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260801120000_UserUiPreferences")]
    public partial class UserUiPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("46-user-ui-preferences.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_user_ui_preferences_audit ON admin.user_ui_preferences;
                DROP TRIGGER IF EXISTS tr_user_ui_preferences_row_version ON admin.user_ui_preferences;
                DROP TABLE IF EXISTS admin.user_ui_preferences;
                """);
    }
}
