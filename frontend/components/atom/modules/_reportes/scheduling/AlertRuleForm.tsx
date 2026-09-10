"use client";

import { useState } from "react";
import { Loader2 } from "lucide-react";
import type {
  AlertMetric,
  AlertOperator,
  AlertRule,
  AlertRuleInput,
} from "@/lib/api/analytics-scheduling";
import {
  METRIC_DESCRIPTIONS,
  METRIC_LABELS,
  OPERATOR_LABELS,
  validateName,
  validateRecipients,
} from "./labels";
import { RecipientsInput } from "./RecipientsInput";
import { digitsOnly, sanitizeDecimalInput } from "@/lib/format/currency";

interface AlertRuleFormProps {
  /** Regla a editar; undefined = crear. */
  initial?: AlertRule;
  /** Restringe el selector "Métrica" (p. ej. el panel del organismo solo ofrece las 2 métricas
   * de alcance OT). Por defecto, todas las métricas soportadas. */
  allowedMetrics?: AlertMetric[];
  onSubmit: (input: AlertRuleInput) => Promise<void>;
  onCancel: () => void;
}

const inputClass =
  "w-full rounded-xl border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900 " +
  "px-3 py-2 text-sm text-[#162744] dark:text-slate-100 outline-none focus:ring-2 focus:ring-[#557EFF]/40";

const labelClass = "block text-xs font-semibold text-[#162744] dark:text-slate-200 mb-1";

/**
 * Formulario de creación/edición de una regla de alerta (Reportes 2.0, HU-D): métrica con
 * etiqueta y descripción en español, operador, umbral, ventana (5-43200 min), cooldown
 * (5-10080 min), destinatarios y activo. Validación espejo del backend.
 */
export function AlertRuleForm({ initial, allowedMetrics, onSubmit, onCancel }: AlertRuleFormProps) {
  const [name, setName] = useState(initial?.name ?? "");
  const [metric, setMetric] = useState<AlertMetric>(
    initial?.metric ?? allowedMetrics?.[0] ?? "rejection_rate_pct",
  );
  const [operator, setOperator] = useState<AlertOperator>(initial?.operator ?? "gt");
  const [threshold, setThreshold] = useState<string>(initial ? String(initial.threshold) : "");
  const [windowMinutes, setWindowMinutes] = useState<number>(initial?.windowMinutes ?? 1440);
  const [cooldownMinutes, setCooldownMinutes] = useState<number>(initial?.cooldownMinutes ?? 240);
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
    const thresholdValue = Number(threshold);
    if (threshold.trim() === "" || Number.isNaN(thresholdValue)) {
      setError("El umbral es obligatorio.");
      return;
    }
    if (windowMinutes < 5 || windowMinutes > 43_200) {
      setError("La ventana de evaluación debe estar entre 5 y 43200 minutos.");
      return;
    }
    if (cooldownMinutes < 5 || cooldownMinutes > 10_080) {
      setError("El periodo de enfriamiento debe estar entre 5 y 10080 minutos.");
      return;
    }
    const recipientsError = validateRecipients(recipients);
    if (recipientsError) {
      setError(recipientsError);
      return;
    }

    const input: AlertRuleInput = {
      name: name.trim(),
      metric,
      operator,
      threshold: thresholdValue,
      windowMinutes,
      cooldownMinutes,
      recipients,
      isActive,
    };

    setSaving(true);
    setError(null);
    try {
      await onSubmit(input);
    } catch {
      setError("No se pudo guardar la regla de alerta. Inténtalo de nuevo.");
      setSaving(false);
    }
  }

  return (
    <form
      onSubmit={handleSubmit}
      data-testid="alert-rule-form"
      className="rounded-2xl border border-slate-200 dark:border-slate-700 p-4 space-y-3"
    >
      <h4 className="text-sm font-bold text-[#162744] dark:text-slate-100">
        {initial ? "Editar alerta" : "Nueva alerta"}
      </h4>

      <div>
        <label htmlFor="alert-name" className={labelClass}>Nombre</label>
        <input
          id="alert-name"
          type="text"
          value={name}
          maxLength={120}
          onChange={(e) => setName(e.target.value)}
          className={inputClass}
          placeholder="Ej. Rechazo OT alto"
        />
      </div>

      <div>
        <label htmlFor="alert-metric" className={labelClass}>Métrica</label>
        <select
          id="alert-metric"
          value={metric}
          onChange={(e) => setMetric(e.target.value as AlertMetric)}
          className={inputClass}
          title={METRIC_DESCRIPTIONS[metric]}
        >
          {Object.entries(METRIC_LABELS)
            .filter(([value]) => !allowedMetrics || allowedMetrics.includes(value as AlertMetric))
            .map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
        </select>
        <p className="mt-1 text-[11px] text-slate-500 dark:text-slate-400" data-testid="metric-description">
          {METRIC_DESCRIPTIONS[metric]}
        </p>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <div>
          <label htmlFor="alert-operator" className={labelClass}>Operador</label>
          <select
            id="alert-operator"
            value={operator}
            onChange={(e) => setOperator(e.target.value as AlertOperator)}
            className={inputClass}
          >
            {Object.entries(OPERATOR_LABELS).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor="alert-threshold" className={labelClass}>Umbral</label>
          <input
            id="alert-threshold"
            type="text"
            inputMode="decimal"
            autoComplete="off"
            value={threshold}
            onChange={(e) => setThreshold(sanitizeDecimalInput(e.target.value))}
            className={inputClass}
            placeholder="Ej. 25"
          />
        </div>
        <div>
          <label htmlFor="alert-window" className={labelClass}>Ventana de evaluación (minutos)</label>
          <input
            id="alert-window"
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            value={windowMinutes}
            onChange={(e) => {
              const raw = digitsOnly(e.target.value);
              setWindowMinutes(raw === "" ? 5 : Number(raw));
            }}
            className={inputClass}
            title="Periodo hacia atrás sobre el que se calcula la métrica."
          />
        </div>
        <div>
          <label htmlFor="alert-cooldown" className={labelClass}>Enfriamiento / cooldown (minutos)</label>
          <input
            id="alert-cooldown"
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            value={cooldownMinutes}
            onChange={(e) => {
              const raw = digitsOnly(e.target.value);
              setCooldownMinutes(raw === "" ? 5 : Number(raw));
            }}
            className={inputClass}
            title="Tiempo mínimo entre disparos consecutivos de esta alerta."
          />
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
        Activa
      </label>

      {error && (
        <p role="alert" data-testid="alert-form-error" className="text-xs font-medium text-red-600 dark:text-red-400">
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
          {initial ? "Guardar cambios" : "Crear alerta"}
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
