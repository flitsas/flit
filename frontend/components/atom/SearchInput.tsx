"use client";

import { Search } from "lucide-react";

/**
 * Caja de búsqueda de las tablas (modelo de trámites): lupa a la izquierda, texto ≥ 12px y anillo de foco
 * visible. El nombre accesible es obligatorio porque el campo no lleva etiqueta visible.
 */
export function SearchInput({
  value,
  onChange,
  label,
  placeholder,
  className = "",
}: {
  value: string;
  onChange: (value: string) => void;
  /** Nombre accesible («Buscar usuarios»). */
  label: string;
  placeholder?: string;
  className?: string;
}) {
  return (
    <div
      className={`flex h-9 min-w-[200px] items-center gap-2 rounded-xl border border-[color:var(--di-border-control)] bg-[var(--di-surface-elevated)] px-3 text-[color:var(--di-text-primary)] transition focus-within:border-[color:var(--di-action)] focus-within:ring-2 focus-within:ring-flit-brand focus-within:ring-offset-2 ${className}`}
    >
      <Search className="h-4 w-4 shrink-0 text-[color:var(--di-text-tertiary)]" aria-hidden />
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label={label}
        className="min-w-0 flex-1 bg-transparent py-1 text-xs outline-none placeholder:text-[color:var(--di-text-tertiary)]"
      />
    </div>
  );
}
