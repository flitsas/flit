-- 76-reportes-programados-alertas-ot.sql — Informes programados y alertas del organismo de tránsito
--
-- Reportes 2.0 (HU-D, tercera ola): lleva "Informes programados y alertas" al lado del Organismo
-- de Tránsito (OT), replicando el patrón ya construido para compañía en vez de crear tablas nuevas.
--
-- Un OT ya es un tenant (identity.tenants) con un perfil OT asociado (admin.transit_office_profiles):
-- no hace falta una columna transit_office_id en report_schedules/alert_rules. Una fila de estas
-- tablas cuyo tenant_id es el tenant DUEÑO de un organismo, con report_type='ot_operativo' o
-- metric IN ('ot_rejection_rate_pct','ot_stuck_count'), es sin ambigüedad "el informe/alerta propio
-- de ese organismo" — exactamente el mismo tenant_id que ya usa cualquier alerta de compañía, solo
-- que la fuente de datos que lee (IOtMetricsReadRepository, eje invertido) es distinta.
--
-- Solo se ensanchan los dos CHECK que enumeran valores permitidos; nada de esquema cambia.

ALTER TABLE analytics.report_schedules
    DROP CONSTRAINT IF EXISTS report_schedules_report_type_check;

ALTER TABLE analytics.report_schedules
    ADD CONSTRAINT report_schedules_report_type_check
    CHECK (report_type IN ('resumen', 'operacion', 'ot', 'uso', 'productividad', 'consulta', 'ot_operativo'));

ALTER TABLE analytics.alert_rules
    DROP CONSTRAINT IF EXISTS alert_rules_metric_check;

ALTER TABLE analytics.alert_rules
    ADD CONSTRAINT alert_rules_metric_check
    CHECK (metric IN (
        'rejection_rate_pct', 'stuck_count', 'external_api_errors', 'pending_identity_validations',
        'ict_stuck_in_validation', 'ict_novelty_rate_pct', 'ict_webhook_delivery_failures', 'ict_jobs_out_of_sla',
        'ot_rejection_rate_pct', 'ot_stuck_count'
    ));

-- report_schedules_consulta_shape_check (§75) ya exige tenant_id NOT NULL para todo report_type
-- distinto de 'consulta' — 'ot_operativo' cae ahí sin cambios: SIEMPRE con tenant (el del organismo).
