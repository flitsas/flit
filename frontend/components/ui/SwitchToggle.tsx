"use client";

// Switch inline (píldora) accesible para activar/desactivar en línea (grillas de compañías
// y tipos de documento). Misma apariencia que el ToggleSwitch de configuración (pista
// h-5 w-10 teal #00DBD5 encendido / gris #DFE5ED apagado), pero SIN caja ni etiqueta visible:
// solo el control, con nombre accesible vía `label` (aria-label). Implementado como checkbox
// visualmente oculto para conservar semántica (role="switch") y foco por teclado.
export interface SwitchToggleProps {
  checked: boolean;
  onChange: (next: boolean) => void;
  /** Nombre accesible del control (p.ej. "Desactivar FLIT SAS"). También se usa como tooltip. */
  label: string;
  disabled?: boolean;
  id?: string;
}

export function SwitchToggle({ checked, onChange, label, disabled = false, id }: SwitchToggleProps) {
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
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="peer sr-only"
      />
      <span
        aria-hidden="true"
        className="h-5 w-10 rounded-full transition peer-focus-visible:ring-2 peer-focus-visible:ring-[#557EFF]"
        style={{ background: checked ? "#00DBD5" : "#DFE5ED" }}
      />
      <span
        aria-hidden="true"
        className="absolute top-0.5 h-4 w-4 rounded-full bg-white transition-all"
        style={{ left: checked ? "calc(100% - 18px)" : "2px" }}
      />
    </label>
  );
}
