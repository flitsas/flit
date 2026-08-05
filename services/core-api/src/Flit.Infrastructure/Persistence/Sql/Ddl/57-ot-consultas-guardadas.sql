-- 57-ot-consultas-guardadas.sql — Consultas guardadas del organismo de tránsito
--
-- Cada usuario del organismo arma sus propias búsquedas sobre los trámites y las guarda para
-- volver a ejecutarlas. La definición va en jsonb y no en una tabla de condiciones a propósito:
-- el catálogo de campos consultables está en el código (OtQueryFieldCatalog) y crece; con columnas
-- normalizadas, agregar un campo exigiría una migración, que es justo lo que el catálogo evita.
--
-- El alcance es organismo + usuario y NO el tenant: el mismo usuario mirando dos organismos tiene
-- dos juegos de consultas, porque las empresas y los revisores que nombran son de un organismo
-- concreto. Por eso el aislamiento no puede ser la política RLS por tenant que usa el resto de
-- admin.* — se resuelve en la capa de aplicación, dentro del mismo alcance OT con el que se leen
-- los reportes (ver OtTenantScope).

CREATE TABLE admin.ot_saved_queries (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_ot_saved_queries PRIMARY KEY (id),
    transit_office_id uuid NOT NULL,
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    nombre varchar(120) NOT NULL,
    descripcion varchar(400),
    definicion jsonb NOT NULL DEFAULT '{}',
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT ck_ot_saved_queries_nombre CHECK (length(btrim(nombre)) > 0)
);

-- Todas las lecturas filtran por organismo + usuario y ordenan por nombre.
CREATE INDEX ix_ot_saved_queries_office_user
  ON admin.ot_saved_queries(transit_office_id, user_id, nombre);

-- Dos consultas con el mismo nombre para la misma persona en el mismo organismo son un error de
-- interfaz, no una funcionalidad: la lista dejaría de poder identificarlas.
CREATE UNIQUE INDEX uq_ot_saved_queries_office_user_nombre
  ON admin.ot_saved_queries(transit_office_id, user_id, lower(btrim(nombre)));

-- Triggers de negocio (checklist A16): row_version por UPDATE y bitácora genérica. Ambas funciones
-- existen desde SchemaBootstrap.
DROP TRIGGER IF EXISTS tr_ot_saved_queries_row_version ON admin.ot_saved_queries;
CREATE TRIGGER tr_ot_saved_queries_row_version BEFORE UPDATE ON admin.ot_saved_queries
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_ot_saved_queries_audit ON admin.ot_saved_queries;
CREATE TRIGGER tr_ot_saved_queries_audit AFTER INSERT OR UPDATE OR DELETE ON admin.ot_saved_queries
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
