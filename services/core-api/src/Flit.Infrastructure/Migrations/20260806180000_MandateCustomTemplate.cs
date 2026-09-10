using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Plantilla propia de mandato por OT: PDF en storage u editor de cuerpo (fallback = template_code).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260806180000_MandateCustomTemplate")]
public partial class MandateCustomTemplate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("60-mandate-custom-template.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE admin.transit_office_mandate_config
              DROP CONSTRAINT IF EXISTS ck_transit_office_mandate_config_custom_kind,
              DROP COLUMN IF EXISTS custom_template_kind,
              DROP COLUMN IF EXISTS custom_template_storage_path,
              DROP COLUMN IF EXISTS custom_template_sha256,
              DROP COLUMN IF EXISTS custom_template_file_name,
              DROP COLUMN IF EXISTS custom_template_body,
              DROP COLUMN IF EXISTS custom_field_manifest;
            """);
    }
}
