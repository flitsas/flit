using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Bug #14604 — marca como exigido (<c>enforced = 'true'</c>) el permiso <c>AdminTramiteCargarDocumento</c> de la carga
/// administrativa de documentos. DDL: <c>202-BUG14604-admin-cargar-documento-enforced.sql</c>. Lleva los atributos inline
/// (sin Designer): sin ellos la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261118120000_BUG14604_AdminCargarDocumentoEnforced")]
public partial class BUG14604_AdminCargarDocumentoEnforced : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("202-BUG14604-admin-cargar-documento-enforced.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("UPDATE security.permissions SET enforced = 'false' WHERE slug = 'AdminTramiteCargarDocumento';");
}
