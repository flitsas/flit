using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>HU-L10 — plantilla municipal al nacer (cinco OT conocidos); resto genérico.</summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260827190000_OtBirthTemplateByOffice")]
public partial class OtBirthTemplateByOffice : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("96-ot-birth-template-by-office.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No revierte filas ya sembradas: el OT pudo haber parametrizado después.
    }
}
