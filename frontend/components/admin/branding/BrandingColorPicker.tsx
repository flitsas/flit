"use client";

import { useId, useMemo } from "react";
import { AlertTriangle, Check, Wand2 } from "lucide-react";
import { brandContrastPairs, formatContrastRatio, isValidHexColor } from "@/lib/brand/contrast";
import { brandColorsReadable, fixContrast, pickOnPrimary } from "@/lib/brand/derive-tokens";
import type { BrandColors } from "@/lib/api/branding";
import { BRAND_COLOR_PRESETS } from "./brand-color-presets";

export interface BrandingColorPickerProps {
  colors: BrandColors;
  onChange: (colors: BrandColors) => void;
  disabled?: boolean;
}

/**
 * Lo que se pide se nombra por DÓNDE se ve, no por su rol técnico: `secondary` es el color de
 * acción (botones, menú activo, enlaces) y `primary` el de títulos y encabezados, que es como los
 * consumen los tokens (`--color-flit-brand` / `--color-flit-primary`) y el correo.
 */
const FIELDS: Array<{ key: "secondary" | "primary"; label: string; hint: string }> = [
  { key: "secondary", label: "Color de botones y menú", hint: "Botones, opción activa del menú, enlaces y selecciones." },
  { key: "primary", label: "Color de títulos y encabezados", hint: "Títulos, encabezado y cabecera de los correos." },
];

/**
 * HU #12414 AC3 — colores de marca sin tecnicismos: combinaciones sugeridas de un clic, el color
 * del texto se elige solo (blanco u oscuro, el que mejor se lea) y, si una combinación no se
 * lee bien, se ofrece corregirla automáticamente. El detalle de contraste (WCAG 4.5:1, el mismo
 * que valida el backend) queda plegado para quien lo quiera ver.
 */
