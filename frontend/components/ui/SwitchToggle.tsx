"use client";

// Switch inline (píldora) accesible para activar/desactivar en línea (grillas de compañías
// y tipos de documento). Misma apariencia que el ToggleSwitch de configuración (pista
// h-5 w-10 teal #00DBD5 encendido / gris #DFE5ED apagado), pero SIN caja ni etiqueta visible:
// solo el control, con nombre accesible vía `label` (aria-label). Implementado como checkbox
// visualmente oculto para conservar semántica (role="switch") y foco por teclado.
import { INTERRUPTOR_CLS, INTERRUPTOR_PERILLA_CLS } from "@flit/ui/Interruptor";

export interface SwitchToggleProps {
  checked: boolean;
  onChange: (next: boolean) => void;
  /** Nombre accesible del control (p.ej. "Desactivar FLIT SAS"). También se usa como tooltip. */
  label: string;
  disabled?: boolean;
  id?: string;
  /** Id de un elemento que explica por qué está deshabilitado (aria-describedby). */
  describedById?: string;
}

export function SwitchToggle({
  checked,
  onChange,
  label,
  disabled = false,
  id,
  describedById,
}: SwitchToggleProps) {
  return (
    <label
      title={label}
      className={`relative inline-flex shrink-0 ${disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}
    >
      <input
        id={id}
        type="checkbox"
        role="switch"
        aria-label={label}
        aria-describedby={describedById}
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="peer sr-only"
      />
      <span
        aria-hidden="true"
        className={`${INTERRUPTOR_CLS} peer-checked:[background-image:url('/assets/kit/interruptor-encendido.svg')] peer-focus-visible:ring-2 peer-focus-visible:ring-flit-brand`}
      />
      <span
        aria-hidden="true"
        className={INTERRUPTOR_PERILLA_CLS}
      />
    </label>
  );
}
