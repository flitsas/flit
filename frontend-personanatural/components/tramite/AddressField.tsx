"use client";

import { Info } from "lucide-react";
import { useId, useState } from "react";

export const TEXTO_ENTREGA = "Esta dirección será la dirección de envío por defecto de la tarjeta de propiedad.";

interface Props {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  /** Error de la dirección (se asocia al campo con `aria-invalid` y `aria-describedby`). */
  error?: string;
  /** Muestra el ícono de información de 44 px con la ayuda de entrega del diseño de referencia (AddressField de tramite-traspaso.tsx). */
  entrega?: boolean;
}

/** Campo «Dirección» del diseño de referencia: etiqueta, ícono de ayuda opcional (clic, foco y teclado) y el campo `.field`. */
export function AddressField({ id, label, value, onChange, disabled, error, entrega = false }: Props) {
  const [abierta, setAbierta] = useState(false);
  const idAyuda = useId();
  const idError = `${id}-error`;
  return (
    <div>
      <div className="mb-1 flex items-center gap-2">
        <label htmlFor={id} className="text-sm font-medium">
          {label}
        </label>
        {entrega && (
          <span className="relative">
            <button
              type="button"
              aria-label="Información sobre la dirección de entrega"
              aria-expanded={abierta}
              aria-controls={abierta ? idAyuda : undefined}
              onClick={() => setAbierta((a) => !a)}
              onBlur={() => setAbierta(false)}
              onKeyDown={(e) => {
                if (e.key === "Escape") setAbierta(false);
              }}
              className="grid h-11 w-11 shrink-0 place-items-center rounded-full text-primary hover:bg-primary/5"
            >
              <Info aria-hidden className="h-5 w-5" />
            </button>
            {abierta && (
              <span id={idAyuda} role="tooltip" className="absolute bottom-full left-0 z-20 mb-1 w-[min(380px,calc(100vw-32px))] rounded-[9px] border border-border bg-card p-3 text-sm text-foreground">
                {TEXTO_ENTREGA}
              </span>
            )}
          </span>
        )}
      </div>
      <input
        id={id}
        className="field"
        autoComplete="street-address"
        disabled={disabled}
        value={value}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? idError : undefined}
        onChange={(e) => onChange(e.target.value)}
      />
      {error && (
        <span id={idError} role="alert" className="mt-1 block text-xs text-alert">
          {error}
        </span>
      )}
    </div>
  );
}
