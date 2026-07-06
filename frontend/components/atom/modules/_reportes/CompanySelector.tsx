"use client";

import type { CompanyListItem } from "@/lib/api/types";

interface CompanySelectorProps {
  companies: CompanyListItem[];
  /** tenantId seleccionado; vacío = usar la compañía propia del claim. */
  value: string;
  onChange: (tenantId: string) => void;
  disabled?: boolean;
  /** Etiqueta de la opción vacía. Por defecto "Mi compañía". */
  defaultLabel?: string;
}

/**
 * Selector de compañía visible solo para SuperAdmin (HU #10247, AC1). Sin selección,
 * el backend usa el tenant del propio claim; al elegir una compañía se envía su
 * `tenantId` al overview.
 */
export function CompanySelector({ companies, value, onChange, disabled, defaultLabel = "Mi compañía" }: CompanySelectorProps) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor="reportes-compania" className="text-[10px] font-semibold uppercase opacity-60">
        Compañía
      </label>
      <select
        id="reportes-compania"
        className="h-10 rounded-[10px] border bg-white px-3 text-xs font-medium text-[#162744] outline-none focus:border-[#557EFF] disabled:opacity-60 dark:bg-[#0B0F14] dark:text-white"
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
      >
        <option value="">{defaultLabel}</option>
        {companies.map((c) => (
          <option key={c.id} value={c.id}>
            {c.razonSocial}
          </option>
        ))}
      </select>
    </div>
  );
}
