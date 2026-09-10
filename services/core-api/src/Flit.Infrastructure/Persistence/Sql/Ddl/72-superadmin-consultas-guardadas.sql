-- 72-superadmin-consultas-guardadas.sql — Consultas guardadas de SuperAdmin (todas las compañías)
--
-- El gemelo de analytics.company_saved_queries, pero SIN tenant_id: SuperAdmin consulta sobre todas
-- las compañías a la vez (Flit.Infrastructure.Persistence.Repositories.SuperAdminTenantScope), así
-- que una consulta suya no pertenece a una compañía en particular. Se guarda en una tabla propia y
-- no con un tenant_id centinela (ej. GUID vacío) sobre company_saved_queries, porque esa columna es
-- NOT NULL y sin FK a propósito para el caso normal, y forzar un centinela ahí sería enseñarle a otro
-- código a tratar un valor especial como si fuera un tenant real.
--
-- Alcance de EQUIPO, no de usuario: cualquier SuperAdmin ve, edita y borra las consultas de
-- cualquier otro SuperAdmin (created_by_user_id es solo auditoría). Es la decisión de producto: un
-- equipo de operaciones se reparte estas consultas, y dejarlas personales las volvería huérfanas en
-- cuanto quien las armó cambiara de rol.

CREATE SCHEMA IF NOT EXISTS analytics;

CREATE TABLE analytics.superadmin_saved_queries (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_superadmin_saved_queries PRIMARY KEY (id),
    created_by_user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    nombre varchar(120) NOT NULL,
    descripcion varchar(400),
    definicion jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT ck_superadmin_saved_queries_nombre CHECK (length(btrim(nombre)) > 0)
);

-- Toda lectura lista TODAS las filas (no hay tenant/usuario que filtre) y ordena por nombre.
CREATE INDEX ix_superadmin_saved_queries_nombre
  ON analytics.superadmin_saved_queries(nombre);

-- Nombre único en TODO el equipo de SuperAdmin, no por usuario: son consultas compartidas, y dos con
-- el mismo nombre serían indistinguibles en la lista de cualquiera que las vea.
CREATE UNIQUE INDEX uq_superadmin_saved_queries_nombre
  ON analytics.superadmin_saved_queries(lower(btrim(nombre)));
