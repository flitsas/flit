"use client";

import { useState } from "react";
import { Loader2 } from "lucide-react";
import type {
  ReportSchedule,
  ReportScheduleInput,
  ReportType,
  ScheduleFormat,
  ScheduleFrequency,
} from "@/lib/api/analytics-scheduling";
import {
  DAY_OF_WEEK_LABELS,
  FORMAT_LABELS,
  FREQUENCY_LABELS,
  REPORT_TYPE_LABELS,
  validateName,
  validateRecipients,
} from "./labels";
import { RecipientsInput } from "./RecipientsInput";
import { digitsOnly } from "@/lib/format/currency";

interface ScheduleFormProps {
  /** Schedule a editar; undefined = crear. */
  initial?: ReportSchedule;
  onSubmit: (input: ReportScheduleInput) => Promise<void>;
  onCancel: () => void;
}

const inputClass =
  "w-full rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 " +
  "px-3 py-2 text-sm text-[#162744] dark:text-slate-100 outline-none focus:ring-2 focus:ring-[#557EFF]/40";

const labelClass = "block text-xs font-semibold text-[#162744] dark:text-slate-200 mb-1";

/**
 * Formulario de creación/edición de un informe programado (Reportes 2.0, HU-D):
 * nombre, tipo, periodicidad con día condicional, hora (Bogotá), formato, destinatarios
 * y activo. Validación client-side espejo del backend; mensajes en español.
 */
export function ScheduleForm({ initial, onSubmit, onCancel }: ScheduleFormProps) {
  const [name, setName] = useState(initial?.name ?? "");
  const [reportType, setReportType] = useState<ReportType>(initial?.reportType ?? "resumen");
  const [frequency, setFrequency] = useState<ScheduleFrequency>(initial?.frequency ?? "daily");
  const [dayOfWeek, setDayOfWeek] = useState<number>(initial?.dayOfWeek ?? 1);
  const [dayOfMonth, setDayOfMonth] = useState<number>(initial?.dayOfMonth ?? 1);
  const [sendHour, setSendHour] = useState<number>(initial?.sendHour ?? 7);
  const [format, setFormat] = useState<ScheduleFormat>(initial?.format ?? "pdf");
  const [recipients, setRecipients] = useState<string[]>(initial?.recipients ?? []);
  const [isActive, setIsActive] = useState<boolean>(initial?.isActive ?? true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const nameError = validateName(name);
    if (nameError) {
      setError(nameError);
      return;
    }
    const recipientsError = validateRecipients(recipients);
    if (recipientsError) {
      setError(recipientsError);
      return;
    }

    const input: ReportScheduleInput = {
      name: name.trim(),
      reportType,
      frequency,
      dayOfWeek: frequency === "weekly" ? dayOfWeek : null,
      dayOfMonth: frequency === "monthly" ? dayOfMonth : null,
      sendHour,
      format,
      recipients,
      isActive,
    };

    setSaving(true);
    setError(null);
    try {
      await onSubmit(input);
    } catch {
      setError("No se pudo guardar el informe programado. Inténtalo de nuevo.");
      setSaving(false);
    }
  }

  return (
    <form
      onSubmit={handleSubmit}
      data-testid="schedule-form"
      className="rounded-2xl border border-slate-200 dark:border-slate-700 p-4 space-y-3"
    >
      <h4 className="text-sm font-bold text-[#162744] dark:text-slate-100">
        {initial ? "Editar informe programado" : "Nuevo informe programado"}
      </h4>

      <div>
        <label htmlFor="schedule-name" className={labelClass}>Nombre</label>
        <input
          id="schedule-name"
          type="text"
          value={name}
          maxLength={120}
          onChange={(e) => setName(e.target.value)}
          className={inputClass}
          placeholder="Ej. Informe semanal OT"
        />
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <div>
          <label htmlFor="schedule-type" className={labelClass}>Tipo de informe</label>
          <select
            id="schedule-type"
            value={reportType}
            onChange={(e) => setReportType(e.target.value as ReportType)}
            className={inputClass}
          >
            {Object.entries(REPORT_TYPE_LABELS).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="schedule-frequency" className={labelClass}>Periodicidad</label>
          <select
            id="schedule-frequency"
            value={frequency}
            onChange={(e) => setFrequency(e.target.value as ScheduleFrequency)}
            className={inputClass}
          >
            {Object.entries(FREQUENCY_LABELS).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
        </div>

        {frequency === "weekly" && (
          <div>
            <label htmlFor="schedule-day-of-week" className={labelClass}>Día de la semana</label>
            <select
              id="schedule-day-of-week"
              value={dayOfWeek}
              onChange={(e) => setDayOfWeek(Number(e.target.value))}
              className={inputClass}
            >
              {DAY_OF_WEEK_LABELS.map((label, i) => (
                <option key={label} value={i}>{label}</option>
              ))}
            </select>
          </div>
        )}

        {frequency === "monthly" && (
          <div>
            <label htmlFor="schedule-day-of-month" className={labelClass}>Día del mes (1-28)</label>
            <input
              id="schedule-day-of-month"
              type="text"
              inputMode="numeric"
              pattern="[0-9]*"
              autoComplete="off"
              value={dayOfMonth}
              onChange={(e) => {
                const raw = digitsOnly(e.target.value);
                setDayOfMonth(raw === "" ? 1 : Number(raw));
              }}
              className={inputClass}
            />
          </div>
        )}

        <div>
          <label htmlFor="schedule-hour" className={labelClass}>Hora de envío (Bogotá)</label>
          <select
            id="schedule-hour"
            value={sendHour}
            onChange={(e) => setSendHour(Number(e.target.value))}
            className={inputClass}
          >
            {Array.from({ length: 24 }, (_, h) => (
              <option key={h} value={h}>{String(h).padStart(2, "0")}:00</option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="schedule-format" className={labelClass}>Formato</label>
          <select
            id="schedule-format"
            value={format}
            onChange={(e) => setFormat(e.target.value as ScheduleFormat)}
            className={inputClass}
          >
            {Object.entries(FORMAT_LABELS).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
          <p className="mt-1 text-[11px] text-slate-500 dark:text-slate-400">
            El correo incluye el resumen de indicadores; el adjunto llegará en versiones futuras.
          </p>
        </div>
      </div>

      <div>
        <span className={labelClass}>Destinatarios</span>
        <RecipientsInput value={recipients} onChange={setRecipients} />
      </div>

      <label className="flex items-center gap-2 text-sm text-[#162744] dark:text-slate-200">
        <input
          type="checkbox"
          checked={isActive}
          onChange={(e) => setIsActive(e.target.checked)}
          className="h-4 w-4 accent-[#557EFF]"
        />
        Activo
      </label>

      {error && (
        <p role="alert" data-testid="schedule-form-error" className="text-xs font-medium text-red-600 dark:text-red-400">
          {error}
        </p>
      )}

      <div className="flex items-center gap-2 pt-1">
        <button
          type="submit"
          disabled={saving}
          aria-busy={saving}
          className="flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
          style={{ background: "#557EFF" }}
        >
          {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
          {initial ? "Guardar cambios" : "Crear informe"}
        </button>
        <button
          type="button"
          onClick={onCancel}
          className="rounded-xl border border-slate-200 dark:border-slate-700 px-4 py-2 text-xs font-semibold text-[#162744] dark:text-slate-200"
        >
          Cancelar
        </button>
      </div>
    </form>
  );
}
