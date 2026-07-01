using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Restringe <c>identity.tenants.tenant_type</c> al catálogo B2B permitido
    /// (<c>RENTING</c>, <c>CONCESIONARIO</c>, <c>FLIT</c>) a nivel de BASE DE DATOS, para que
    /// no se puedan insertar/actualizar tipos arbitrarios ni siquiera por SQL manual.
    ///
    /// Hasta ahora la columna era <c>varchar(20)</c> sin CHECK y la validación vivía solo en la
    /// capa de aplicación (<see cref="Domain.Companies.Create.CompanyTenantTypes"/>), por lo que
    /// existían valores heredados fuera del catálogo (p.ej. <c>'standard'</c>, <c>'transit_office'</c>).
    ///
    /// Up: (1) NORMALIZA cualquier valor fuera del catálogo a <c>RENTING</c> (el tipo por defecto del
    /// alta) para que el CHECK no falle; (2) agrega el CHECK de forma idempotente
    /// (drop-if-exists + add). El CHECK es un artefacto de BD (como triggers/RLS de este repo): vive
    /// solo en SQL, NO en el modelo EF, así que no altera el snapshot.
    ///
    /// Down: solo elimina el CHECK. La normalización de tipos heredados es irreversible
    /// (no se conserva el valor original), igual que cualquier UPDATE de datos.
    /// </remarks>
    public partial class RestrictTenantTypeCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- 1. Normaliza tipos fuera del catálogo B2B (heredados: 'standard', 'transit_office', etc.) a RENTING.
                UPDATE identity.tenants
                   SET tenant_type = 'RENTING'
                 WHERE tenant_type NOT IN ('RENTING', 'CONCESIONARIO', 'FLIT');

                -- 2. Restringe tenant_type al catálogo B2B (idempotente: drop-if-exists + add).
                ALTER TABLE identity.tenants
                  DROP CONSTRAINT IF EXISTS ck_tenants_tenant_type;

                ALTER TABLE identity.tenants
                  ADD CONSTRAINT ck_tenants_tenant_type
                  CHECK (tenant_type IN ('RENTING', 'CONCESIONARIO', 'FLIT'));

                COMMENT ON CONSTRAINT ck_tenants_tenant_type ON identity.tenants IS
                  'Catálogo B2B de tipos de compañía permitidos. Mantener en sync con Flit.Admin.Domain.Companies.Create.CompanyTenantTypes.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE identity.tenants
                  DROP CONSTRAINT IF EXISTS ck_tenants_tenant_type;
                """);
        }
    }
}
