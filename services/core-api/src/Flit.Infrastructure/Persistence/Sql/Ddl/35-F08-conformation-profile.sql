-- FEATURE-08 / Fase 2b — Parte A: ALTER tablas existentes
-- Migración: 20260721100000_F08_ConformationProfile
-- CFD-01 (version + gate_profile en procedure_types)
-- CFD-09 (section_type en procedure_sections)
-- CFD-06 (is_dummy + condition_group en procedure_document_requirements + triggers A16)

ALTER TABLE tramites.procedure_types
    ADD COLUMN IF NOT EXISTS version integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS gate_profile jsonb NOT NULL DEFAULT '{}';

COMMENT ON COLUMN tramites.procedure_types.version IS
'Versión semántica del tipo. Se incrementa al publicar un cambio de configuración. Las instancias en curso capturan esta versión en tramites.procedure_type_snapshots para protegerse de cambios posteriores (AC#5).';

COMMENT ON COLUMN tramites.procedure_types.gate_profile IS
'Perfil de conformación dinámico del tipo de trámite. Esquema: { entryMode: "PLATE"|"VIN"|"BOTH", requiresSeller: bool, requiresBuyer: bool, allowsMultipleBuyer: bool, allowsMultipleSeller: bool, requiresCommercialValue: bool, commercialValueSource: "FASECOLDA"|"BASE_GRAVABLE"|"MERCADO_LIBRE"|null, requiresBiometrics: bool, biometricActors: string[], requiresSignature: bool, requiresPlateRequest: bool, validateCompanyRule: bool, validateOtOperability: bool, validateDuplicateProcedure: bool, validateSoat: bool, validatePazSalvoImpuesto: bool, hasPrendaGate: bool, simitMode: "INTERNAL"|"ONLINE"|null }. Evaluado por DynamicGateEvaluator.cs cuando F08_DynamicProcedures flag = true.';

ALTER TABLE tramites.procedure_sections
    ADD COLUMN IF NOT EXISTS section_type varchar(40) NOT NULL DEFAULT 'generic_form';

ALTER TABLE tramites.procedure_sections
    ADD CONSTRAINT ck_procedure_sections_section_type
        CHECK (section_type IN (
            'vehicle_query',
            'document_checklist',
            'actor_form',
            'commercial',
            'biometric',
            'signature_fur',
            'plate_request',
            'prenda_decision',
            'generic_form'
        ));

COMMENT ON COLUMN tramites.procedure_sections.section_type IS
'Tipo de renderer frontend. Mapea a SectionRendererRegistry en el cliente (CFD-09). Catálogo cerrado — cambios requieren PR coordinado backend + frontend.';

ALTER TABLE tramites.procedure_document_requirements
    ADD COLUMN IF NOT EXISTS is_dummy boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS condition_group varchar(50),
    ADD COLUMN IF NOT EXISTS row_version bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS updated_at timestamptz,
    ADD COLUMN IF NOT EXISTS updated_by uuid;

COMMENT ON COLUMN tramites.procedure_document_requirements.is_dummy IS
'Buzón dummy CFD-06: el documento se muestra en el checklist pero no bloquea la validación del paso.';
COMMENT ON COLUMN tramites.procedure_document_requirements.condition_group IS
'Grupo condicional (ej: "prenda"). El requisito solo aplica si la condición del grupo está activa en la instancia. NULL = siempre aplica.';

DROP TRIGGER IF EXISTS tr_procedure_doc_req_row_version ON tramites.procedure_document_requirements;
CREATE TRIGGER tr_procedure_doc_req_row_version
    BEFORE UPDATE ON tramites.procedure_document_requirements
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_procedure_doc_req_audit ON tramites.procedure_document_requirements;
CREATE TRIGGER tr_procedure_doc_req_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_document_requirements
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
