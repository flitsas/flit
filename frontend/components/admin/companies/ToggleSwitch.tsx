"use client";

// Switch accesible reutilizado en las pestañas de configuración (HU #10194).
// Implementado como checkbox visualmente oculto para conservar semántica y foco.
export interface ToggleSwitchProps {
  id: string;
  label: string;
  description?: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
}

export function ToggleSwitch({
  id,
  label,
  description,
  checked,
  disabled = false,
  onChange,
}: ToggleSwitchProps) {
  return (
    <div className="flex items-center justify-between gap-4 rounded-xl border bg-white px-4 py-3 dark:bg-[#0B0F14]">
      <div>
        <label htmlFor={id} className="cursor-pointer text-xs font-semibold">
          {label}
        </label>
        {description && <p className="mt-0.5 text-[11px] opacity-60">{description}</p>}
      </div>
      <label
        htmlFor={id}
        className={`relative inline-flex shrink-0 ${disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}
      >
        <input
          id={id}
          type="checkbox"
          role="switch"
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
    </div>
  );
}
