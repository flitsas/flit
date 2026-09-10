-- 76-reportes-programados-alertas-ot.sql — Informes programados y alertas del organismo de tránsito
--
-- Reportes 2.0 (HU-D, tercera ola): lleva "Informes programados y alertas" al lado del Organismo
-- de Tránsito (OT), replicando el patrón ya construido para compañía en vez de crear tablas nuevas.
--
-- Un OT ya es un tenant (identity.tenants) con un perfil OT asociado (admin.transit_office_profiles):
-- no hace falta una columna transit_office_id en report_schedules/alert_rules. Una fila de estas
-- tablas cuyo tenant_id es el tenant DUEÑO de un organismo, con report_type IN
-- ('ot_analisis','ot_informe','ot_revisores') o metric IN ('ot_rejection_rate_pct','ot_stuck_count'),
-- es sin ambigüedad "el informe/alerta propio de ese organismo" — exactamente el mismo tenant_id que
-- ya usa cualquier alerta de compañía, solo que la fuente de datos que lee
-- (IOtMetricsReadRepository, eje invertido) es distinta.
--
-- Tres tipos de informe, uno por pestaña con rango de OtReportsConsole.tsx (mismo criterio que los
-- 5 tipos de compañía, uno por pestaña de Reportes.tsx): "ot_analisis" (causales de rechazo +
-- desempeño), "ot_informe" (detalle trámite a trámite del periodo) y "ot_revisores" (qué hizo cada
-- persona). "Ahora mismo" queda fuera: es un snapshot en vivo sin rango, no un informe periódico.
--
-- "Consultas personalizadas" del organismo se programan igual que las de compañía: report_type=
-- 'consulta' con un tercer valor de saved_query_scope ('ot'), tratado como 'empresa' en el shape
-- check de abajo (tenant_id obligatorio: una consulta de OT no cruza organismos).

ALTER TABLE analytics.report_schedules
    DROP CONSTRAINT IF EXISTS report_schedules_report_type_check;

ALTER TABLE analytics.report_schedules
    ADD CONSTRAINT report_schedules_report_type_check
    CHECK (report_type IN (
        'resumen', 'operacion', 'ot', 'uso', 'productividad', 'consulta',
        'ot_analisis', 'ot_informe', 'ot_revisores'
    ));

ALTER TABLE analytics.report_schedules
    DROP CONSTRAINT IF EXISTS report_schedules_saved_query_scope_check;

ALTER TABLE analytics.report_schedules
    ADD CONSTRAINT report_schedules_saved_query_scope_check
    CHECK (saved_query_scope IS NULL OR saved_query_scope IN ('empresa', 'ot', 'superadmin'));

-- Ensancha el shape check de §75: 'ot' se comporta como 'empresa' (tenant_id obligatorio, nunca
-- cruza organismos) — solo 'superadmin' tiene tenant_id prohibido.
ALTER TABLE analytics.report_schedules
    DROP CONSTRAINT IF EXISTS report_schedules_consulta_shape_check;

ALTER TABLE analytics.report_schedules
    ADD CONSTRAINT report_schedules_consulta_shape_check
    CHECK (
        (report_type <> 'consulta'
            AND tenant_id IS NOT NULL
            AND saved_query_id IS NULL
            AND saved_query_scope IS NULL)
        OR (report_type = 'consulta'
            AND saved_query_id IS NOT NULL
            AND (
                (saved_query_scope IN ('empresa', 'ot') AND tenant_id IS NOT NULL)
                OR (saved_query_scope = 'superadmin' AND tenant_id IS NULL)
            ))
    );

ALTER TABLE analytics.alert_rules
    DROP CONSTRAINT IF EXISTS alert_rules_metric_check;

ALTER TABLE analytics.alert_rules
    ADD CONSTRAINT alert_rules_metric_check
    CHECK (metric IN (
        'rejection_rate_pct', 'stuck_count', 'external_api_errors', 'pending_identity_validations',
        'ict_stuck_in_validation', 'ict_novelty_rate_pct', 'ict_webhook_delivery_failures', 'ict_jobs_out_of_sla',
        'ot_rejection_rate_pct', 'ot_stuck_count'
    ));
