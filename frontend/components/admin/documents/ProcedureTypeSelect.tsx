"use client";

import { PROCEDURE_TYPES } from "@/lib/constants/procedure-types";

// Selector de tipo de trámite (HU #10198). Lee la lista estática (no hay endpoint
// `GET /admin/procedure-types` en el contrato). Usado en la página de selección y,
// opcionalmente, dentro de la consola por trámite.
export interface ProcedureTypeSelectProps {
  id?: string;
  value: string;
  onChange: (procedureTypeId: string) => void;
  label?: string;
  placeholder?: string;
}

export function ProcedureTypeSelect({
  id = "procedure-type-select",
  value,
  onChange,
  label = "Tipo de trámite",
  placeholder = "Selecciona un tipo de trámite…",
}: ProcedureTypeSelectProps) {
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      <select
        id={id}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
      >
        <option value="">{placeholder}</option>
        {PROCEDURE_TYPES.map((p) => (
          <option key={p.id} value={p.id}>
            {p.name} ({p.code})
          </option>
        ))}
      </select>
    </div>
  );
}
