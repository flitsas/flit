using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14033 (Feature #14022, Épica #14021) — siembra la empresa «Persona Natural», a cuyo nombre nacen los trámites del
/// portal de persona natural (<c>PlatformTenants.PersonaNatural</c>). DDL: <c>152-HU14033-empresa-persona-natural.sql</c>.
/// Lleva los atributos inline (sin Designer): sin ellos la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261027170000_HU14033_EmpresaPersonaNatural")]
public partial class HU14033_EmpresaPersonaNatural : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("152-HU14033-empresa-persona-natural.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Retira la empresa solo si no tiene nada colgado: una compañía con trámites, usuarios o permisos no se borra (la
    /// llave foránea lo impediría y perderíamos datos), así que en ese caso el DELETE no hace nada.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                DELETE FROM identity.tenants WHERE id = '00000000-0000-0000-0000-0000000f1171';
            EXCEPTION WHEN foreign_key_violation THEN
                RAISE NOTICE 'La empresa Persona Natural tiene datos asociados: no se retiró.';
            END $$;
            """);
}
