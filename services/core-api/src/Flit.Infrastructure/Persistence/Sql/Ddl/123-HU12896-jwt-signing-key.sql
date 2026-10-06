-- HU #12896 (Feature #12886, FLIT Suite, tarea A-03) — llave de firma del JWT persistente.
-- Migración: 20260925220000_HU12896_JwtSigningKey.
--
-- Hasta ahora, sin Jwt__PrivateKeyPem la API firmaba con una llave RSA efímera que cambiaba en cada reinicio, y
-- nadie validaba la firma (docs/suite/frentes/a-inventario-ambientes.md). Con Jwt:PersistSigningKey la API crea
-- la llave una sola vez y la guarda aquí, con la llave privada cifrada por Data Protection (keyring en
-- public.data_protection_keys, ApplicationName flit-core-api). Así sobrevive a reinicios y despliegues y la API
-- puede validar sus propios tokens sin poner llaves en el .env. Una llave configurada por variable gana siempre.
-- Tabla global, sin tenant_id ni RLS. Idempotente.

CREATE TABLE IF NOT EXISTS security.jwt_signing_keys (
    key_id                varchar(64) NOT NULL,
    protected_private_key text        NOT NULL,
    created_at            timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT pk_jwt_signing_keys PRIMARY KEY (key_id)
);

COMMENT ON TABLE security.jwt_signing_keys IS
    'HU #12896 (A-03) · Llave RSA de firma del JWT de core-api, cifrada con Data Protection. La crea la API la primera vez (Jwt:PersistSigningKey). Rotar = insertar un key_id nuevo y cambiar Jwt:SigningKeyId.';
COMMENT ON COLUMN security.jwt_signing_keys.protected_private_key IS
    '@pii:none · PEM PKCS#8 de la llave privada, protegido con IDataProtector (propósito Flit.Jwt.SigningKey.v1). Nunca se guarda en claro.';
