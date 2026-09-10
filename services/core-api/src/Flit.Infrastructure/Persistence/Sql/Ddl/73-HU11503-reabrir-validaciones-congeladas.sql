-- ─────────────────────────────────────────────────────────────────────────────
-- Bug #11503 — REMEDIACIÓN DE DATOS: reabre las validaciones de identidad que
-- quedaron CONGELADAS en `rechazado` terminal tras el PRIMER intento fallido.
--
-- QUÉ PASÓ:
--   KyverumVerifyClient trataba el status top-level `rechazado` del GET como
--   cierre incondicional de la validación, cuando Kyverum lo emite igual en un
--   intento INTERMEDIO (aún quedaban reintentos). La fila se terminalizaba, y
--   como IdentityValidationResultApplier deja inmutable toda fila terminal, ya
--   no se recupera sola: aunque el ciudadano apruebe después, nunca se
--   actualiza. En trámites eso bloquea la radicación (borrador → preparado),
--   que exige una validación `aprobado` vigente.
--
-- FIRMA EXCLUSIVA DEL DEFECTO:
--   status = 'rechazado' AND attempts < max_attempts
--   Es exclusiva porque las otras dos rutas a `rechazado` exigen
--   attempts >= max_attempts: el webhook (KyverumVerifyCommand.cs:493) y las
--   ramas `rechazado_intento`/`default` de IdentityValidationReconciler.cs
--   (líneas 53 y 93). Alcanza tanto a validaciones de trámite como a
--   prevalidaciones standalone (procedure_instance_id IS NULL).
--
-- QUÉ HACE:
--   Devuelve la fila a `en_proceso` para que el ciclo normal la resuelva. NO
--   inventa un veredicto: no aprueba ni rechaza nada. El worker de
--   reconciliación consultará a Kyverum y, con el cliente ya corregido,
--   aprobará si el ciudadano aprobó, terminalizará en `rechazado` si Kyverum
--   cerró de verdad (`result.closedAt`), o la dejará como Kyverum diga.
--
-- POR QUÉ reconcile_poll_count = 0 (CRÍTICO):
--   La consulta de reclamo del worker
--   (IdentityValidationReconcileProcessor.cs:207-219) acota por
--   `reconcile_poll_count < 3`. Una fila reabierta con el presupuesto de
--   sondeo ya agotado quedaría en `en_proceso` sin que NADIE la vuelva a
--   mirar: cambiaríamos un bug por otro (congelada en rechazado → congelada en
--   en_proceso). Reiniciarlo es exactamente la semántica documentada del campo:
--   se pone a 0 ante una señal de actividad nueva.
--
-- LO QUE NO SE TOCA:
--   attempts / max_attempts → el conteo del webhook es AUTORITATIVO y es la
--   señal con la que el reconciliador decidirá terminalizar; alterarlo
--   falsearía la decisión. created_at tampoco (es historia). provider_status,
--   provider_payload, detail y score se dejan como están: los reescribe el
--   propio reconciliador cuando llegue el veredicto real.
--
-- ─── FILTROS DE SEGURIDAD, cada uno con su razón ─────────────────────────────
--
-- 1) provider = 'kyverum'
--    El defecto es exclusivo del cliente Kyverum, y además solo el worker de
--    Kyverum reclama filas `en_proceso`. Reabrir una fila `mock` la dejaría
--    colgada para siempre porque nadie la sondea.
--
-- 2) kyverum_verification_id IS NOT NULL
--    Sin id de verificación no hay nada que reconciliar contra el proveedor.
--    El worker ya lo exige en su reclamo (línea 212).
--
-- 3) deleted_at IS NULL
--    No se resucita lo que alguien borró (soft delete, HU #10865).
--
-- 4) expires_at > now() + interval '5 minutes'
--    DESVIACIÓN DELIBERADA del diseño inicial, que contemplaba reabrir también
--    las vencidas para que "expiraran". La evidencia lo desaconseja: el paso de
--    vencidas del worker (IdentityValidationReconcileProcessor.cs:95-112) corre
--    ANTES que el de reconciliación y las terminaliza en `expirado` SIN
--    consultar a Kyverum. Reabrir una fila ya vencida, por tanto:
--      · no puede recuperar una aprobación (nunca se pregunta al proveedor);
--      · no mejora nada aguas abajo — el gate de radicación exige un `aprobado`
--        positivo, y ListProcedureInstancesQuery.cs:332 trata `expirado` y
--        `rechazado` como el MISMO estado agregado;
--      · sí introduce un daño: mientras esté en `en_proceso` ocupa el cupo del
--        índice único parcial de "en vuelo" (ver 5) y bloquea que se inicie una
--        validación nueva para ese documento.
--    El margen de 5 minutos existe porque el worker no sondea una fila tocada
--    hace menos de 120 s (Staleness, línea 28): sin margen, una fila a punto de
--    vencer sería capturada por el paso de vencidas antes de que alguien le
--    preguntara a Kyverum. Solo se reabre lo que de verdad da tiempo a
--    reconciliarse.
--
-- 5) Sin colisión con uq_biometric_validations_inflight_doc_norm
--    HU #11266 creó un índice ÚNICO PARCIAL: a lo sumo UNA validación en vuelo
--    (pendiente_envio | enviado | en_proceso) por (tenant, documento Trim+Upper)
--    con deleted_at IS NULL. Es el escenario ESPERADO del bug: al ciudadano lo
--    rechazaron mal y volvió a empezar, así que muchas filas congeladas conviven
--    con una validación nueva viva del mismo documento. Reabrirlas sin más
--    violaría el índice (23505) y TUMBARÍA EL DEPLOY completo. Por eso:
--      · se excluye toda candidata que ya tenga otra fila en vuelo (esa persona
--        no necesita rescate: ya tiene un intento vivo);
--      · si varias candidatas comparten (tenant, documento), se reabre SOLO la
--        más reciente por created_at — las demás chocarían entre sí.
--    Las candidatas descartadas por esta regla se quedan en `rechazado`, que es
--    inofensivo: ese estado NO bloquea iniciar una validación nueva (es
--    justamente lo que el índice permite).
--
-- IDEMPOTENTE: el predicado exige status = 'rechazado'; tras aplicarse, las
-- filas quedan en `en_proceso` y una segunda ejecución no coincide con nada.
-- Tampoco puede pisar trabajo posterior: una fila que el worker ya resolvió a
-- `aprobado`/`rechazado` legítimo (con attempts agotados) deja de cumplirlo.
--
-- RLS: el schema tramites tiene RLS habilitada pero NO `FORCE ROW LEVEL
-- SECURITY`, y las migraciones corren como owner de la tabla, así que la
-- política tenant_isolation no se evalúa aquí. La remediación es transversal a
-- tenants por diseño (el bug no discrimina) y no necesita fijar
-- app.current_tenant_id — mismo criterio que los backfills 65 y 66.
--
-- AUDITORÍA: la tabla tiene trigger tr_..._audit AFTER UPDATE, así que cada
-- fila reabierta queda registrada en public.audit_log con su valor previo.
-- ─────────────────────────────────────────────────────────────────────────────

