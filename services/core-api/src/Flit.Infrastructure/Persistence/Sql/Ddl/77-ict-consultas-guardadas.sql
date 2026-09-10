-- 77-ict-consultas-guardadas.sql — Consultas guardadas sobre pre-trámites de ICT
--
-- El gemelo de analytics.company_saved_queries, mismo alcance empresa + usuario, pero para las
-- consultas que arma una empresa sobre sus propios pre-trámites de Integración con Terceros
-- (ict.external_integration_master, propiedad del microservicio core-ict). Es una tabla propia y no
-- una fila más de company_saved_queries porque lo que cada una nombra en su "definicion" es un
-- catálogo de campos distinto (IctQueryFieldCatalog pregunta por el pipeline de validación de
-- pre-trámites, CompanyQueryFieldCatalog por el ciclo del trámite ya radicado): una tabla común
-- obligaría a una columna de discriminación que solo sirve para volver a separarlas en cada consulta.
--
-- La definición va en jsonb y no en una tabla de condiciones, por la misma razón que el resto de
-- consultas guardadas: el catálogo de campos vive en el código y crece, y columnas normalizadas
-- exigirían una migración cada vez que se agrega un campo consultable.
--
-- Alcance empresa + usuario. El aislamiento sigue el patrón del resto de analytics.*: se filtra en
-- la capa de aplicación por el tenant del token, no con RLS.

CREATE SCHEMA IF NOT EXISTS analytics;

CREATE TABLE analytics.ict_saved_queries (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_ict_saved_queries PRIMARY KEY (id),
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE ON UPDATE CASCADE,
    nombre varchar(120) NOT NULL,
    descripcion varchar(400),
    definicion jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz,
    CONSTRAINT ck_ict_saved_queries_nombre CHECK (length(btrim(nombre)) > 0)
);

-- Todas las lecturas filtran por empresa + usuario y ordenan por nombre.
CREATE INDEX ix_ict_saved_queries_tenant_user
  ON analytics.ict_saved_queries(tenant_id, user_id, nombre);

-- Dos consultas con el mismo nombre para la misma persona en la misma empresa son un error de
-- interfaz, no una funcionalidad: la lista dejaría de poder identificarlas.
CREATE UNIQUE INDEX uq_ict_saved_queries_tenant_user_nombre
  ON analytics.ict_saved_queries(tenant_id, user_id, lower(btrim(nombre)));
