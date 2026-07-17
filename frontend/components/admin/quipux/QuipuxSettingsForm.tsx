"use client";

// HU #10710 — configuración operativa GLOBAL de la integración Quipux (admin.quipux_settings).
// Solo SuperAdmin (el endpoint exige SuperAdminPolicy; /admin/quipux ya queda restringido a
// SuperAdmin por el middleware). Reemplaza el "manda un PUT a mano": aquí se cargan URLs,
// credenciales Quipux/AWS, datos del funcionario y cadencias de los workers, y se enciende la
// integración con el interruptor maestro.
//
// Secretos: la contraseña Quipux y la secret key AWS NUNCA se devuelven (el GET solo dice si
// hay una cargada). Dejar el campo vacío = "no lo cambies"; escribir algo = reemplazarlo.
import { useEffect, useMemo, useState } from "react";
import { Send } from "lucide-react";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { useToast } from "@/components/admin/Toast";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import {
  fetchQuipuxSettings,
  saveQuipuxSettings,
  type QuipuxSettings,
  type SaveQuipuxSettingsRequest,
} from "@/lib/api/admin-quipux-settings";

// Valores por defecto de una fila nueva (espejan los DEFAULT del DDL y de QuipuxSettings).
const DEFAULTS = {
  enabled: false,
  urlLogin: "",
  urlRegisterDocument: "",
  urlValidateStatus: "",
  username: "",
  consumerCode: "",
  bucket: "",
  s3Prefix: "FLIT/",
  awsRegion: "us-east-1",
  awsAccessKeyId: "",
  officerDocumentType: 3,
  officerDocumentNumber: "",
  registerIntervalMinutes: 15,
  pollIntervalMinutes: 15,
  batchSize: 20,
  maxAttempts: 5,
  maxPolls: 500,
  timeoutSeconds: 60,
};

type FormState = typeof DEFAULTS;

// Catálogo de tipos de documento de Quipux (el mismo código que espera su API en
// `tipoDocumentoFuncionario`). El valor es el identificador que viaja al cable; la etiqueta es
// solo para la UI. Para la entidad que radica (FLIT) lo normal es 3 (NIT).
const DOCUMENT_TYPES: ReadonlyArray<{ value: number; label: string }> = [
  { value: 1, label: "NN - No identificado" },
  { value: 2, label: "Cédula de Ciudadanía" },
  { value: 3, label: "NIT" },
  { value: 4, label: "Cédula de Extranjería" },
  { value: 5, label: "Tarjeta de Identidad" },
  { value: 6, label: "Pasaporte" },
  { value: 7, label: "Número Único de Identificación" },
  { value: 8, label: "Carnet Diplomático" },
  { value: 9, label: "RUT" },
  { value: 20, label: "Sin Documento" },
  { value: 21, label: "Registro Civil" },
  { value: 22, label: "Cédula Venezolana" },
  { value: 25, label: "Cédula Ecuatoriana" },
];

function toForm(s: QuipuxSettings): FormState {
  return {
    enabled: s.enabled,
    urlLogin: s.urlLogin,
    urlRegisterDocument: s.urlRegisterDocument,
    urlValidateStatus: s.urlValidateStatus,
    username: s.username,
    consumerCode: s.consumerCode,
    bucket: s.bucket,
    s3Prefix: s.s3Prefix,
    awsRegion: s.awsRegion,
    awsAccessKeyId: s.awsAccessKeyId,
    officerDocumentType: s.officerDocumentType,
    officerDocumentNumber: s.officerDocumentNumber,
    registerIntervalMinutes: s.registerIntervalMinutes,
    pollIntervalMinutes: s.pollIntervalMinutes,
    batchSize: s.batchSize,
    maxAttempts: s.maxAttempts,
    maxPolls: s.maxPolls,
    timeoutSeconds: s.timeoutSeconds,
  };
}

