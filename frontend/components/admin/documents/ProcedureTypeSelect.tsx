"use client";

import { useProcedureTypes } from "@/hooks/useProcedureTypes";

// Selector de tipo de trámite (HU #10198). Resuelve la lista desde el API
// (`GET /api/v1/superadmin/procedure-types`): los ids de procedure_types se generan por
// ambiente (uuidv7), así que NO pueden hardcodearse o la consola apunta a un id inexistente.
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
  const { items } = useProcedureTypes();
  const options = items.filter((p) => p.isActive);
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
        {options.map((p) => (
          <option key={p.id} value={p.id}>
            {p.name} ({p.code})
          </option>
        ))}
      </select>
    </div>
  );
}
