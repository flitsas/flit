-- HU #13330 (Epic #13316, ADR-0064 decisión 1): un usuario de base por servicio, dueño de SU esquema y sin
-- permisos sobre los demás. Idempotente: se puede correr de nuevo para cambiar la clave o el tope de conexiones.
--
-- Lo corre el líder técnico con un usuario administrador, una vez por servicio y por ambiente:
--
--   psql "$ADMIN_URL" -v servicio=consultas -v conexiones=20 -v clave="$CLAVE" \
--        -f deploy/postgres/servicio-con-esquema-propio.sql
--
--   servicio    código del servicio = nombre del esquema (consultas, notificaciones, …). El usuario es flit_<servicio>.
--   conexiones  tope de conexiones del usuario (HU #13331). Debe ser mayor o igual al Max Pool Size de su cadena.
--   clave       contraseña del usuario. Nunca se escribe en el repo: llega por variable y va al secreto del ambiente.
--
-- Qué NO hace: no toca core-api, core-identity ni core-ict, que siguen con su usuario actual hasta pagar la deuda de
-- lectura cruzada de Trámites (ver deploy/postgres/README.md).

\set ON_ERROR_STOP on

SELECT :'servicio' ~ '^[a-z][a-z0-9_]{1,30}$' AS servicio_valido \gset
\if :servicio_valido
\else
  DO $$ BEGIN RAISE EXCEPTION 'servicio inválido: solo minúsculas, dígitos y _, empezando por letra'; END $$;
\endif

SELECT 'flit_' || :'servicio' AS rol \gset

-- 1. El usuario: puede entrar, no hereda permisos de otros roles y tiene un tope de conexiones.
SELECT format('CREATE ROLE %I LOGIN NOINHERIT', :'rol')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'rol') \gexec
SELECT format('ALTER ROLE %I WITH LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION CONNECTION LIMIT %s PASSWORD %L',
              :'rol', :'conexiones', :'clave') \gexec

-- 2. Su esquema, del que es dueño: crea, migra y borra solo ahí (sus migraciones y su tabla __EFMigrationsHistory).
SELECT format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION %I', :'servicio', :'rol') \gexec
SELECT format('ALTER SCHEMA %I OWNER TO %I', :'servicio', :'rol') \gexec

-- 3. Entrar a la base, nada más. Sin search_path hacia otros esquemas.
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'rol') \gexec
SELECT format('ALTER ROLE %I IN DATABASE %I SET search_path = %I', :'rol', current_database(), :'servicio') \gexec

-- 4. Nunca crear objetos en public (Postgres 15+ ya lo niega a PUBLIC; se deja explícito por si la base viene de antes).
SELECT format('REVOKE CREATE ON SCHEMA public FROM %I', :'rol') \gexec

\echo 'Listo: usuario' :'rol' 'dueño del esquema' :'servicio'
