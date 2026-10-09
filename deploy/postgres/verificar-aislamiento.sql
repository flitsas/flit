-- HU #13330: comprueba que el usuario de un servicio solo opera en su esquema. Se corre CONECTADO COMO ESE USUARIO:
--
--   psql "postgresql://flit_consultas:<clave>@<host>/<base>" -v servicio=consultas -v ajeno=tramites \
--        -f deploy/postgres/verificar-aislamiento.sql
--
-- `ajeno` es cualquier esquema de otro servicio con al menos una tabla. Termina con código 0 solo si todo se cumple;
-- ante cualquier falla, psql sale con código 3 y el mensaje dice cuál.

\set ON_ERROR_STOP on

SELECT current_user = 'flit_' || :'servicio' AS es_el_usuario \gset
\if :es_el_usuario
\else
  DO $$ BEGIN RAISE EXCEPTION 'FALLA: no estás conectado como el usuario del servicio'; END $$;
\endif

-- Puede crear y borrar en su esquema.
SELECT format('CREATE TABLE %I.verificacion_aislamiento (id int)', :'servicio') \gexec
SELECT format('DROP TABLE %I.verificacion_aislamiento', :'servicio') \gexec
\echo 'OK: crea y borra en su esquema'

-- No puede leer una tabla de otro esquema.
SELECT format('%I.%I', schemaname, tablename) AS tabla_ajena
FROM pg_tables WHERE schemaname = :'ajeno' LIMIT 1 \gset
\set ON_ERROR_STOP off
SELECT 1 FROM :tabla_ajena LIMIT 1;
\if :ERROR
  \echo 'OK: no puede leer' :'tabla_ajena'
\else
  \echo 'FALLA: pudo leer' :'tabla_ajena'
  \set ON_ERROR_STOP on
  DO $$ BEGIN RAISE EXCEPTION 'FALLA: pudo leer una tabla de otro esquema'; END $$;
\endif

-- No puede crear en otro esquema ni en public.
SELECT format('CREATE TABLE %I.intruso (id int)', :'ajeno') \gexec
\if :ERROR
  \echo 'OK: no puede crear en' :'ajeno'
\else
  \echo 'FALLA: pudo crear en' :'ajeno'
  \set ON_ERROR_STOP on
  DO $$ BEGIN RAISE EXCEPTION 'FALLA: pudo crear en otro esquema'; END $$;
\endif
CREATE TABLE public.intruso (id int);
\if :ERROR
  \echo 'OK: no puede crear en public'
\else
  \set ON_ERROR_STOP on
  DO $$ BEGIN RAISE EXCEPTION 'FALLA: pudo crear en public'; END $$;
\endif

\echo 'AISLAMIENTO VERIFICADO'
