using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Bug #14625 — el trigger de inmutabilidad de <c>field_values</c> admite <c>impuesto_departamental_pagado</c> con
/// <c>source = flito</c> en todo estado salvo <c>anulado</c> y <c>revocado</c> (la marca del comprobante de FLITO).
/// DDL: <c>203-BUG14625-impuesto-pagado-flito-cualquier-estado.sql</c>. Lleva los atributos inline (sin Designer): sin
/// ellos la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261119140000_BUG14625_ImpuestoPagadoFlitoCualquierEstado")]
public partial class BUG14625_ImpuestoPagadoFlitoCualquierEstado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("203-BUG14625-impuesto-pagado-flito-cualquier-estado.sql"));

    /// <inheritdoc />
    /// <remarks>Restaura la versión del DDL 130 (HU #13264).</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("130-HU13264-impuesto-pagado-flito-preasignacion.sql"));
}
