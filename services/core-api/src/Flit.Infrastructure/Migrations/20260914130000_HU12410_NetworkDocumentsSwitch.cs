using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12410 (Feature #12257) — interruptor <c>network_documents_concesion</c> en
/// <c>identity.hierarchy_switches</c>: amplía el CHECK de claves y siembra la fila APAGADA (valor por
/// defecto mientras el PO no decida; pendiente 13 / bloqueo D1). Sin cambios de modelo EF: la entidad
/// <c>HierarchySwitch</c> ya representa cualquier fila de la tabla. DDL embebido 114-.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260914130000_HU12410_NetworkDocumentsSwitch")]
public partial class HU12410_NetworkDocumentsSwitch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("114-HU12410-network-documents-switch.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Borra la fila sembrada y devuelve el CHECK a las dos claves de 108-. El comentario de la tabla
    /// vuelve al texto de 108- (sin la clave nueva).
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DELETE FROM identity.hierarchy_switches WHERE switch_key = 'network_documents_concesion';

            ALTER TABLE identity.hierarchy_switches
                DROP CONSTRAINT IF EXISTS ck_hierarchy_switches_switch_key;

            ALTER TABLE identity.hierarchy_switches
                ADD CONSTRAINT ck_hierarchy_switches_switch_key
                CHECK (switch_key IN ('group_read_scope', 'inherited_configuration'));

            COMMENT ON COLUMN identity.hierarchy_switches.switch_key IS
                'Clave fija del interruptor: group_read_scope | inherited_configuration (ck_hierarchy_switches_switch_key).';

            COMMENT ON TABLE identity.hierarchy_switches IS
                'HU #12323 (ADR-0057) — interruptores GLOBALES de la jerarquía de clientes, conmutables sin '
                'despliegue. group_read_scope: apagado ⇒ toda cabeza de grupo resuelve alcance Single (deja de '
                'leer a sus hijos). inherited_configuration: apagado ⇒ los hijos dejan de heredar la '
                'configuración del padre (lista de OT; Feature #12256). Ninguno reutiliza is_group_parent: el '
                'vínculo y el invariante de profundidad siguen intactos con los interruptores apagados. Sin '
                'tenant_id ni RLS por ser configuración de plataforma (excepción documentada checklist A4/A10).';
            """);
}
