"use client";

import {
  CONSULTATION_CONDUCTOR_PROVIDERS,
  CONSULTATION_VEHICLE_PROVIDERS,
  type ConsultationProviderOption,
  type SettingsForm,
} from "./settingsForm";

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

  return (
    <fieldset className="rounded-2xl border p-4">
      <legend className="px-1 text-xs font-semibold">Proveedores de consulta RUNT</legend>
      <p className="mb-3 max-w-md text-[11px] opacity-60">
        Proveedor que resuelve cada consulta al RUNT. Kyverum es el predeterminado; si no responde, la
        consulta cae automáticamente al proveedor de contingencia.
      </p>

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
        <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {configError}
        </p>
      )}

      <div className="mt-4 max-w-xs">
        <label htmlFor="runtFailoverTimeoutMs" className="mb-1 block text-xs font-semibold">
          Timeout de failover (ms)
        </label>
        <input
          id="runtFailoverTimeoutMs"
          type="number"
          min={500}
          max={60000}
          step={100}
          value={form.runtFailoverTimeoutMs}
          onChange={(e) => onChange({ runtFailoverTimeoutMs: e.target.valueAsNumber || 0 })}
          className="w-full rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          style={{ borderColor: timeoutError ? "#FF4E00" : "#DFE5ED" }}
        />
        <p className="mt-1 text-[11px] opacity-60">
          Cuánto espera al proveedor primario antes de intentar con el de contingencia (500–60000 ms).
        </p>
        {timeoutError && (
          <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
            {timeoutError}
          </p>
        )}
      </div>
    </fieldset>
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
      <label htmlFor={id} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      <select
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
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