export function BrandingColorPicker({ colors, onChange, disabled = false }: BrandingColorPickerProps) {
  const baseId = useId();
  const pairs = useMemo(() => brandContrastPairs(colors), [colors]);
  const valid = [colors.primary, colors.secondary, colors.onPrimary].every(isValidHexColor);
  const readable = brandColorsReadable(colors);

  function setColors(next: { primary: string; secondary: string }) {
    // El texto acompaña a los colores: así nadie tiene que elegirlo ni pensar en su contraste.
    onChange({ ...next, onPrimary: pickOnPrimary(next.primary, next.secondary) });
  }

  function handleFieldChange(key: "primary" | "secondary", value: string) {
    setColors({ primary: colors.primary, secondary: colors.secondary, [key]: value.toUpperCase() });
  }

  const activePreset = BRAND_COLOR_PRESETS.find(
    (p) => p.primary === colors.primary.toUpperCase() && p.secondary === colors.secondary.toUpperCase(),
  );

  return (
    <fieldset className="flex flex-col gap-4" disabled={disabled}>
      <legend className="text-sm font-bold text-flit-primary">Colores de tu marca</legend>

      <div className="flex flex-col gap-2">
        <p className="text-xs font-semibold text-flit-primary">Empieza con una combinación</p>
        <div className="flex flex-wrap gap-2" role="group" aria-label="Combinaciones sugeridas">
          {BRAND_COLOR_PRESETS.map((preset) => {
            const selected = activePreset?.name === preset.name;
            return (
              <button
                key={preset.name}
                type="button"
                onClick={() => setColors({ primary: preset.primary, secondary: preset.secondary })}
                aria-pressed={selected}
                aria-label={`Combinación ${preset.name}`}
                className="flex items-center gap-1.5 rounded-full border bg-white px-2.5 py-1 text-[11px] font-medium transition hover:shadow-sm"
                style={{ borderColor: selected ? preset.secondary : "#DFE5ED", boxShadow: selected ? `0 0 0 1px ${preset.secondary}` : undefined }}
              >
                <span className="flex" aria-hidden>
                  <span className="h-3.5 w-3.5 rounded-full border border-white" style={{ background: preset.secondary }} />
                  <span className="-ml-1 h-3.5 w-3.5 rounded-full border border-white" style={{ background: preset.primary }} />
                </span>
                {preset.name}
                {selected && <Check className="h-3 w-3" aria-hidden />}
              </button>
            );
          })}
        </div>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        {FIELDS.map((field) => {
          const inputId = `${baseId}-${field.key}`;
          const value = colors[field.key] ?? "#000000";
          const fieldValid = isValidHexColor(value);
          return (
            <div key={field.key} className="flex flex-col gap-1.5 rounded-xl border bg-white p-3" style={{ borderColor: "#DFE5ED" }}>
              <span className="text-xs font-semibold text-flit-primary">{field.label}</span>
              <p className="text-[11px] opacity-60">{field.hint}</p>
              <div className="flex items-center gap-2">
                <label className="relative block h-10 w-10 shrink-0 cursor-pointer overflow-hidden rounded-xl border" style={{ borderColor: "#DFE5ED", background: fieldValid ? value : "#FFFFFF" }}>
                  <span className="sr-only">Elegir {field.label.toLowerCase()}</span>
                  <input
                    type="color"
                    value={fieldValid ? value : "#000000"}
                    onChange={(e) => handleFieldChange(field.key, e.target.value)}
                    className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
                  />
                </label>
                <div className="flex flex-col">
                  <label htmlFor={inputId} className="text-[10px] uppercase tracking-wide opacity-60">
                    Código
                  </label>
                  <input
                    id={inputId}
                    type="text"
                    inputMode="text"
                    value={value}
                    onChange={(e) => handleFieldChange(field.key, e.target.value)}
                    placeholder="#RRGGBB"
                    aria-invalid={!fieldValid}
                    aria-label={`Código de ${field.label.toLowerCase()}`}
                    className="w-28 rounded-lg border px-2 py-1 font-mono text-xs"
                    style={{ borderColor: fieldValid ? "#DFE5ED" : "#FF4E00" }}
                  />
                </div>
              </div>
              {!fieldValid && (
                <span role="alert" className="text-[11px]" style={{ color: "#FF4E00" }}>
                  Escribe el código con el formato #RRGGBB, por ejemplo #2563EB.
                </span>
              )}
            </div>
          );
        })}
      </div>

      {valid && (
        <div
          className="flex flex-col gap-2 rounded-xl border px-3 py-2.5 text-xs"
          style={{
            borderColor: readable ? "#8CC63F" : "#F9AC00",
            background: readable ? "rgba(140,198,63,0.08)" : "rgba(249,172,0,0.08)",
          }}
          role="status"
        >
          <div className="flex items-start gap-2">
            {readable ? (
              <Check className="mt-0.5 h-4 w-4 shrink-0" style={{ color: "#4F7A12" }} aria-hidden />
            ) : (
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" style={{ color: "#8A6000" }} aria-hidden />
            )}
            <span className="text-flit-primary">
              {readable
                ? `Se lee bien: el texto sobre tus colores irá en ${colors.onPrimary.toUpperCase() === "#FFFFFF" ? "blanco" : "oscuro"}.`
                : "Con estos colores el texto de los botones no se leería bien. Puedes corregirlo con un clic: ajustamos el tono sin cambiar el color."}
            </span>
          </div>
          {!readable && (
            <button
              type="button"
              onClick={() => onChange(fixContrast(colors))}
              className="inline-flex w-fit items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold text-white"
              style={{ background: "#8A6000" }}
            >
              <Wand2 className="h-3.5 w-3.5" aria-hidden /> Ajustar automáticamente
            </button>
          )}
          <details className="text-[11px] opacity-70">
            <summary className="cursor-pointer select-none">Detalle técnico</summary>
            <ul className="mt-1 space-y-0.5">
              {pairs.map((p) => (
                <li key={p.label}>
                  Contraste {p.label}: {p.ratio === null ? "—" : `${formatContrastRatio(p.ratio)}:1`}{" "}
                  {p.ok ? "(cumple el mínimo 4.5:1)" : "(no cumple el mínimo 4.5:1)"}
                </li>
              ))}
            </ul>
          </details>
        </div>
      )}
    </fieldset>
  );
}
