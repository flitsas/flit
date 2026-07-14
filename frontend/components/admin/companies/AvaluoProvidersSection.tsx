"use client";

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
    <fieldset className="rounded-2xl border p-4">
      <legend className="px-1 text-xs font-semibold">Proveedores de avalúos</legend>
      <p className="mb-3 max-w-md text-[11px] opacity-60">
        Fuentes que sugieren el valor comercial del vehículo en el paso de datos comerciales del
        traspaso. Fasecolda viene activo por defecto; puedes habilitar fuentes adicionales y elegir
        cuál se sugiere primero.
      </p>

      <div className="flex flex-col gap-2">
        {AVALUO_PROVIDERS.map((provider) => {
          const checked = form.avaluoEnabled.includes(provider.value);
          const inputId = `avaluo-${provider.value}`;
          return (
            <label
              key={provider.value}
              htmlFor={inputId}
              className="flex cursor-pointer items-start gap-2 rounded-xl border px-3 py-2 text-xs"
              style={{ borderColor: configError ? "#FF4E00" : "#DFE5ED" }}
            >
              <input
                id={inputId}
                type="checkbox"
                checked={checked}
                disabled={provider.locked}
                onChange={(e) => toggle(provider.value, e.target.checked)}
                className="mt-0.5 h-4 w-4 accent-[#557EFF] disabled:opacity-60"
              />
              <span className="min-w-0">
                <span className="flex items-center gap-2 font-semibold">
                  {provider.label}
                  {provider.locked && (
                    <span className="rounded-full bg-black/5 px-2 py-0.5 text-[10px] font-medium opacity-70 dark:bg-white/10">
                      Base
                    </span>
                  )}
                </span>
                {provider.hint && (
                  <span className="block text-[11px] opacity-50">{provider.hint}</span>
                )}
              </span>
            </label>
          );
        })}
      </div>

      {configError && (
        <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {configError}
        </p>
      )}

      <div className="mt-4 max-w-xs">
        <label htmlFor="avaluoPrimary" className="mb-1 block text-xs font-semibold">
          Proveedor sugerido por defecto
        </label>
        <select
          id="avaluoPrimary"
          value={form.avaluoPrimary}
          onChange={(e) => onChange({ avaluoPrimary: e.target.value })}
          className="w-full rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF]"
          style={{ borderColor: "#DFE5ED" }}
        >
          {primaryOptions.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <p className="mt-1 text-[11px] opacity-60">
          Valor que se propone primero cuando hay varias fuentes disponibles.
        </p>
      </div>
    </fieldset>
  );
}
