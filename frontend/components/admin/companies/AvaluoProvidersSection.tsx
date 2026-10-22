"use client";

import { ClampedText, FIELD_CLASS, FIELD_LABEL, HELP_TEXT, OptionCard } from "./ConfigUi";
import {
  AVALUO_BASE_PROVIDER,
  AVALUO_PROVIDERS,
  type SettingsForm,
} from "./settingsForm";

// Sección "Proveedores de avalúos" (Feature #10707). Se ubica bajo "Proveedores de consulta RUNT".
// Fasecolda es el proveedor base (siempre activo, no se puede desactivar); base gravable y Mercado
// Libre se habilitan por compañía. Un selector define cuál se sugiere por defecto en el paso
// comercial del traspaso. La lista de proveedores sale de AVALUO_PROVIDERS: sumar uno nuevo no
// requiere tocar este componente.
export interface AvaluoProvidersSectionProps {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
  fieldErrors?: Record<string, string>;
}

export function AvaluoProvidersSection({
  form,
  onChange,
  fieldErrors,
}: AvaluoProvidersSectionProps) {
  const configError = fieldErrors?.avaluoProviderConfig;

  const toggle = (value: string, on: boolean) => {
    const next = on
      ? [...form.avaluoEnabled, value]
      : form.avaluoEnabled.filter((v) => v !== value);
    // Si se desactiva el proveedor sugerido, el sugerido cae al proveedor base.
    const nextPrimary = next.includes(form.avaluoPrimary)
      ? form.avaluoPrimary
      : AVALUO_BASE_PROVIDER;
    onChange({ avaluoEnabled: next, avaluoPrimary: nextPrimary });
  };

  // Solo los proveedores habilitados pueden ser el sugerido.
  const primaryOptions = AVALUO_PROVIDERS.filter((p) => form.avaluoEnabled.includes(p.value));

  return (
    <div className="space-y-4">
      <ClampedText text="Fuentes que sugieren el valor comercial del vehículo en el paso de datos comerciales del traspaso. Fasecolda viene activo por defecto; puedes habilitar fuentes adicionales y elegir cuál se sugiere primero." />

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {AVALUO_PROVIDERS.map((provider) => (
          <OptionCard
            key={provider.value}
            type="checkbox"
            label={provider.label}
            description={provider.hint}
            checked={form.avaluoEnabled.includes(provider.value)}
            locked={Boolean(provider.locked)}
            onChange={(on) => toggle(provider.value, on)}
          />
        ))}
      </div>

      {configError && (
        <p className="text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
          {configError}
        </p>
      )}

      <div className="max-w-xs">
        <label htmlFor="avaluoPrimary" className={FIELD_LABEL}>
          Proveedor sugerido por defecto
        </label>
        <select
          id="avaluoPrimary"
          value={form.avaluoPrimary}
          onChange={(e) => onChange({ avaluoPrimary: e.target.value })}
          className={FIELD_CLASS}
          style={{ borderColor: "#DFE5ED" }}
        >
          {primaryOptions.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <p className={`mt-1 ${HELP_TEXT}`}>
          Valor que se propone primero cuando hay varias fuentes disponibles.
        </p>
      </div>
    </div>
  );
}
