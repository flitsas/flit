-- =============================================================================
-- TRASPASO_UNILATERAL: capacidades declaradas del gate_profile (ADR-0051).
-- Migración: 20260826150000_TraspasoUnilateralCapacidadesDeclaradas (DDL 94)
--
-- El seed base de `82-parametrizacion-catalogo-completo.sql` era, por su propia advertencia, «una
-- base técnica sin validar» — y quedó al revés de lo que el negocio validó: declaraba
-- `biometricActors:["BUYER"]` (valida identidad el comprador) y `requiresCommercialValue`/
-- `commercialValueSource` (avalúo Fasecolda), cuando ADR-0051 fija que en `TRASPASO_UNILATERAL` SÍ
-- comparece el propietario (`requiresSeller:true`, va en `vehicle_owner_*` del FUR) pero NO se
-- captura por formulario (`sellerCapturedViaForm:false`), SOLO ÉL firma y valida identidad
-- (`signatureActors`/`biometricActors:["OWNER"]`), y no hay compraventa ni avalúo entre dos partes
-- porque el locatario ya tenía el vehículo por contrato de leasing (`generatesSaleDocument`/
-- `hasAppraisalBlock: false`).
--
-- Alcance: ÚNICAMENTE `TRASPASO_UNILATERAL`. Ningún otro tipo cambia de valor — ver la guarda final,
-- que confirma que `TRASPASO_STANDARD` y `TRASPASO_TRANSFERENCIA_DE_DOMINIO` no necesitan declarar
-- las llaves nuevas: los defaults de `ProcedureTypeGateProfile` (ADR-0051 §Compatibilidad hacia
-- atrás) les devuelven exactamente su comportamiento actual porque ambos ya declaran
-- `requiresSeller:true` y `biometricActors:["OWNER","BUYER"]`.
--
-- `TRASPASO_UNILATERAL` tiene `wizard_enabled = false` (no operable en creación) y no hay
-- expedientes reales de este tipo en ningún ambiente: no hay `procedure_type_snapshots` que proteger.
--
-- Idempotente y reaplicable.
-- =============================================================================

-- ============================================================================
-- 1. gate_profile — capacidades declaradas + corrección de biometricActors + retiro de avalúo
-- ============================================================================
UPDATE tramites.procedure_types
   SET gate_profile = (coalesce(gate_profile, '{}'::jsonb)
                        || '{
                              "requiresSeller": true,
                              "sellerCapturedViaForm": false,
                              "signatureActors": ["OWNER"],
                              "biometricActors": ["OWNER"],
                              "generatesSaleDocument": false,
                              "hasAppraisalBlock": false
                            }'::jsonb)
                       - 'requiresCommercialValue'
                       - 'commercialValueSource',
       updated_at = now()
 WHERE code = 'TRASPASO_UNILATERAL'
   AND (gate_profile -> 'requiresSeller' IS DISTINCT FROM 'true'::jsonb
        OR gate_profile -> 'sellerCapturedViaForm' IS DISTINCT FROM 'false'::jsonb
        OR gate_profile -> 'signatureActors' IS DISTINCT FROM '["OWNER"]'::jsonb
        OR gate_profile -> 'biometricActors' IS DISTINCT FROM '["OWNER"]'::jsonb
        OR gate_profile -> 'generatesSaleDocument' IS DISTINCT FROM 'false'::jsonb
        OR gate_profile -> 'hasAppraisalBlock' IS DISTINCT FROM 'false'::jsonb
        OR gate_profile -> 'requiresCommercialValue' IS NOT NULL
        OR gate_profile -> 'commercialValueSource' IS NOT NULL);

