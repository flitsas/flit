-- HU #12193 (Feature #12189) — hacer sargable la busqueda por placa del historial operativo.
--
-- PROBLEMA. El filtro de placa del listado genera `upper(plate) = @placa`
-- (ProcedureInstanceRepository.ApplyListFilters). El unico indice que existia era
-- ix_procedure_instances_tenant_id_plate ON (tenant_id, plate), de la DDL 47: una funcion sobre la
-- columna lo anula como camino de acceso para la placa. Postgres puede como mucho aprovechar el
-- prefijo tenant_id y despues filtra fila a fila; en la ruta global (SuperAdmin, sin tenant_id en el
-- predicado) ni eso: seq scan puro. La columna NO se normaliza al escribirse -- el trigger
-- tr_procedure_instance_field_values_denorm copia value_text tal cual (DDL 47) -- asi que el upper()
-- de la consulta no se puede quitar: hay que indexar la expresion.
--
-- upper() es IMMUTABLE solo porque el motor lo trata asi para la colacion de la base; es el mismo
-- supuesto que ya asume la consulta, no uno nuevo que introduzca esta migracion.
--
-- SIN CONCURRENTLY, a proposito: las migraciones EF corren dentro de una transaccion y
-- CREATE INDEX CONCURRENTLY no puede ejecutarse en una (ningun script de esta carpeta lo usa). Ademas
-- un CONCURRENTLY interrumpido deja el indice INVALID y un reintento con IF NOT EXISTS lo daria por
-- creado sin serlo. El volumen actual de tramites.procedure_instances (~1.3k filas en DEV) no
-- justifica el riesgo: el ACCESS EXCLUSIVE dura milisegundos.

-- 1) Camino acotado por compania (Radicador / AdminCompany, y SuperAdmin con X-Tenant-Id).
--    tenant_id primero (checklist A11) para que sirva tambien al predicado combinado.
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_plate_upper
    ON tramites.procedure_instances (tenant_id, upper(plate));

-- 2) Camino global de SuperAdmin (decision D1 del PO). No lleva tenant_id en el WHERE, asi que NO
--    puede apoyarse en el indice de arriba: necesita el suyo con upper(plate) como primera columna.
CREATE INDEX IF NOT EXISTS ix_procedure_instances_plate_upper
    ON tramites.procedure_instances (upper(plate));

-- 3) VIN, camino acotado. Mismo defecto exacto y misma linea de codigo (ApplyListFilters genera
--    `upper(vin) = @vin` justo encima del de placa), sobre un filtro que el listado de tramites ya
--    expone hoy; ix_procedure_instances_tenant_id_vin esta igual de muerto para ese predicado.
--    NO se crea el equivalente global (upper(vin)): ninguna funcionalidad pide buscar por VIN sin
--    compania -- el historial por placa es por placa -- y cada indice extra encarece la escritura de
--    una tabla que el trigger de denormalizacion actualiza en cada cambio de field_values.
CREATE INDEX IF NOT EXISTS ix_procedure_instances_tenant_id_vin_upper
    ON tramites.procedure_instances (tenant_id, upper(vin));

COMMENT ON INDEX tramites.ix_procedure_instances_tenant_id_plate_upper IS
    'HU #12193. Indice funcional para el filtro case-insensitive de placa acotado a la compania. Reemplaza como camino de acceso a ix_procedure_instances_tenant_id_plate, que queda solo para orden/rango por el valor crudo.';
COMMENT ON INDEX tramites.ix_procedure_instances_plate_upper IS
    'HU #12193. Indice funcional para el historial por placa cross-tenant de SuperAdmin (decision D1), cuyo WHERE no incluye tenant_id.';
COMMENT ON INDEX tramites.ix_procedure_instances_tenant_id_vin_upper IS
    'HU #12193. Equivalente para el filtro de VIN del listado, que sufre el mismo upper() sobre la columna.';
