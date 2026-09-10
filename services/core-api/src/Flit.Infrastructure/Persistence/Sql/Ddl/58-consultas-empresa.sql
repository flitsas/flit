-- 58-consultas-empresa.sql — Consultas guardadas de la empresa gestora
--
-- El gemelo de admin.ot_saved_queries, del otro lado del trámite. Cada usuario de una empresa arma
-- sus propias búsquedas sobre los trámites que gestiona y las guarda para volver a ejecutarlas.
--
-- La definición va en jsonb y no en una tabla de condiciones a propósito: el catálogo de campos
-- consultables está en el código (CompanyQueryFieldCatalog) y crece; con columnas normalizadas,
-- agregar un campo exigiría una migración, que es justo lo que el catálogo evita.
--
-- Alcance empresa + usuario. El aislamiento sigue el patrón del resto de analytics.*: se filtra en
-- la capa de aplicación por el tenant del token, no con RLS. Es el mismo camino por el que ya se
-- leen los reportes de esta empresa, y tener dos mecanismos distintos para el mismo dato es cómo se
-- termina confiando en el que no está aplicado.

CREATE SCHEMA IF NOT EXISTS analytics;

CREATE TABLE analytics.company_saved_queries (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_saved_queries PRIMARY KEY (id),
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    nombre varchar(120) NOT NULL,
    descripcion varchar(400),
    definicion jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT ck_company_saved_queries_nombre CHECK (length(btrim(nombre)) > 0)
);

-- Todas las lecturas filtran por empresa + usuario y ordenan por nombre.
CREATE INDEX ix_company_saved_queries_tenant_user
  ON analytics.company_saved_queries(tenant_id, user_id, nombre);

-- Dos consultas con el mismo nombre para la misma persona en la misma empresa son un error de
-- interfaz, no una funcionalidad: la lista dejaría de poder identificarlas.
CREATE UNIQUE INDEX uq_company_saved_queries_tenant_user_nombre
  ON analytics.company_saved_queries(tenant_id, user_id, lower(btrim(nombre)));
