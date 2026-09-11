"use client";

// HU #10710 — configuración operativa GLOBAL de la integración Quipux (admin.quipux_settings).
// Solo SuperAdmin (el endpoint exige SuperAdminPolicy; /admin/quipux ya queda restringido a
// SuperAdmin por el middleware). Reemplaza el "manda un PUT a mano": aquí se cargan URLs,
// credenciales Quipux/AWS, datos del funcionario y cadencias de los workers, y se enciende la
// integración con el interruptor maestro.
//
// Secretos: la contraseña Quipux y la secret key AWS NUNCA se devuelven (el GET solo dice si
// hay una cargada). Dejar el campo vacío = "no lo cambies"; escribir algo = reemplazarlo.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Save } from "lucide-react";
import { ToggleSwitch } from "@/components/admin/companies/ToggleSwitch";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CreateButton } from "@/components/atom/CreateButton";
import { CarLoader, CarLoaderModal } from "@/components/atom/CarLoader";
import { InlineAlert } from "@/components/atom/InlineAlert";
import {
  WIZARD_HINT,
  WIZARD_INPUT,
  WIZARD_LABEL,
  WIZARD_SELECT,
} from "@/components/operacion/wizard-field-styles";
import { ApiError } from "@/lib/api/types";
import {
  fetchQuipuxSettings,
  saveQuipuxSettings,
  type QuipuxSettings,
  type SaveQuipuxSettingsRequest,
} from "@/lib/api/admin-quipux-settings";
import { digitsOnly } from "@/lib/format/currency";

const CARD =
  "space-y-4 rounded-2xl border border-[#DFE5ED] bg-white p-5 dark:border-white/10 dark:bg-[#162744]";

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

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoadError(false);
    setLoading(true);
    try {
      const settings = await fetchQuipuxSettings(signal);
      if (signal?.aborted) return;
      if (settings) {
        setForm(toForm(settings));
        setHasPassword(settings.hasPassword);
        setHasAwsSecret(settings.hasAwsSecretAccessKey);
        setUpdatedAt(settings.updatedAt);
      } else {
        setForm(DEFAULTS);
        setHasPassword(false);
        setHasAwsSecret(false);
        setUpdatedAt(null);
      }
      setLoading(false);
    } catch {
      if (signal?.aborted) return;
      setLoadError(true);
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

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
  const isEmpty = updatedAt == null;

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
      const status = err instanceof ApiError ? err.status : (err as { status?: number }).status;
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
      <div className="py-16">
        <CarLoader label="Cargando Integración Quipux…" />
      </div>
    );
  }

  if (loadError) {
    return (
      <UiStateBoundary
        status="error"
        errorMessage="No se pudo cargar la configuración de Quipux."
        onRetry={() => void load()}
      />
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {saving ? <CarLoaderModal label="Guardando configuración…" /> : null}
      {isEmpty ? (
        <InlineAlert tone="info" title="Todavía no hay una configuración guardada">
          Se muestran los valores por defecto. Al guardar se crea la fila de Integración Quipux.
        </InlineAlert>
      ) : null}

      <InlineAlert tone="info">
        Los workers releen estos valores en cada ciclo, sin redeploy. La contraseña y la clave AWS
        no se muestran: déjalas vacías para conservarlas.
      </InlineAlert>

      <section className={CARD}>
        <SectionHeader
          title="Interruptor maestro"
          description="Apagado (o sin configurar), los workers de Quipux no hacen nada."
        />
        <ToggleSwitch
          id="qx-enabled"
          label="Integración Quipux activa"
          checked={form.enabled}
          disabled={saving}
          onChange={(v) => set("enabled", v)}
        />
        {enabledButIncomplete ? (
          <InlineAlert tone="warning" title="Encendida pero incompleta">
            Puedes guardar, pero los workers no radicarán hasta que estén todos los campos
            obligatorios (URLs, usuario y contraseña Quipux, código de consumidor, bucket y
            credenciales AWS, y el documento del funcionario).
          </InlineAlert>
        ) : null}
      </section>

      <section className={CARD}>
        <SectionHeader
          title="Conexión con Quipux"
          description="Direcciones y credenciales con las que FLIT inicia sesión y radica."
        />
        <Field id="qx-url-login" label="URL de login">
          <input
            id="qx-url-login"
            type="url"
            value={form.urlLogin}
            disabled={saving}
            onChange={(e) => set("urlLogin", e.target.value)}
            placeholder="https://…/login"
            className={`mt-1 ${WIZARD_INPUT}`}
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
            className={`mt-1 ${WIZARD_INPUT}`}
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
            className={`mt-1 ${WIZARD_INPUT}`}
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
              className={`mt-1 ${WIZARD_INPUT}`}
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
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            value={form.consumerCode}
            disabled={saving}
            onChange={(e) => set("consumerCode", digitsOnly(e.target.value))}
            className={`mt-1 font-mono ${WIZARD_INPUT}`}
          />
        </Field>
      </section>

      <section className={CARD}>
        <SectionHeader
          title="Almacenamiento S3 (bucket de Quipux)"
          description="El PDF consolidado se publica en el bucket de Quipux, de donde ellos lo leen. Es la única parte donde FLIT maneja credenciales AWS directas."
        />
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Field id="qx-bucket" label="Bucket">
            <input
              id="qx-bucket"
              type="text"
              value={form.bucket}
              disabled={saving}
              onChange={(e) => set("bucket", e.target.value)}
              placeholder="qxinterconnect"
              className={`mt-1 font-mono ${WIZARD_INPUT}`}
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
              className={`mt-1 font-mono ${WIZARD_INPUT}`}
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
              className={`mt-1 font-mono ${WIZARD_INPUT}`}
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
              className={`mt-1 font-mono ${WIZARD_INPUT}`}
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

      <section className={CARD}>
        <SectionHeader
          title="Entidad que radica (FLIT)"
          description="Identifica a FLIT como remitente ante Quipux. No es el ciudadano ni el dueño del vehículo; normalmente es el NIT de FLIT."
        />
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Field id="qx-officer-type" label="Tipo de documento">
            <select
              id="qx-officer-type"
              value={form.officerDocumentType}
              disabled={saving}
              onChange={(e) => set("officerDocumentType", toInt(e.target.value, DEFAULTS.officerDocumentType))}
              className={`mt-1 ${WIZARD_SELECT}`}
            >
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
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="off"
              value={form.officerDocumentNumber}
              disabled={saving}
              onChange={(e) => set("officerDocumentNumber", digitsOnly(e.target.value))}
              placeholder="NIT de FLIT"
              className={`mt-1 font-mono ${WIZARD_INPUT}`}
            />
          </Field>
        </div>
      </section>

      <section className={CARD}>
        <SectionHeader
          title="Cadencia y límites"
          description="Intervalos, lote y reintentos. Aplican en el siguiente ciclo, sin desplegar."
        />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3">
          <NumberField
            id="qx-register-interval"
            label="Intervalo de registro (min)"
            hint="1 a 1440"
            value={form.registerIntervalMinutes}
            disabled={saving}
            onChange={(v) => set("registerIntervalMinutes", v)}
          />
          <NumberField
            id="qx-poll-interval"
            label="Intervalo de consulta (min)"
            hint="1 a 1440"
            value={form.pollIntervalMinutes}
            disabled={saving}
            onChange={(v) => set("pollIntervalMinutes", v)}
          />
          <NumberField
            id="qx-batch"
            label="Tamaño de lote"
            hint="1 a 500"
            value={form.batchSize}
            disabled={saving}
            onChange={(v) => set("batchSize", v)}
          />
          <NumberField
            id="qx-max-attempts"
            label="Máx. intentos"
            hint="1 a 100"
            value={form.maxAttempts}
            disabled={saving}
            onChange={(v) => set("maxAttempts", v)}
          />
          <NumberField
            id="qx-max-polls"
            label="Máx. consultas"
            hint="1 a 100000"
            value={form.maxPolls}
            disabled={saving}
            onChange={(v) => set("maxPolls", v)}
          />
          <NumberField
            id="qx-timeout"
            label="Timeout (seg)"
            hint="1 a 600"
            value={form.timeoutSeconds}
            disabled={saving}
            onChange={(v) => set("timeoutSeconds", v)}
          />
        </div>
      </section>

      {saveError ? <InlineAlert tone="error">{saveError}</InlineAlert> : null}

      <div className="flex flex-wrap items-center justify-between gap-3 pb-2">
        <p className="text-xs text-[#59677D] dark:text-white/70">
          {updatedAt
            ? `Última actualización: ${new Date(updatedAt).toLocaleString("es-CO")}`
            : "Aún no se ha guardado ninguna configuración."}
        </p>
        <CreateButton
          label={saving ? "Guardando…" : "Guardar configuración"}
          icon={Save}
          disabled={saving}
          onClick={() => void save()}
        />
      </div>
    </div>
  );
}

