-- Bootstrap transversal Fase 1 (schemas, uuidv7, audit, triggers)
-- Reversible: ver Down en SchemaBootstrap.cs

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE OR REPLACE FUNCTION uuidv7() RETURNS uuid
LANGUAGE sql VOLATILE AS $$
  SELECT encode(
    set_bit(set_bit(
      overlay(uuid_send(gen_random_uuid())
        PLACING substring(int8send(floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
        FROM 1 FOR 6),
      52, 1), 53, 1),
    'hex')::uuid;
$$;

CREATE SCHEMA IF NOT EXISTS identity;
CREATE SCHEMA IF NOT EXISTS security;
CREATE SCHEMA IF NOT EXISTS tramites;
CREATE SCHEMA IF NOT EXISTS admin;
CREATE SCHEMA IF NOT EXISTS analytics;
CREATE SCHEMA IF NOT EXISTS catalogs;
CREATE SCHEMA IF NOT EXISTS audit;

CREATE TABLE IF NOT EXISTS audit.audit_logs (
    id uuid PRIMARY KEY DEFAULT uuidv7(),
    schema_name text NOT NULL,
    table_name text NOT NULL,
    record_id uuid NOT NULL,
    action char(1) NOT NULL,
    old_data jsonb,
    new_data jsonb,
    changed_at timestamptz NOT NULL DEFAULT now(),
    changed_by uuid
);

CREATE OR REPLACE FUNCTION public.trg_row_version() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN NEW.row_version := COALESCE(OLD.row_version, 0) + 1; RETURN NEW; END; $$;

CREATE OR REPLACE FUNCTION public.trg_audit_log() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_action char(1); v_old jsonb; v_new jsonb; v_id uuid;
BEGIN
  IF TG_OP = 'INSERT' THEN v_action := 'I'; v_old := NULL; v_new := to_jsonb(NEW); v_id := NEW.id;
  ELSIF TG_OP = 'UPDATE' THEN v_action := 'U'; v_old := to_jsonb(OLD); v_new := to_jsonb(NEW); v_id := NEW.id;
  ELSIF TG_OP = 'DELETE' THEN v_action := 'D'; v_old := to_jsonb(OLD); v_new := NULL; v_id := OLD.id;
  END IF;
  INSERT INTO audit.audit_logs (schema_name, table_name, record_id, action, old_data, new_data)
  VALUES (TG_TABLE_SCHEMA, TG_TABLE_NAME, v_id, v_action, v_old, v_new);
  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF; RETURN NEW;
END; $$;