-- Guarda: si el tipo existe, tiene que quedar declarado exactamente así. Entrymode/requiresBuyer/
-- requiresBiometrics/requiresSignature/validateOtOperability/simitMode NO se tocan (fuera de
-- alcance de este ADR) — se verifica que sigan presentes, no su valor puntual, para no acoplar esta
-- guarda a parametrización que un superadmin pueda ajustar después en esas llaves.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM tramites.procedure_types WHERE code = 'TRASPASO_UNILATERAL')
       AND NOT EXISTS (
           SELECT 1
             FROM tramites.procedure_types
            WHERE code = 'TRASPASO_UNILATERAL'
              AND gate_profile ->> 'requiresSeller' = 'true'
              AND gate_profile ->> 'sellerCapturedViaForm' = 'false'
              AND gate_profile -> 'signatureActors' = '["OWNER"]'::jsonb
              AND gate_profile -> 'biometricActors' = '["OWNER"]'::jsonb
              AND gate_profile ->> 'generatesSaleDocument' = 'false'
              AND gate_profile ->> 'hasAppraisalBlock' = 'false'
              AND gate_profile -> 'requiresCommercialValue' IS NULL
              AND gate_profile -> 'commercialValueSource' IS NULL
              AND gate_profile -> 'entryMode' IS NOT NULL
              AND gate_profile -> 'requiresBuyer' IS NOT NULL
              AND gate_profile -> 'requiresBiometrics' IS NOT NULL
              AND gate_profile -> 'requiresSignature' IS NOT NULL
              AND gate_profile -> 'validateOtOperability' IS NOT NULL
              AND gate_profile -> 'simitMode' IS NOT NULL)
    THEN
        RAISE EXCEPTION 'TRASPASO_UNILATERAL no quedó con las capacidades declaradas de ADR-0051';
    END IF;
END $$;

-- ============================================================================
-- 2. Recorrido — retira la sección COMERCIAL del paso «documentos»
-- ============================================================================
-- Sin compraventa (`generatesSaleDocument:false`) ni bloque de avalúo (`hasAppraisalBlock:false`) no
-- hay valor comercial que capturar en el wizard. El resto del recorrido (consulta → comprador →
-- documentos[checklist] → identidad → fur) queda igual — sembrado por 82-parametrizacion-catalogo-
-- completo.sql.
DELETE FROM tramites.procedure_sections sec
 USING tramites.procedure_steps st, tramites.procedure_types pt
 WHERE sec.procedure_step_id = st.id
   AND st.procedure_type_id = pt.id
   AND pt.code = 'TRASPASO_UNILATERAL'
   AND st.code = 'documentos'
   AND sec.code = 'COMERCIAL'
   AND sec.section_type = 'commercial';

-- ============================================================================
-- 3. Checklist documental — retira ÚNICAMENTE la fila «compraventa»
-- ============================================================================
-- `tarjeta_propiedad`, `doc_identidad_comprador` y `soat` se dejan como están: el checklist
-- documental de este tipo queda a cargo de parametrización (TipoTramiteDocumentos), donde
-- `contrato_leasing` y `declaracion_arrendadora` ya están disponibles en el catálogo
-- (23-HU10520-document-types-seed.sql) — no se siembran aquí a propósito, es decisión del usuario.
DELETE FROM tramites.procedure_document_requirements pdr
 USING tramites.procedure_types pt, tramites.document_types dt
 WHERE pdr.procedure_type_id = pt.id
   AND pdr.document_type_id = dt.id
   AND pt.code = 'TRASPASO_UNILATERAL'
   AND dt.code = 'compraventa';

-- ============================================================================
-- 4. Guarda de no-regresión: TRASPASO_STANDARD y TRASPASO_TRANSFERENCIA_DE_DOMINIO
-- ============================================================================
-- ADR-0051 §Compatibilidad hacia atrás: si cualquiera de los dos tipos gana una declaración
-- explícita de `signatureActors`, `generatesSaleDocument` o `hasAppraisalBlock` cuyo valor no
-- coincida con lo que ya produce el default de ProcedureTypeGateProfile (requiresSeller:true +
-- family TRASPASO ⇒ ["OWNER","BUYER"] / true / true), esta migración se detiene: significa que el
-- default dejó de alcanzar y hay que declarar la llave explícitamente para ese tipo — no asumirlo.
DO $$
DECLARE
    con_desvio text;
