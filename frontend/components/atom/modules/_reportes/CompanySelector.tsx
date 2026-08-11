"use client";

import { useMemo } from "react";
import { SearchableSelect } from "@/components/atom/SearchableSelect";
import type { CompanyListItem } from "@/lib/api/types";

interface CompanySelectorProps {
  companies: CompanyListItem[];
  /** tenantId seleccionado; vacío = usar la compañía propia del claim. */
  value: string;
  onChange: (tenantId: string) => void;
  disabled?: boolean;
  /** Etiqueta de la opción vacía. Por defecto "Mi compañía". */
  defaultLabel?: string;
  /** Oculta la etiqueta visual (cuando el contexto ya la da). */
  hideLabel?: boolean;
  className?: string;
  id?: string;
}

/**
 * Selector de compañía visible solo para SuperAdmin (HU #10247, AC1). Sin selección, el backend usa
 * el tenant del propio claim; al elegir una compañía se envía su `tenantId`.
 *
 * Delega en <see cref="SearchableSelect"/> para tener buscador interno: con decenas de empresas,
 * recorrer un desplegable nativo a ojo era el cuello de botella. Se filtra por razón social y por NIT.
 */
export function CompanySelector({
  companies,
  value,
  onChange,
  disabled,
  defaultLabel = "Mi compañía",
  hideLabel = false,
  className,
  id = "reportes-compania",
}: CompanySelectorProps) {
  const options = useMemo(
    () => companies.map((c) => ({ value: c.id, label: c.razonSocial, hint: c.nit })),
    [companies],
  );

  return (
    <SearchableSelect
      id={id}
      label="Compañía"
      hideLabel={hideLabel}
      options={options}
      value={value}
      onChange={onChange}
      disabled={disabled}
      defaultLabel={defaultLabel}
      placeholder="Buscar compañía…"
      className={className}
    />
  );
}
