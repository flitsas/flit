using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10520 — Catálogo completo de tipos de documento (paridad FLIT 1.0).
    /// Siembra tramites.document_types con todos los documentos de CatalogoDocumentos.
    /// Idempotente (ON CONFLICT DO NOTHING) y sin regresión: reglas de carga iguales a
    /// las globales vigentes. Corre en todos los entornos (catálogo de negocio, no dev-seed).
    /// </remarks>
    public partial class HU10520_DocumentTypesCatalogSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("23-HU10520-document-types-seed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM tramites.document_types
                WHERE code IN (
                    'portada','factura','certificado_ambiental','aduana','declaracion_aduana',
                    'impronta','paz_salvo','paz_salvo_prenda','inscripcion_prenda',
                    'doc_identidad_propietario','doc_identidad_comprador','doc_identidad_vendedor',
                    'cedulas','poder_tramitador','anexos_generales','factura_carroceria','compraventa',
                    'fur','mandato','tramite_virtual','tablas_certificadoras','certificado_vigencia_soat_rtm',
                    'soat','rtm','soat_manual','paz_salvo_rnmc','rues','contrato_leasing',
                    'declaracion_arrendadora','transferencia_dominio','impronta_validada','firma',
                    'escritura_publica','camara_comercio','certificado_superfinanciera','liquidacion_impuesto',
                    'licencia_transito','tarjeta_propiedad','reporte_rtm_soat','consolidado','acta_remate',
                    'oficio_judicial','comprobante_derechos','acta_entrega','cert_tradicion','otro'
                );
                """);
        }
    }
}