export function QuipuxSettingsForm() {
  const { show } = useToast();

  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [form, setForm] = useState<FormState>(DEFAULTS);
  // Secretos: se editan aparte. Vacío = conservar el cifrado ya guardado.
  const [password, setPassword] = useState("");
  const [awsSecret, setAwsSecret] = useState("");
  const [hasPassword, setHasPassword] = useState(false);
  const [hasAwsSecret, setHasAwsSecret] = useState(false);
  const [updatedAt, setUpdatedAt] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void fetchQuipuxSettings(controller.signal)
      .then((settings) => {
        if (controller.signal.aborted) return;
        if (settings) {
          setForm(toForm(settings));
          setHasPassword(settings.hasPassword);
          setHasAwsSecret(settings.hasAwsSecretAccessKey);
          setUpdatedAt(settings.updatedAt);
        }
        setLoading(false);
      })
      .catch(() => {
        if (controller.signal.aborted) return;
        setLoadError(true);
        setLoading(false);
      });
    return () => controller.abort();
  }, []);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  // Espeja QuipuxSettings.EstaCompleta() del backend para avisar (no bloquear) cuando el
  // interruptor está encendido pero falta algo. Un secreto cuenta como presente si ya había uno
  // cargado (hasX) o si el administrador acaba de escribir uno.
  const looksComplete = useMemo(() => {
    const passwordOk = hasPassword || password.trim() !== "";
    const awsSecretOk = hasAwsSecret || awsSecret.trim() !== "";
    return (
      form.urlLogin.trim() !== "" &&
      form.urlRegisterDocument.trim() !== "" &&
      form.urlValidateStatus.trim() !== "" &&
      form.username.trim() !== "" &&
      passwordOk &&
      form.consumerCode.trim() !== "" &&
      form.bucket.trim() !== "" &&
      form.awsAccessKeyId.trim() !== "" &&
      awsSecretOk &&
      form.officerDocumentNumber.trim() !== ""
    );
  }, [form, hasPassword, hasAwsSecret, password, awsSecret]);

  const enabledButIncomplete = form.enabled && !looksComplete;

  async function save() {
    setSaveError(null);
    setSaving(true);
    const body: SaveQuipuxSettingsRequest = {
      ...form,
      // Vacío = conservar. Solo se envía el secreto cuando el administrador escribió uno nuevo.
      password: password.trim() === "" ? undefined : password,
      awsSecretAccessKey: awsSecret.trim() === "" ? undefined : awsSecret,
    };
    try {
      const saved = await saveQuipuxSettings(body);
      setForm(toForm(saved));
      setHasPassword(saved.hasPassword);
      setHasAwsSecret(saved.hasAwsSecretAccessKey);
      setUpdatedAt(saved.updatedAt);
      setPassword("");
      setAwsSecret("");
      setSaving(false);
      show(
        saved.enabled && !saved.estaCompleta
          ? "Configuración guardada. Ojo: está encendida pero incompleta, los workers no radicarán aún."
          : "Configuración de Quipux guardada.",
        "success",
      );
    } catch (err) {
      const status = (err as { status?: number }).status;
      setSaveError(
        status === 403
          ? "No tienes permisos para editar la configuración de Quipux."
          : "No se pudo guardar la configuración de Quipux. Revisa los valores e inténtalo de nuevo.",
      );
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <div
        className="flex items-center justify-center py-16"
        role="status"
        aria-busy="true"
        aria-live="polite"
      >
        <span className="sr-only">Cargando configuración…</span>
        <div
          className="h-10 w-10 animate-spin rounded-full border-2 border-t-transparent"
          style={{ borderColor: "#557EFF", borderTopColor: "transparent" }}
          aria-hidden="true"
        />
      </div>
    );
  }

  if (loadError) {
    return (
      <p role="alert" className="text-sm" style={{ color: "#FF4E00" }}>
        No se pudo cargar la configuración de Quipux. Recarga la página para reintentar.
      </p>
    );
  }

  return (
    <div className="space-y-6">
      {/* Interruptor maestro */}
      <section className="rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <ToggleSwitch
          id="qx-enabled"
          label="Integración Quipux activa"
          description="Interruptor maestro. Apagado (o sin configurar), los workers no hacen nada."
          checked={form.enabled}
          disabled={saving}
          onChange={(v) => set("enabled", v)}
        />
        {enabledButIncomplete && (
          <p
            role="alert"
            className="mt-3 rounded-xl px-3 py-2 text-[11px]"
            style={{ background: "#FFF4EC", color: "#7A2E00", border: "1px solid #FFD9C2" }}
          >
            <span className="font-semibold">Encendida pero incompleta.</span> Puedes guardar, pero
            los workers no radicarán hasta que estén todos los campos obligatorios (URLs, usuario y
            contraseña Quipux, código de consumidor, bucket y credenciales AWS, y el documento del
            funcionario).
          </p>
        )}
      </section>

      {/* Conexión con Quipux */}
      <section className="space-y-4 rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <h2 className="text-sm font-semibold">Conexión con Quipux</h2>
        <Field id="qx-url-login" label="URL de login">
          <input
            id="qx-url-login"
            type="url"
            value={form.urlLogin}
            disabled={saving}
            onChange={(e) => set("urlLogin", e.target.value)}
            placeholder="https://…/login"
            className={`mt-1 ${OT_INPUT_CLS}`}
          />
        </Field>
        <Field id="qx-url-register" label="URL de registro de documento">
          <input
            id="qx-url-register"
            type="url"
            value={form.urlRegisterDocument}
            disabled={saving}
            onChange={(e) => set("urlRegisterDocument", e.target.value)}
            placeholder="https://…/registroDocumento"
            className={`mt-1 ${OT_INPUT_CLS}`}
          />
        </Field>
        <Field id="qx-url-validate" label="URL de validación de estado">
          <input
            id="qx-url-validate"
            type="url"
            value={form.urlValidateStatus}
            disabled={saving}
            onChange={(e) => set("urlValidateStatus", e.target.value)}
            placeholder="https://…/validarEstadoDocumento"
            className={`mt-1 ${OT_INPUT_CLS}`}
          />
        </Field>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Field id="qx-username" label="Usuario">
            <input
              id="qx-username"
              type="text"
              autoComplete="off"
              value={form.username}
              disabled={saving}
              onChange={(e) => set("username", e.target.value)}
              className={`mt-1 ${OT_INPUT_CLS}`}
            />
          </Field>
          <SecretField
            id="qx-password"
            label="Contraseña"
            value={password}
            hasStored={hasPassword}
            disabled={saving}
            onChange={setPassword}
          />
        </div>
        <Field
          id="qx-consumer"
          label="Código de consumidor"
          hint="El que Quipux asignó a FLIT (p. ej. 1003). Viaja en el login y en cada payload."
        >
          <input
            id="qx-consumer"
            type="text"
            value={form.consumerCode}
            disabled={saving}
            onChange={(e) => set("consumerCode", e.target.value)}
            className={`mt-1 font-mono ${OT_INPUT_CLS}`}
          />
        </Field>
      </section>

      {/* Almacenamiento S3 (bucket de Quipux) */}
      <section className="space-y-4 rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <h2 className="text-sm font-semibold">Almacenamiento S3 (bucket de Quipux)</h2>
        <p className="text-[11px] opacity-60">
          El PDF consolidado se publica en el bucket S3 <span className="font-semibold">de Quipux</span>
          , de donde ellos lo leen. Es la única parte de la integración donde FLIT maneja
          credenciales AWS directas.
        </p>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Field id="qx-bucket" label="Bucket">
            <input
              id="qx-bucket"
              type="text"
              value={form.bucket}
              disabled={saving}
              onChange={(e) => set("bucket", e.target.value)}
              placeholder="qxinterconnect"
              className={`mt-1 font-mono ${OT_INPUT_CLS}`}
            />
          </Field>
          <Field id="qx-prefix" label="Prefijo de la key">
            <input
              id="qx-prefix"
              type="text"
              value={form.s3Prefix}
              disabled={saving}
              onChange={(e) => set("s3Prefix", e.target.value)}
              placeholder="FLIT/"
              className={`mt-1 font-mono ${OT_INPUT_CLS}`}
            />
          </Field>
          <Field id="qx-region" label="Región AWS">
            <input
              id="qx-region"
              type="text"
              value={form.awsRegion}
              disabled={saving}
              onChange={(e) => set("awsRegion", e.target.value)}
              placeholder="us-east-1"
              className={`mt-1 font-mono ${OT_INPUT_CLS}`}
            />
          </Field>
          <Field id="qx-access-key" label="Access Key ID">
            <input
              id="qx-access-key"
              type="text"
              autoComplete="off"
              value={form.awsAccessKeyId}
              disabled={saving}
              onChange={(e) => set("awsAccessKeyId", e.target.value)}
              className={`mt-1 font-mono ${OT_INPUT_CLS}`}
            />
          </Field>
        </div>
        <SecretField
          id="qx-aws-secret"
          label="Secret Access Key"
          value={awsSecret}
          hasStored={hasAwsSecret}
          disabled={saving}
          onChange={setAwsSecret}
        />
      </section>

      {/* Entidad que radica (FLIT) */}
      <section className="space-y-4 rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <h2 className="text-sm font-semibold">Entidad que radica (FLIT)</h2>
        <p className="text-[11px] opacity-60">
          Identifica a <span className="font-semibold">FLIT</span> como la entidad que presenta el
          documento ante Quipux. No es el ciudadano ni el dueño del vehículo (ese viaja por trámite);
          es el «remitente» de la radicación. Normalmente es el NIT de FLIT.
        </p>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Field id="qx-officer-type" label="Tipo de documento">
            {/* Fondo/texto sólidos por tema: con `bg-transparent` (OT_INPUT_CLS) el popup nativo de
                opciones queda ilegible en modo oscuro. Se estiliza también cada <option>. */}
            <select
              id="qx-officer-type"
              value={form.officerDocumentType}
              disabled={saving}
              onChange={(e) => set("officerDocumentType", toInt(e.target.value, DEFAULTS.officerDocumentType))}
              className="mt-1 w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#0B0F14] outline-none focus:border-[#557EFF] disabled:opacity-60 dark:border-[#2A3441] dark:bg-[#0B0F14] dark:text-white"
            >
              {/* Si el valor guardado no está en el catálogo (dato viejo), se muestra igual. */}
              {!DOCUMENT_TYPES.some((t) => t.value === form.officerDocumentType) && (
                <option
                  value={form.officerDocumentType}
                  className="bg-white text-[#0B0F14] dark:bg-[#0B0F14] dark:text-white"
                >
                  Código {form.officerDocumentType}
                </option>
              )}
              {DOCUMENT_TYPES.map((t) => (
                <option
                  key={t.value}
                  value={t.value}
                  className="bg-white text-[#0B0F14] dark:bg-[#0B0F14] dark:text-white"
                >
                  {t.value} — {t.label}
                </option>
              ))}
            </select>
          </Field>
          <Field id="qx-officer-number" label="Número de documento" hint="El NIT de FLIT como entidad que radica.">
            <input
              id="qx-officer-number"
              type="text"
              value={form.officerDocumentNumber}
              disabled={saving}
              onChange={(e) => set("officerDocumentNumber", e.target.value)}
              placeholder="NIT de FLIT"
              className={`mt-1 font-mono ${OT_INPUT_CLS}`}
            />
          </Field>
        </div>
      </section>

      {/* Cadencia y límites */}
      <section className="space-y-4 rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <h2 className="text-sm font-semibold">Cadencia y límites</h2>
        <p className="text-[11px] opacity-60">
          Cambian en caliente: los workers releen estos valores en cada ciclo, sin desplegar.
        </p>
        <div className="grid grid-cols-2 gap-4 md:grid-cols-3">
          <NumberField
            id="qx-register-interval"
            label="Intervalo de registro (min)"
            value={form.registerIntervalMinutes}
            min={1}
            max={1440}
            disabled={saving}
            onChange={(v) => set("registerIntervalMinutes", v)}
          />
          <NumberField
            id="qx-poll-interval"
            label="Intervalo de consulta (min)"
            value={form.pollIntervalMinutes}
            min={1}
            max={1440}
            disabled={saving}
            onChange={(v) => set("pollIntervalMinutes", v)}
          />
          <NumberField
            id="qx-batch"
            label="Tamaño de lote"
            value={form.batchSize}
            min={1}
            max={500}
            disabled={saving}
            onChange={(v) => set("batchSize", v)}
          />
          <NumberField
            id="qx-max-attempts"
            label="Máx. intentos"
            value={form.maxAttempts}
            min={1}
            max={100}
            disabled={saving}
            onChange={(v) => set("maxAttempts", v)}
          />
          <NumberField
            id="qx-max-polls"
            label="Máx. consultas"
            value={form.maxPolls}
            min={1}
            max={100000}
            disabled={saving}
            onChange={(v) => set("maxPolls", v)}
          />
          <NumberField
            id="qx-timeout"
            label="Timeout (seg)"
            value={form.timeoutSeconds}
            min={1}
            max={600}
            disabled={saving}
            onChange={(v) => set("timeoutSeconds", v)}
          />
        </div>
      </section>

      {saveError && (
        <p role="alert" className="text-sm" style={{ color: "#FF4E00" }}>
          {saveError}
        </p>
      )}

      <div className="flex items-center justify-between gap-3">
        <p className="text-[11px] opacity-60">
          {updatedAt
            ? `Última actualización: ${new Date(updatedAt).toLocaleString("es-CO")}`
            : "Aún no se ha guardado ninguna configuración."}
        </p>
        <button
          type="button"
          onClick={save}
          disabled={saving}
          className="inline-flex items-center gap-1.5 rounded-xl px-5 py-2.5 text-sm font-semibold text-white shadow-sm disabled:opacity-60"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <Send className="h-4 w-4" aria-hidden="true" />
          {saving ? "Guardando…" : "Guardar configuración"}
        </button>
      </div>
    </div>
  );
}

