"use client";

import { useState } from "react";
import { SlidersHorizontal } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { Accordion, ClampedText, FIELD_CLASS, FIELD_LABEL, HELP_TEXT } from "./ConfigUi";
import {
  CONSULTATION_CONDUCTOR_PROVIDERS,
  CONSULTATION_VEHICLE_PROVIDERS,
  type ConsultationProviderOption,
  type SettingsForm,
} from "./settingsForm";
import { digitsOnly } from "@/lib/format/currency";

// Sección "Proveedores de consulta RUNT" (HU #10478). Tres selectores (VIN, placa, conductor) con
// Kyverum como default y Verifik como alternativa; Intempo se lista deshabilitado (aún no disponible).
// El fallback se deriva del primario elegido. Un input controla el presupuesto de failover (ms).
export interface ConsultaProvidersSectionProps {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
  fieldErrors?: Record<string, string>;
}

export function ConsultaProvidersSection({
  form,
  onChange,
  fieldErrors,
}: ConsultaProvidersSectionProps) {
  const configError = fieldErrors?.consultationProviderConfig;
  const timeoutError = fieldErrors?.runtFailoverTimeoutMs;

  const [advancedOpen, setAdvancedOpen] = useState(false);
  // Un error en el campo plegado no puede quedar oculto: se fuerza abierto.
  const showAdvanced = advancedOpen || Boolean(timeoutError);

  return (
    <div className="space-y-4">
      <ClampedText text="Proveedor que resuelve cada consulta al RUNT. Kyverum es el predeterminado; si no responde, la consulta cae automáticamente al proveedor de contingencia." />

      <div className="grid gap-4 sm:grid-cols-3">
        <ProviderSelect
          id="consultaVin"
          label="Vehículo por VIN"
          value={form.consultaVin}
          options={CONSULTATION_VEHICLE_PROVIDERS}
          invalid={Boolean(configError)}
          onChange={(v) => onChange({ consultaVin: v })}
        />
        <ProviderSelect
          id="consultaPlaca"
          label="Vehículo por placa"
          value={form.consultaPlaca}
          options={CONSULTATION_VEHICLE_PROVIDERS}
          invalid={Boolean(configError)}
          onChange={(v) => onChange({ consultaPlaca: v })}
        />
        <ProviderSelect
          id="consultaConductor"
          label="Conductor"
          value={form.consultaConductor}
          options={CONSULTATION_CONDUCTOR_PROVIDERS}
          invalid={Boolean(configError)}
          onChange={(v) => onChange({ consultaConductor: v })}
        />
      </div>

      {configError && (
        <p className="text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
          {configError}
        </p>
      )}

      <Accordion
        title="Opciones avanzadas"
        subtitle="Tiempo de espera antes de pasar al proveedor de contingencia."
        open={showAdvanced}
        onToggle={() => setAdvancedOpen((v) => !v)}
        keepMounted
        flat
        bodyClassName="block"
        badge={
          <StatusBadge
            tone="neutral"
            ariaLabel={`Failover ${form.runtFailoverTimeoutMs} milisegundos`}
            label={
              <span className="inline-flex items-center gap-1">
                <SlidersHorizontal className="h-3.5 w-3.5" aria-hidden />
                Failover {form.runtFailoverTimeoutMs} ms
              </span>
            }
          />
        }
      >
        <div className="max-w-xs">
          <label htmlFor="runtFailoverTimeoutMs" className={FIELD_LABEL}>
            Timeout de failover (ms)
          </label>
          <input
            id="runtFailoverTimeoutMs"
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            autoComplete="off"
            value={form.runtFailoverTimeoutMs}
            onChange={(e) => {
              const raw = digitsOnly(e.target.value);
              onChange({ runtFailoverTimeoutMs: raw === "" ? 0 : Number(raw) });
            }}
            className={FIELD_CLASS}
            style={{ borderColor: timeoutError ? "#FF4E00" : "#DFE5ED" }}
          />
          <p className={`mt-1 ${HELP_TEXT}`}>
            Cuánto espera al proveedor primario antes de intentar con el de contingencia (500–60000 ms).
          </p>
          {timeoutError && (
            <p className="mt-1 text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
              {timeoutError}
            </p>
          )}
        </div>
      </Accordion>
    </div>
  );
}

interface ProviderSelectProps {
  id: string;
  label: string;
  value: string;
  options: ConsultationProviderOption[];
  invalid: boolean;
  onChange: (value: string) => void;
}

function ProviderSelect({ id, label, value, options, invalid, onChange }: ProviderSelectProps) {
  return (
    <div>
      <label htmlFor={id} className={FIELD_LABEL}>
        {label}
      </label>
      <select
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className={FIELD_CLASS}
        style={{ borderColor: invalid ? "#FF4E00" : "#DFE5ED" }}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value} disabled={option.disabled}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}
