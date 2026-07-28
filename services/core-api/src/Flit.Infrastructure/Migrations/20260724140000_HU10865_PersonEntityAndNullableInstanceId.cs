using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10865 (Feature #10864, CF-00) — Entidad persona/sujeto a nivel tenant para
    /// prevalidaciones de identidad (ADR-0030, Propuesto).
    ///
    /// El DDL embebido (41-HU10865-persons.sql):
    /// 1) Crea <c>tramites.persons</c> (tenant-scoped, RLS, triggers row_version + audit,
    ///    índices, comentarios PII — Ley 1581).
    /// 2) Altera <c>tramites.procedure_instance_biometric_validations</c>:
    ///    - ADD COLUMN person_id → FK a <c>tramites.persons</c> (nullable backcompat).
    ///    - ALTER COLUMN procedure_instance_id → nullable (soporta prevalidaciones standalone).
    ///    - DROP FK CASCADE → ADD FK SET NULL (protege standalone al borrar la instancia).
    ///    - ADD CHECK <c>ck_biometric_validation_anchor</c>
    ///      (person_id IS NOT NULL OR procedure_instance_id IS NOT NULL).
    ///    - ADD índice <c>ix_procedure_instance_biometric_validations_person_id</c>.
    ///    - ADD índice de cobertura <c>ix_biometric_validations_vigente_approved</c>
    ///      (CF-02: FindVigenteApprovedByDocumentAsync incluyendo standalone).
    ///
    /// La tabla <c>tramites.persons</c> queda con <c>ExcludeFromMigrations()</c> en
    /// <c>PersonConfiguration</c> (mismo patrón que <c>person_data_consents</c> /
    /// <c>external_query_cache</c>): RLS/triggers/CHECKs que el MigrationBuilder no modela.
    /// Los cambios a <c>procedure_instance_biometric_validations</c> (entidad gestionada por EF)
    /// se aplican vía SQL crudo (<c>migrationBuilder.Sql</c>) para mantener el patrón de DDL
    /// embebido; el snapshot se actualiza manualmente para reflejar el nuevo estado del modelo.
    ///
    /// Prerequisito: debe ejecutarse ANTES de las HUs #10866 y #10867.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260724140000_HU10865_PersonEntityAndNullableInstanceId")]
    public partial class HU10865_PersonEntityAndNullableInstanceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("41-HU10865-persons.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- ── Down HU #10865 ───────────────────────────────────────────────────────────
                -- ADVERTENCIA: DROP COLUMN person_id puede fallar si hay filas con person_id IS NOT NULL.
                -- Ejecutar solo en ambientes sin datos de persona (DEV seed / tests).

                -- Eliminar constraint de ancla
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP CONSTRAINT IF EXISTS ck_biometric_validation_anchor;

                -- Eliminar FK a persons
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP CONSTRAINT IF EXISTS fk_procedure_instance_biometric_validations_persons;

                -- Eliminar índices agregados
                DROP INDEX IF EXISTS tramites.ix_procedure_instance_biometric_validations_person_id;
                DROP INDEX IF EXISTS tramites.ix_biometric_validations_vigente_approved;

                -- Eliminar columna person_id
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP COLUMN IF EXISTS person_id;

                -- Restaurar procedure_instance_id NOT NULL (guard: fallará si hay filas con null)
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    ALTER COLUMN procedure_instance_id SET NOT NULL;

                -- Restaurar FK CASCADE (drop SET NULL + recrear CASCADE)
                ALTER TABLE tramites.procedure_instance_biometric_validations
                    DROP CONSTRAINT IF EXISTS fk_procedure_instance_biometric_validations_procedure_instances;

                ALTER TABLE tramites.procedure_instance_biometric_validations
                    ADD CONSTRAINT fk_procedure_instance_biometric_validations_procedure_instances
                        FOREIGN KEY (procedure_instance_id)
                        REFERENCES tramites.procedure_instances(id)
                        ON DELETE CASCADE
                        ON UPDATE CASCADE;

                -- Eliminar tabla persons y sus objetos dependientes
                DROP TABLE IF EXISTS tramites.persons CASCADE;
                """);
        }
    }
}