// ── Subcomponentes de campo ─────────────────────────────────────────────────────────────

function Field({
  id,
  label,
  hint,
  children,
}: {
  id: string;
  label: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={id} className="text-xs font-semibold">
        {label}
      </label>
      {children}
      {hint && <p className="mt-1 text-[11px] opacity-60">{hint}</p>}
    </div>
  );
}

/**
 * Campo de secreto: si ya hay uno cargado, el placeholder lo indica y el campo vacío significa
 * "no lo cambies". El backend nunca devuelve el valor, así que aquí nunca se precarga.
 */
function SecretField({
  id,
  label,
  value,
  hasStored,
  disabled,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  hasStored: boolean;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <Field
      id={id}
      label={label}
      hint={hasStored ? "Hay uno guardado. Déjalo vacío para conservarlo." : undefined}
    >
      <input
        id={id}
        type="password"
        autoComplete="new-password"
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        placeholder={hasStored ? "•••••••• (guardado)" : "Sin configurar"}
        className={`mt-1 ${OT_INPUT_CLS}`}
      />
    </Field>
  );
}

function NumberField({
  id,
  label,
  value,
  min,
  max,
  disabled,
  onChange,
}: {
  id: string;
  label: string;
  value: number;
  min: number;
  max: number;
  disabled: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <Field id={id} label={label}>
      <input
        id={id}
        type="number"
        min={min}
        max={max}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(toInt(e.target.value, value))}
        className={`mt-1 ${OT_INPUT_CLS}`}
      />
    </Field>
  );
}

/** Parsea un entero; si el campo queda vacío o no es número, conserva el valor previo (fallback). */
function toInt(raw: string, fallback: number): number {
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) ? n : fallback;
}
