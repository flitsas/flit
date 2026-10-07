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
      className={`flex min-w-[200px] items-center gap-2 rounded-xl border border-[#DDE5F0] bg-white px-3 py-1.5 focus-within:border-[#557EFF] focus-within:ring-2 focus-within:ring-[#557EFF]/30 dark:border-white/10 dark:bg-[#0B0F14] ${className}`}
    >
      <Search className="h-4 w-4 shrink-0 opacity-60" aria-hidden />
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label={label}
        className="min-w-0 flex-1 bg-transparent py-1 text-xs outline-none"
      />
    </div>
  );
}
