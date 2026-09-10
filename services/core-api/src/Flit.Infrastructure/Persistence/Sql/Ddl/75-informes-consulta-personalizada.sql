-- 75-informes-consulta-personalizada.sql — Informes programados sobre una consulta guardada
--
-- Reportes 2.0 (HU-D, segunda ola): un informe programado puede apuntar a una CONSULTA GUARDADA
-- (analytics.company_saved_queries o analytics.superadmin_saved_queries) en vez de a uno de los 5
-- tipos agregados existentes (resumen/operacion/ot/uso/productividad). Solo formato Excel — el
-- resultado de una consulta es tabular, no tiene el KPI/gráfica que amerita un PDF ejecutivo.
--
-- tenant_id pasa a NULLABLE únicamente para este caso: una consulta de SuperAdmin (scope
-- 'superadmin') cruza todas las compañías a propósito (mismo motor que
-- ICompanyQueryRepository.ExecuteForSuperAdminAsync — ver comentario de la tabla gemela en
-- 72-superadmin-consultas-guardadas.sql) y no pertenece a un tenant. Los otros 5 tipos de informe
-- SIGUEN exigiendo tenant_id — son intrínsecamente de una compañía, esto no lo relaja.
--
-- saved_query_id NO lleva FK: apunta a una de DOS tablas posibles según saved_query_scope
-- (company_saved_queries | superadmin_saved_queries), y Postgres no tiene FK polimórfica. La
-- integridad la garantiza el handler de aplicación (igual criterio que el resto del módulo de
-- Consultas, que ya no usa FK entre sus tablas de saved query y las de dominio).

ALTER TABLE analytics.report_schedules
    ALTER COLUMN tenant_id DROP NOT NULL;

ALTER TABLE analytics.report_schedules
    ADD COLUMN IF NOT EXISTS saved_query_id uuid NULL,
    ADD COLUMN IF NOT EXISTS saved_query_scope varchar(10) NULL;

ALTER TABLE analytics.report_schedules
    DROP CONSTRAINT IF EXISTS report_schedules_report_type_check;

ALTER TABLE analytics.report_schedules
    ADD CONSTRAINT report_schedules_report_type_check
    CHECK (report_type IN ('resumen', 'operacion', 'ot', 'uso', 'productividad', 'consulta'));

ALTER TABLE analytics.report_schedules
    ADD CONSTRAINT report_schedules_saved_query_scope_check
    CHECK (saved_query_scope IS NULL OR saved_query_scope IN ('empresa', 'superadmin'));

-- Un informe tipo 'consulta' SIEMPRE trae saved_query_id + saved_query_scope, y el tenant depende
-- del alcance (empresa: obligatorio: superadmin: prohibido). Los otros 4 tipos NO tocan estas
-- columnas nuevas y siguen exigiendo tenant_id, como antes de esta migración.
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
                (saved_query_scope = 'empresa' AND tenant_id IS NOT NULL)
                OR (saved_query_scope = 'superadmin' AND tenant_id IS NULL)
            ))
    );

-- Un informe de consulta solo se entrega en Excel (§ arriba).
ALTER TABLE analytics.report_schedules
    ADD CONSTRAINT report_schedules_consulta_format_check
    CHECK (report_type <> 'consulta' OR format = 'excel');

COMMENT ON COLUMN analytics.report_schedules.saved_query_id IS
    'Solo report_type=consulta. Apunta a company_saved_queries o superadmin_saved_queries según saved_query_scope (sin FK: dos tablas posibles).';
COMMENT ON COLUMN analytics.report_schedules.saved_query_scope IS
    'Solo report_type=consulta. empresa (tenant_id obligatorio) | superadmin (tenant_id prohibido — cruza todas las compañías).';