BEGIN
    SELECT string_agg(pt.code, ', ') INTO con_desvio
      FROM tramites.procedure_types pt
     WHERE pt.code IN ('TRASPASO_STANDARD', 'TRASPASO_TRANSFERENCIA_DE_DOMINIO')
       AND (
           -- Ambos deben seguir con requiresSeller:true y biometricActors:["OWNER","BUYER"]: si eso
           -- cambia, el default de signatureActors/generatesSaleDocument/hasAppraisalBlock deja de
           -- describir su comportamiento actual.
           coalesce((pt.gate_profile ->> 'requiresSeller')::boolean, false) IS NOT TRUE
        OR pt.gate_profile -> 'biometricActors' IS DISTINCT FROM '["OWNER","BUYER"]'::jsonb
           -- Ninguno de los dos debe declarar signatureActors explícito con un valor distinto de
           -- ["OWNER","BUYER"] (el que ya produce el default).
        OR (pt.gate_profile -> 'signatureActors' IS NOT NULL
            AND pt.gate_profile -> 'signatureActors' IS DISTINCT FROM '["OWNER","BUYER"]'::jsonb)
           -- Ninguno debe declarar generatesSaleDocument/hasAppraisalBlock explícito en false (el
           -- default ya resuelve true para ambos vía requiresSeller + family TRASPASO).
        OR (pt.gate_profile -> 'generatesSaleDocument' IS NOT NULL
            AND pt.gate_profile ->> 'generatesSaleDocument' = 'false')
        OR (pt.gate_profile -> 'hasAppraisalBlock' IS NOT NULL
            AND pt.gate_profile ->> 'hasAppraisalBlock' = 'false')
           );

    IF con_desvio IS NOT NULL THEN
        RAISE EXCEPTION
            'ADR-0051: % se desvió del comportamiento que el default de signatureActors/'
            'generatesSaleDocument/hasAppraisalBlock asume. Declara la llave explícitamente para '
            'ese tipo antes de continuar — ver ADR-0051 §Compatibilidad hacia atrás.', con_desvio;
    END IF;
END $$;

-- ============================================================================
-- 5. Comentario de esquema — refleja las 4 llaves nuevas + la corrección de biometricActors
-- ============================================================================
-- No modifica la migración ya aplicada (35-F08-conformation-profile.sql, regla FLIT #5): el
-- comentario de columna se reemite aquí, en una migración nueva. COMMENT ON no es destructivo ni
-- reversible-sensible: Down lo revierte a la redacción anterior.
COMMENT ON COLUMN tramites.procedure_types.gate_profile IS
'Perfil de conformación dinámico del tipo de trámite (ADR-0050, ampliado por ADR-0051). Esquema: { entryMode: "PLATE"|"VIN"|"BOTH", requiresSeller: bool, sellerCapturedViaForm: bool (ADR-0051, ausente=true; separa "hay vendedor" de "se captura por formulario"), requiresBuyer: bool, requiresLessee: bool, allowsMultipleBuyer: bool, allowsMultipleSeller: bool, requiresCommercialValue: bool, commercialValueSource: "FASECOLDA"|"BASE_GRAVABLE"|"MERCADO_LIBRE"|null, requiresBiometrics: bool, biometricActors: string[] ("OWNER"|"BUYER"|"LESSEE"), signatureActors: string[] (ADR-0051, ausente=RequiresSeller?["OWNER","BUYER"]:["BUYER"]), requiresSignature: bool, requiresPlateRequest: bool, validateCompanyRule: bool, validateOtOperability: bool, validateDuplicateProcedure: bool, validateSoat: bool, validatePazSalvoImpuesto: bool, hasPrendaGate: bool, simitMode: "INTERNAL"|"ONLINE"|null, allowsComplementaryTransformations: bool|null, allowsComplementaryPrenda: bool|null, generatesSaleDocument: bool|null (ADR-0051, ausente=RequiresSeller&&family==TRASPASO), hasAppraisalBlock: bool|null (ADR-0051, misma regla), transitOfficeSource: "RUNT"|"OPERATOR"|null, requiresDestinationTransitOffice: bool }. Evaluado por ProcedureTypeGateProfile.cs / DynamicGateEvaluator.cs cuando F08_DynamicProcedures flag = true.';
