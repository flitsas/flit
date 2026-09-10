using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0051 — <c>TRASPASO_UNILATERAL</c> declara en su <c>gate_profile</c> las capacidades que hoy
/// se bifurcan por código/familia en el motor de conformación:
/// <c>sellerCapturedViaForm:false</c> (SÍ comparece el propietario, pero no llena formulario),
/// <c>signatureActors:["OWNER"]</c> y <c>biometricActors:["OWNER"]</c> (corrige el
/// <c>["BUYER"]</c> del seed técnico sin validar), <c>generatesSaleDocument:false</c> y
/// <c>hasAppraisalBlock:false</c> (no hay compraventa ni avalúo entre dos partes: el locatario ya
/// tenía el vehículo por contrato de leasing). Se retira <c>requiresCommercialValue</c>/
/// <c>commercialValueSource</c> (sin compraventa no hay valor de venta que capturar), la sección
/// COMERCIAL del paso «documentos» y la fila «compraventa» del checklist documental.
/// DDL: <c>94-traspaso-unilateral-capacidades-declaradas.sql</c>.
/// <para><c>TRASPASO_STANDARD</c> y <c>TRASPASO_TRANSFERENCIA_DE_DOMINIO</c> NO se tocan: sus
/// defaults en <c>ProcedureTypeGateProfile</c> ya reproducen su comportamiento actual (ADR-0051
/// §Compatibilidad hacia atrás), verificado con una guarda en el DDL.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260826150000_TraspasoUnilateralCapacidadesDeclaradas")]
public partial class TraspasoUnilateralCapacidadesDeclaradas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("94-traspaso-unilateral-capacidades-declaradas.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Restaura el seed técnico anterior a ADR-0051 (base sin validar, pero es el estado previo a
    /// esta migración): quita las 4 llaves nuevas y <c>requiresSeller</c> (ausente antes de este
    /// cambio), regresa <c>biometricActors</c> a <c>["BUYER"]</c> y repone
    /// <c>requiresCommercialValue</c>/<c>commercialValueSource</c>. Repone la sección COMERCIAL y la
    /// fila «compraventa» del checklist. No hay expedientes reales de este tipo en ningún ambiente
    /// (tipo no operable, <c>wizard_enabled = false</c>), así que no hay datos de negocio que
    /// reconciliar al revertir.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE tramites.procedure_types
               SET gate_profile = (gate_profile
                                    - 'requiresSeller'
                                    - 'sellerCapturedViaForm'
                                    - 'signatureActors'
                                    - 'generatesSaleDocument'
                                    - 'hasAppraisalBlock')
                                   || '{"biometricActors": ["BUYER"], "requiresCommercialValue": true, "commercialValueSource": "FASECOLDA"}'::jsonb,
                   updated_at = now()
             WHERE code = 'TRASPASO_UNILATERAL';

            INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout, section_type)
            SELECT uuidv7(), st.id, 'COMERCIAL', st.title, 2, 'single', 'commercial'
              FROM tramites.procedure_steps st
              JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
             WHERE pt.code = 'TRASPASO_UNILATERAL'
               AND st.code = 'documentos'
               AND NOT EXISTS (
                   SELECT 1 FROM tramites.procedure_sections sec
                    WHERE sec.procedure_step_id = st.id AND sec.code = 'COMERCIAL');

            INSERT INTO tramites.procedure_document_requirements
                (id, procedure_type_id, document_type_id, is_mandatory, default_sort_order)
            SELECT uuidv7(), pt.id, dt.id, true, 3
              FROM tramites.procedure_types pt, tramites.document_types dt
             WHERE pt.code = 'TRASPASO_UNILATERAL'
               AND dt.code = 'compraventa'
            ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING;

            COMMENT ON COLUMN tramites.procedure_types.gate_profile IS
            'Perfil de conformación dinámico del tipo de trámite. Esquema: { entryMode: "PLATE"|"VIN"|"BOTH", requiresSeller: bool, requiresBuyer: bool, allowsMultipleBuyer: bool, allowsMultipleSeller: bool, requiresCommercialValue: bool, commercialValueSource: "FASECOLDA"|"BASE_GRAVABLE"|"MERCADO_LIBRE"|null, requiresBiometrics: bool, biometricActors: string[], requiresSignature: bool, requiresPlateRequest: bool, validateCompanyRule: bool, validateOtOperability: bool, validateDuplicateProcedure: bool, validateSoat: bool, validatePazSalvoImpuesto: bool, hasPrendaGate: bool, simitMode: "INTERNAL"|"ONLINE"|null }. Evaluado por DynamicGateEvaluator.cs cuando F08_DynamicProcedures flag = true.';
            """);
}
