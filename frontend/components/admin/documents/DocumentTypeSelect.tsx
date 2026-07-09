"use client";

import type { DocumentType } from "@/lib/api/types-documents";

// Selector de documento del catálogo (HU #10198). Solo lista documentos activos
// (`estado === "activo"`): la asociación a un trámite y los overrides exigen
// documentos vigentes. Se pueden excluir ids ya usados vía `excludeIds`.
export interface DocumentTypeSelectProps {
  id?: string;
  value: string;
  onChange: (documentTypeId: string) => void;
  options: DocumentType[];
  excludeIds?: string[];
  label?: string;
  placeholder?: string;
  disabled?: boolean;
}

export function DocumentTypeSelect({
  id = "document-type-select",
  value,
  onChange,
  options,
  excludeIds = [],
  label = "Documento",
  placeholder = "Selecciona un documento…",
  disabled,
}: DocumentTypeSelectProps) {
  const excluded = new Set(excludeIds);
  const available = options.filter((d) => d.estado === "activo" && !excluded.has(d.id));

  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      <select
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled || available.length === 0}
        className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 disabled:opacity-50"
      >
        <option value="">
          {available.length === 0 ? "No hay documentos activos disponibles" : placeholder}
        </option>
        {available.map((d) => (
          <option key={d.id} value={d.id}>
            {d.nombre} ({d.codigo})
          </option>
        ))}
      </select>
    </div>
  );
}
