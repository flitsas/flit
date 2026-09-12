"use client";

// HU #12123 — formulario SuperAdmin de ict.job_settings. Espejo de QuipuxSettingsForm:
// 4 estados (vacío/carga/error/lleno), clamps en UI alineados al API, sin secretos.
import { useCallback, useEffect, useState } from "react";
import { Save } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
import { CarLoader, CarLoaderModal } from "@/components/atom/CarLoader";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  WIZARD_HINT,
  WIZARD_INPUT,
  WIZARD_LABEL,
} from "@/components/operacion/wizard-field-styles";
import { ApiError } from "@/lib/api/types";
import { digitsOnly } from "@/lib/format/currency";
import {
  fetchIctJobSettings,
  saveIctJobSettings,
  toIctJobSettingsWrite,
  validateIctJobSettings,
  type IctJobSettings,
  type IctJobSettingsFieldErrors,
  type IctJobSettingsWrite,
} from "@/lib/api/admin-ict-job-settings";

const ICT_JOBS_REPORT_HREF = "/?m=ict-reportes&ictReportesTab=jobs";

const CARD =
  "space-y-4 rounded-2xl border border-[#DFE5ED] bg-white p-5 dark:border-white/10 dark:bg-[#162744]";

export function IctJobSettingsForm() {
  const { show } = useToast();

  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<IctJobSettingsFieldErrors>({});
  const [form, setForm] = useState<IctJobSettingsWrite | null>(null);
  const [updatedAt, setUpdatedAt] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoadError(false);
    setLoading(true);
    try {
      const settings = await fetchIctJobSettings(signal);
      if (signal?.aborted) return;
      applySettings(settings);
      setLoading(false);
    } catch {
      if (signal?.aborted) return;
      setLoadError(true);
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial: el setState de `load` ocurre tras el await (no es setState síncrono).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  function applySettings(settings: IctJobSettings) {
    setForm(toIctJobSettingsWrite(settings));
    setUpdatedAt(settings.updatedAt);
    setFieldErrors({});
  }

  function set<K extends keyof IctJobSettingsWrite>(key: K, value: IctJobSettingsWrite[K]) {
    setForm((prev) => (prev ? { ...prev, [key]: value } : prev));
  }

  async function save() {
    if (!form) return;
    const errors = validateIctJobSettings(form);
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      setSaveError("Hay valores fuera de rango. Corrígelos antes de guardar.");
      return;
    }

    setFieldErrors({});
    setSaveError(null);
    setSaving(true);
    try {
      const saved = await saveIctJobSettings(form);
      applySettings(saved);
      setSaving(false);
      show("Configuración ICT guardada. core-ict la aplica en el siguiente ciclo.", "success");
    } catch (err) {
      const status = err instanceof ApiError ? err.status : (err as { status?: number }).status;
      setSaveError(
        status === 403
          ? "No tienes permisos para editar la configuración ICT."
          : status === 400
            ? "El servidor rechazó los valores (clamps o ventana). No se simuló el guardado."
            : "No se pudo guardar la configuración ICT. Revisa la conexión e inténtalo de nuevo.",
      );
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <div className="py-16">
        <CarLoader label="Cargando cadencia ICT…" />
      </div>
    );
  }

  if (loadError || !form) {
    return (
      <UiStateBoundary
        status="error"
        errorMessage="No se pudo cargar la configuración ICT."
        onRetry={() => void load()}
      />
    );
  }

  const isEmpty = updatedAt == null;

  return (
    <div className="flex flex-col gap-4">
      {saving ? <CarLoaderModal label="Guardando configuración…" /> : null}
      {isEmpty ? (
        <InlineAlert tone="info" title="Todavía no hay una configuración guardada">
          Se muestran los valores por defecto. Al guardar se crea la fila de cadencia ICT.
        </InlineAlert>
      ) : null}

      <InlineAlert tone="info">
        Los cambios aplican en el siguiente ciclo, sin redeploy. Zona horaria America/Bogota.{" "}
        <a href={ICT_JOBS_REPORT_HREF} className="font-semibold underline" style={{ color: "#557EFF" }}>
          Ver últimas corridas (Reportes ICT → Jobs)
        </a>
      </InlineAlert>

      <section className={CARD}>
        <SectionHeader
          title="Ventana horaria"
          description="El pipeline solo corre entre estas horas. El inicio se incluye; el fin no."
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <NumberField
            id="ict-window-start"
            label="Hora de inicio"
            hint="0 a 23, incluida"
            value={form.windowStartHour}
            error={fieldErrors.windowStartHour}
            disabled={saving}
            onChange={(v) => set("windowStartHour", v)}
          />
          <NumberField
            id="ict-window-end"
            label="Hora de fin"
            hint="0 a 23, exclusiva"
            value={form.windowEndHour}
            error={fieldErrors.windowEndHour}
            disabled={saving}
            onChange={(v) => set("windowEndHour", v)}
          />
        </div>
      </section>

      <div className="grid gap-4 xl:grid-cols-2">
        <section className={CARD}>
          <SectionHeader
            title="Validación de negocio"
            description="Reglas internas del pre-trámite."
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <NumberField
              id="ict-business-poll"
              label="Intervalo (segundos)"
              hint="1 a 3600"
              value={form.businessPollSeconds}
              error={fieldErrors.businessPollSeconds}
              disabled={saving}
              onChange={(v) => set("businessPollSeconds", v)}
            />
            <NumberField
              id="ict-business-batch"
              label="Lote"
              hint="1 a 5000"
              value={form.businessBatchSize}
              error={fieldErrors.businessBatchSize}
              disabled={saving}
              onChange={(v) => set("businessBatchSize", v)}
            />
          </div>
        </section>

        <section className={CARD}>
          <SectionHeader
            title="Validación externa"
            description="Fuentes externas del pre-trámite."
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <NumberField
              id="ict-external-poll"
              label="Intervalo (segundos)"
              hint="1 a 3600"
              value={form.externalPollSeconds}
              error={fieldErrors.externalPollSeconds}
              disabled={saving}
              onChange={(v) => set("externalPollSeconds", v)}
            />
            <NumberField
              id="ict-external-batch"
              label="Lote"
              hint="1 a 5000"
              value={form.externalBatchSize}
              error={fieldErrors.externalBatchSize}
              disabled={saving}
              onChange={(v) => set("externalBatchSize", v)}
            />
          </div>
        </section>
      </div>

      <section className={CARD}>
        <SectionHeader
          title="Consultas RUNT"
          description="Familia y placa del pre-trámite vía proveedor. No es Confirmación RUNT."
        />
        <div className="grid gap-4 sm:grid-cols-3">
          <NumberField
            id="ict-orch-poll"
            label="Intervalo (segundos)"
            hint="1 a 3600"
            value={form.orchestratorPollSeconds}
            error={fieldErrors.orchestratorPollSeconds}
            disabled={saving}
            onChange={(v) => set("orchestratorPollSeconds", v)}
          />
          <NumberField
            id="ict-orch-conc"
            label="Concurrencia"
            hint="1 a 100"
            value={form.orchestratorConcurrency}
            error={fieldErrors.orchestratorConcurrency}
            disabled={saving}
            onChange={(v) => set("orchestratorConcurrency", v)}
          />
          <NumberField
            id="ict-orch-batch"
            label="Lote"
            hint="1 a 5000"
            value={form.orchestratorBatchSize}
            error={fieldErrors.orchestratorBatchSize}
            disabled={saving}
            onChange={(v) => set("orchestratorBatchSize", v)}
          />
        </div>
      </section>

      <section className={CARD}>
        <SectionHeader
          title="Envío a FLIT"
          description="Pasa el pre-trámite validado a core-api."
        />
        <div className="grid gap-4 sm:grid-cols-3">
          <NumberField
            id="ict-send-poll"
            label="Intervalo (segundos)"
            hint="1 a 3600"
            value={form.sendPollSeconds}
            error={fieldErrors.sendPollSeconds}
            disabled={saving}
            onChange={(v) => set("sendPollSeconds", v)}
          />
          <NumberField
            id="ict-send-conc"
            label="Concurrencia"
            hint="1 a 100"
            value={form.sendConcurrency}
            error={fieldErrors.sendConcurrency}
            disabled={saving}
            onChange={(v) => set("sendConcurrency", v)}
          />
          <NumberField
            id="ict-send-batch"
            label="Lote"
            hint="1 a 5000"
            value={form.sendBatchSize}
            error={fieldErrors.sendBatchSize}
            disabled={saving}
            onChange={(v) => set("sendBatchSize", v)}
          />
        </div>
      </section>

      <section className={CARD}>
        <SectionHeader
          title="Notificaciones a gestores"
          description="Avisos webhook cuando cambia el estado del pre-trámite."
        />
        <div className="grid gap-4 sm:grid-cols-2 xl:max-w-xl">
          <NumberField
            id="ict-webhook-poll"
            label="Intervalo (segundos)"
            hint="1 a 3600"
            value={form.webhookPollSeconds}
            error={fieldErrors.webhookPollSeconds}
            disabled={saving}
            onChange={(v) => set("webhookPollSeconds", v)}
          />
          <NumberField
            id="ict-webhook-batch"
            label="Lote"
            hint="1 a 5000"
            value={form.webhookBatchSize}
            error={fieldErrors.webhookBatchSize}
            disabled={saving}
            onChange={(v) => set("webhookBatchSize", v)}
          />
        </div>
      </section>

      {saveError ? (
        <InlineAlert tone="error">{saveError}</InlineAlert>
      ) : null}

      <div className="flex justify-end pb-2">
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

function NumberField({
  id,
  label,
  hint,
  value,
  error,
  disabled,
  onChange,
}: {
  id: string;
  label: string;
  hint?: string;
  value: number;
  error?: string;
  disabled: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <div className="min-w-0">
      <label htmlFor={id} className={WIZARD_LABEL}>
        {label}
      </label>
      <input
        id={id}
        type="text"
        inputMode="numeric"
        pattern="[0-9]*"
        autoComplete="off"
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : hint ? `${id}-hint` : undefined}
        value={value}
        disabled={disabled}
        onChange={(e) => {
          const raw = digitsOnly(e.target.value);
          if (raw === "") {
            onChange(0);
            return;
          }
          const n = Number.parseInt(raw, 10);
          onChange(Number.isFinite(n) ? n : value);
        }}
        className={`mt-1 ${WIZARD_INPUT}`}
      />
      {error ? (
        <p id={`${id}-error`} className={WIZARD_HINT} style={{ color: "#FF4E00" }}>
          {error}
        </p>
      ) : hint ? (
        <p id={`${id}-hint`} className={WIZARD_HINT}>
          {hint}
        </p>
      ) : null}
    </div>
  );
}
