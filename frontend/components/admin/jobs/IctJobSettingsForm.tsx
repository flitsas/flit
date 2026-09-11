"use client";

// HU #12123 — formulario SuperAdmin de ict.job_settings. Espejo de QuipuxSettingsForm:
// 4 estados (vacío/carga/error/lleno), clamps en UI alineados al API, sin secretos.
import { useEffect, useState } from "react";
import { Send } from "lucide-react";
import { useToast } from "@/components/admin/Toast";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
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

export function IctJobSettingsForm() {
  const { show } = useToast();

  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<IctJobSettingsFieldErrors>({});
  const [form, setForm] = useState<IctJobSettingsWrite | null>(null);
  const [updatedAt, setUpdatedAt] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void fetchIctJobSettings(controller.signal)
      .then((settings) => {
        if (controller.signal.aborted) return;
        applySettings(settings);
        setLoading(false);
      })
      .catch(() => {
        if (controller.signal.aborted) return;
        setLoadError(true);
        setLoading(false);
      });
    return () => controller.abort();
  }, []);

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
      <div
        className="flex items-center justify-center py-16"
        role="status"
        aria-busy="true"
        aria-live="polite"
      >
        <span className="sr-only">Cargando configuración ICT…</span>
        <div
          className="h-10 w-10 animate-spin rounded-full border-2 border-t-transparent"
          style={{ borderColor: "#557EFF", borderTopColor: "transparent" }}
          aria-hidden="true"
        />
      </div>
    );
  }

  if (loadError || !form) {
    return (
      <p role="alert" className="text-sm" style={{ color: "#FF4E00" }}>
        No se pudo cargar la configuración ICT. Recarga la página para reintentar.
      </p>
    );
  }

  const isEmpty = updatedAt == null;

  return (
    <div className="space-y-6">
      {isEmpty && (
        <p
          role="status"
          className="rounded-xl px-3 py-2 text-[11px]"
          style={{ background: "#EEF3FF", color: "#1E3A8A", border: "1px solid #C5D4FF" }}
        >
          Aún no hay una fila persistida. Se muestran los valores por defecto; al guardar se
          crea <code>ict.job_settings</code>.
        </p>
      )}

      <p className="text-xs opacity-70">
        Cadencia y lotes del pipeline ICT (America/Bogota). Los cambios aplican en caliente, sin
        redeploy.{" "}
        <a href={ICT_JOBS_REPORT_HREF} className="font-semibold underline" style={{ color: "#557EFF" }}>
          Ver últimas corridas (Reportes ICT → Jobs)
        </a>
      </p>

      <Section title="Ventana horaria (America/Bogota)">
        <NumberField
          id="ict-window-start"
          label="Hora inicio (inclusiva)"
          value={form.windowStartHour}
          error={fieldErrors.windowStartHour}
          disabled={saving}
          onChange={(v) => set("windowStartHour", v)}
        />
        <NumberField
          id="ict-window-end"
          label="Hora fin (exclusiva)"
          value={form.windowEndHour}
          error={fieldErrors.windowEndHour}
          disabled={saving}
          onChange={(v) => set("windowEndHour", v)}
        />
      </Section>

      <Section title="Validación de negocio">
        <NumberField
          id="ict-business-poll"
          label="Intervalo (segundos)"
          value={form.businessPollSeconds}
          error={fieldErrors.businessPollSeconds}
          disabled={saving}
          onChange={(v) => set("businessPollSeconds", v)}
        />
        <NumberField
          id="ict-business-batch"
          label="Lote"
          value={form.businessBatchSize}
          error={fieldErrors.businessBatchSize}
          disabled={saving}
          onChange={(v) => set("businessBatchSize", v)}
        />
      </Section>

      <Section title="Validación externa (fuentes)">
        <NumberField
          id="ict-external-poll"
          label="Intervalo (segundos)"
          value={form.externalPollSeconds}
          error={fieldErrors.externalPollSeconds}
          disabled={saving}
          onChange={(v) => set("externalPollSeconds", v)}
        />
        <NumberField
          id="ict-external-batch"
          label="Lote"
          value={form.externalBatchSize}
          error={fieldErrors.externalBatchSize}
          disabled={saving}
          onChange={(v) => set("externalBatchSize", v)}
        />
      </Section>

      <Section title="Orchestrator (consultas RUNT/familia vía proveedor)">
        <NumberField
          id="ict-orch-poll"
          label="Intervalo (segundos)"
          value={form.orchestratorPollSeconds}
          error={fieldErrors.orchestratorPollSeconds}
          disabled={saving}
          onChange={(v) => set("orchestratorPollSeconds", v)}
        />
        <NumberField
          id="ict-orch-conc"
          label="Concurrencia"
          value={form.orchestratorConcurrency}
          error={fieldErrors.orchestratorConcurrency}
          disabled={saving}
          onChange={(v) => set("orchestratorConcurrency", v)}
        />
        <NumberField
          id="ict-orch-batch"
          label="Lote"
          value={form.orchestratorBatchSize}
          error={fieldErrors.orchestratorBatchSize}
          disabled={saving}
          onChange={(v) => set("orchestratorBatchSize", v)}
        />
      </Section>

      <Section title="Envío a core-api">
        <NumberField
          id="ict-send-poll"
          label="Intervalo (segundos)"
          value={form.sendPollSeconds}
          error={fieldErrors.sendPollSeconds}
          disabled={saving}
          onChange={(v) => set("sendPollSeconds", v)}
        />
        <NumberField
          id="ict-send-conc"
          label="Concurrencia"
          value={form.sendConcurrency}
          error={fieldErrors.sendConcurrency}
          disabled={saving}
          onChange={(v) => set("sendConcurrency", v)}
        />
        <NumberField
          id="ict-send-batch"
          label="Lote"
          value={form.sendBatchSize}
          error={fieldErrors.sendBatchSize}
          disabled={saving}
          onChange={(v) => set("sendBatchSize", v)}
        />
      </Section>

      <Section title="Webhooks al gestor">
        <NumberField
          id="ict-webhook-poll"
          label="Intervalo (segundos)"
          value={form.webhookPollSeconds}
          error={fieldErrors.webhookPollSeconds}
          disabled={saving}
          onChange={(v) => set("webhookPollSeconds", v)}
        />
        <NumberField
          id="ict-webhook-batch"
          label="Lote"
          value={form.webhookBatchSize}
          error={fieldErrors.webhookBatchSize}
          disabled={saving}
          onChange={(v) => set("webhookBatchSize", v)}
        />
      </Section>

      {saveError && (
        <p role="alert" className="text-sm" style={{ color: "#FF4E00" }}>
          {saveError}
        </p>
      )}

      <div className="flex items-center justify-end gap-3">
        <button
          type="button"
          onClick={() => void save()}
          disabled={saving}
          className="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold text-white disabled:opacity-60"
          style={{ background: "#557EFF" }}
        >
          <Send className="h-4 w-4" aria-hidden="true" />
          {saving ? "Guardando…" : "Guardar configuración"}
        </button>
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="space-y-4 rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
      <h2 className="text-sm font-semibold">{title}</h2>
      <div className="grid gap-4 sm:grid-cols-2">{children}</div>
    </section>
  );
}

function NumberField({
  id,
  label,
  value,
  error,
  disabled,
  onChange,
}: {
  id: string;
  label: string;
  value: number;
  error?: string;
  disabled: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <div>
      <label htmlFor={id} className="text-xs font-semibold">
        {label}
      </label>
      <input
        id={id}
        type="text"
        inputMode="numeric"
        pattern="[0-9]*"
        autoComplete="off"
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : undefined}
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
        className={`mt-1 ${OT_INPUT_CLS}`}
      />
      {error && (
        <p id={`${id}-error`} className="mt-1 text-[11px]" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}
