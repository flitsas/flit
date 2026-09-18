"use client";

import { useId, useMemo } from "react";
import { Check, X } from "lucide-react";
import { contrastRatio, formatContrastRatio, isValidHexColor, meetsMinimumContrast } from "@/lib/brand/contrast";
import type { BrandColors } from "@/lib/api/branding";

export interface BrandingColorPickerProps {
  colors: BrandColors;
  onChange: (colors: BrandColors) => void;
  disabled?: boolean;
}

const FIELDS: Array<{ key: keyof BrandColors; label: string; hint: string }> = [
  { key: "primary", label: "Color principal", hint: "Fondo de botones y elementos de marca" },
  { key: "secondary", label: "Color secundario", hint: "Acentos y degradados" },
  { key: "onPrimary", label: "Color de texto", hint: "Texto sobre el color principal" },
];

/** HU #12414 AC3 — selector de colores con contraste WCAG en vivo. */
export function BrandingColorPicker({ colors, onChange, disabled = false }: BrandingColorPickerProps) {
  const baseId = useId();

  const ratio = useMemo(() => {
    if (!isValidHexColor(colors.primary) || !isValidHexColor(colors.onPrimary)) return null;
    return contrastRatio(colors.primary, colors.onPrimary);
  }, [colors.primary, colors.onPrimary]);

  const contrastOk = ratio !== null && meetsMinimumContrast(ratio);

  function handleFieldChange(key: keyof BrandColors, value: string) {
    onChange({ ...colors, [key]: value });
  }

  return (
    <fieldset className="flex flex-col gap-4" disabled={disabled}>
      <legend className="text-sm font-bold" style={{ color: "#162744" }}>
        Colores de marca
      </legend>

      {FIELDS.map((field) => {
        const inputId = `${baseId}-${field.key}`;
        const value = colors[field.key] ?? "#000000";
        const valid = isValidHexColor(value);
        return (
          <div key={field.key} className="flex flex-col gap-1">
            <label htmlFor={inputId} className="text-xs font-semibold" style={{ color: "#162744" }}>
              {field.label}
            </label>
            <p className="text-[11px] opacity-60">{field.hint}</p>
            <div className="flex items-center gap-2">
              <input
                type="color"
                aria-label={`Selector visual de ${field.label.toLowerCase()}`}
                value={valid ? value : "#000000"}
                onChange={(e) => handleFieldChange(field.key, e.target.value.toUpperCase())}
                className="h-9 w-9 shrink-0 cursor-pointer rounded-lg border p-0.5"
                style={{ borderColor: "#DFE5ED" }}
              />
              <input
                id={inputId}
                type="text"
                inputMode="text"
                value={value}
                onChange={(e) => handleFieldChange(field.key, e.target.value.toUpperCase())}
                placeholder="#RRGGBB"
                aria-invalid={!valid}
                className="w-32 rounded-lg border px-2.5 py-1.5 font-mono text-xs"
                style={{ borderColor: valid ? "#DFE5ED" : "#FF4E00" }}
              />
              {!valid && (
                <span role="alert" className="text-[11px]" style={{ color: "#FF4E00" }}>
                  Formato inválido (#RRGGBB)
                </span>
              )}
            </div>
          </div>
        );
      })}

      <div
        className="flex items-center gap-2 rounded-xl border px-3 py-2 text-xs"
        style={{
          borderColor: contrastOk ? "#8CC63F" : "#F9AC00",
          background: contrastOk ? "rgba(140,198,63,0.08)" : "rgba(249,172,0,0.08)",
        }}
        role="status"
      >
        {contrastOk ? (
          <Check className="h-4 w-4 shrink-0" style={{ color: "#8CC63F" }} aria-hidden />
        ) : (
          <X className="h-4 w-4 shrink-0" style={{ color: "#F9AC00" }} aria-hidden />
        )}
        <span style={{ color: "#162744" }}>
          {ratio !== null ? (
            <>
              Contraste texto/principal: <strong>{formatContrastRatio(ratio)}:1</strong>{" "}
              {contrastOk ? "cumple el mínimo (4.5:1)." : "no cumple el mínimo (4.5:1)."}
            </>
          ) : (
            "Introduce colores válidos para calcular el contraste."
          )}
        </span>
      </div>
      {!contrastOk && (
        <p className="text-[11px] opacity-70">
          Puedes guardar el borrador con este contraste, pero no podrás publicarlo hasta corregirlo.
        </p>
      )}
    </fieldset>
  );
}