WITH candidatas AS (
    SELECT v.id,
           v.tenant_id,
           upper(btrim(v.document_type))   AS doc_type_norm,
           upper(btrim(v.document_number)) AS doc_number_norm,
           v.created_at
      FROM tramites.procedure_instance_biometric_validations v
     WHERE v.status = 'rechazado'
       AND v.attempts < v.max_attempts
       AND v.provider = 'kyverum'
       AND v.kyverum_verification_id IS NOT NULL
       AND v.deleted_at IS NULL
       AND v.expires_at > now() + interval '5 minutes'
),
sin_colision AS (
    SELECT c.*
      FROM candidatas c
     WHERE NOT EXISTS (
               SELECT 1
                 FROM tramites.procedure_instance_biometric_validations o
                WHERE o.tenant_id = c.tenant_id
                  AND upper(btrim(o.document_type))   = c.doc_type_norm
                  AND upper(btrim(o.document_number)) = c.doc_number_norm
                  AND o.deleted_at IS NULL
                  AND o.status IN ('pendiente_envio', 'enviado', 'en_proceso')
           )
),
elegidas AS (
    SELECT DISTINCT ON (tenant_id, doc_type_norm, doc_number_norm) id
      FROM sin_colision
     ORDER BY tenant_id, doc_type_norm, doc_number_norm, created_at DESC
)
UPDATE tramites.procedure_instance_biometric_validations v
   SET status               = 'en_proceso',
       reconcile_poll_count = 0,
       updated_at           = now()
  FROM elegidas e
 WHERE v.id = e.id;

-- Diagnóstico de despliegue: deja constancia de cuántas filas siguen con la
-- firma del defecto y NO se reabrieron (vencidas o con otra validación en
-- vuelo). No falla ni altera datos; solo evita que el saldo quede invisible.
DO $$
DECLARE
    residuales integer;
BEGIN
    SELECT count(*) INTO residuales
      FROM tramites.procedure_instance_biometric_validations
     WHERE status = 'rechazado'
       AND attempts < max_attempts
       AND provider = 'kyverum'
       AND deleted_at IS NULL;

    IF residuales > 0 THEN
        RAISE NOTICE
            'Bug #11503: quedan % validaciones con la firma del defecto sin reabrir '
            '(enlace vencido o ya hay otra validación en vuelo para ese documento). '
            'No bloquean: `rechazado` permite iniciar una validación nueva.',
            residuales;
    END IF;
END $$;