function SectionHeader({ title, description }: { title: string; description: string }) {
  return (
    <header className="border-b border-[#DFE5ED] pb-3 dark:border-white/10">
      <h2 className="text-sm font-semibold text-[#162744] dark:text-white">{title}</h2>
      <p className="mt-0.5 text-xs leading-snug text-[#59677D] dark:text-white/70">{description}</p>
    </header>
  );
}

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
    <div className="min-w-0">
      <label htmlFor={id} className={WIZARD_LABEL}>
        {label}
      </label>
      {children}
      {hint ? (
        <p className={WIZARD_HINT}>{hint}</p>
      ) : null}
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
        className={`mt-1 ${WIZARD_INPUT}`}
      />
    </Field>
  );
}

function NumberField({
  id,
  label,
  hint,
  value,
  disabled,
  onChange,
}: {
  id: string;
  label: string;
  hint?: string;
  value: number;
  disabled: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <Field id={id} label={label} hint={hint}>
      <input
        id={id}
        type="text"
        inputMode="numeric"
        pattern="[0-9]*"
        autoComplete="off"
        value={value}
        disabled={disabled}
        onChange={(e) => {
          const raw = digitsOnly(e.target.value);
          onChange(raw === "" ? value : toInt(raw, value));
        }}
        className={`mt-1 ${WIZARD_INPUT}`}
      />
    </Field>
  );
}

/** Parsea un entero; si el campo queda vacío o no es número, conserva el valor previo (fallback). */
function toInt(raw: string, fallback: number): number {
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) ? n : fallback;
}
