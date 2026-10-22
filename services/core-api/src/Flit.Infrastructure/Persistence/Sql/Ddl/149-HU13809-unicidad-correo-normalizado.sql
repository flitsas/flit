-- =============================================================================
-- HU #13809 (Feature #13808, Épica #13516) — unicidad del correo normalizado a nivel de datos.
-- Migración: 20261022100000_HU13809_UnicidadCorreoNormalizado.
--
-- Por qué: uq_users_email (parcial, deleted_at IS NULL, Bug #13194) ya hacía el correo único entre usuarios
-- vivos, pero distingue mayúsculas: 'Juan@x.com' y 'juan@x.com' convivían como dos cuentas. Además las
-- invitaciones pendientes solo eran únicas por compañía, así que dos compañías podían invitar el mismo correo
-- y terminar creando dos usuarios.
--
-- Qué hace:
--   1. Aborta si hay usuarios vivos duplicados por correo normalizado (lower(btrim(email))). Fusionar cuentas
--      es una decisión de negocio: se depuran a mano antes de reintentar (consulta de detección abajo).
--   2. Normaliza (sin espacios, en minúsculas) el correo de usuarios e invitaciones.
--   3. Cancela las invitaciones pendientes que ya no se pueden aceptar con la regla global:
--        a. las de un correo que ya tiene usuario vivo;
--        b. entre pendientes del mismo correo (en cualquier compañía), todas menos la más reciente.
--      Una invitación cancelada se puede reactivar desde la UI; no se borra nada.
--   4. CHECK (email = lower(btrim(email))) en identity.users y security.user_invitations: la base rechaza
--      cualquier correo sin normalizar, así uq_users_email (sobre la columna, sin índice funcional) basta para
--      la unicidad insensible a mayúsculas y el ON CONFLICT (email) de la integración ICT sigue igual.
--   5. Reemplaza uq_user_invitations_tenant_email_pending (tenant_id, email) por
--      uq_user_invitations_email_pending (email): una sola invitación pendiente por correo en toda la plataforma.
--
-- Detección previa (solo lectura, correr en QA/PDN antes del despliegue):
--   SELECT lower(btrim(email)) AS correo, count(*) AS n, string_agg(id::text, ', ') AS ids
--     FROM identity.users WHERE deleted_at IS NULL GROUP BY 1 HAVING count(*) > 1;
--
-- Idempotente: la normalización y las cancelaciones no hacen nada en una segunda corrida; el CHECK y los
-- índices se crean con IF NOT EXISTS / DROP IF EXISTS.
-- =============================================================================

-- 1. Usuarios vivos duplicados por correo normalizado: abortar con detalle.
DO $$
DECLARE
    v_duplicados int;
    v_ejemplos   text;
BEGIN
    SELECT count(*), string_agg(correo, ', ')
      INTO v_duplicados, v_ejemplos
      FROM (SELECT lower(btrim(email)) AS correo
              FROM identity.users
             WHERE deleted_at IS NULL
             GROUP BY 1
            HAVING count(*) > 1
             ORDER BY 1
             LIMIT 20) d;

    IF v_duplicados > 0 THEN
        RAISE EXCEPTION 'HU13809: hay % correos con más de un usuario vivo (ignorando mayúsculas/espacios): %', v_duplicados, v_ejemplos
            USING HINT = 'Depurar los duplicados (eliminar o cambiar el correo de la cuenta sobrante) y volver a aplicar la migración. Consulta de detección en el encabezado de 149-HU13809-unicidad-correo-normalizado.sql.';
    END IF;
END $$;

-- 2. Normalizar correos.
UPDATE identity.users
   SET email = lower(btrim(email))
 WHERE email <> lower(btrim(email));

UPDATE security.user_invitations
   SET email = lower(btrim(email))
 WHERE email <> lower(btrim(email));

-- 3a. Pendientes de un correo que ya tiene usuario vivo: nunca podrían activarse.
UPDATE security.user_invitations i
   SET status = 'cancelled', updated_at = now()
 WHERE i.status = 'pending'
   AND EXISTS (SELECT 1 FROM identity.users u WHERE u.email = i.email AND u.deleted_at IS NULL);

-- 3b. Varias pendientes del mismo correo: queda la más reciente.
UPDATE security.user_invitations i
   SET status = 'cancelled', updated_at = now()
  FROM (SELECT id,
               row_number() OVER (PARTITION BY email ORDER BY coalesce(last_sent_at, created_at) DESC, id DESC) AS rn
          FROM security.user_invitations
         WHERE status = 'pending') r
 WHERE i.id = r.id
   AND r.rn > 1;

-- 4. La base rechaza correos sin normalizar.
ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS ck_users_email_normalizado;
ALTER TABLE identity.users
    ADD CONSTRAINT ck_users_email_normalizado CHECK (email = lower(btrim(email)));

ALTER TABLE security.user_invitations DROP CONSTRAINT IF EXISTS ck_user_invitations_email_normalizado;
ALTER TABLE security.user_invitations
    ADD CONSTRAINT ck_user_invitations_email_normalizado CHECK (email = lower(btrim(email)));

-- 5. Una sola invitación pendiente por correo en toda la plataforma.
DROP INDEX IF EXISTS security.uq_user_invitations_tenant_email_pending;
CREATE UNIQUE INDEX IF NOT EXISTS uq_user_invitations_email_pending
    ON security.user_invitations (email)
    WHERE status = 'pending';
