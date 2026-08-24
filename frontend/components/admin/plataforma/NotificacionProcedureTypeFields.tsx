"use client";

import { useMemo, type ReactNode } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";

const FAMILY_LABEL: Record<string, string> = {
  MATRICULAS: "Matrículas",
  TRASPASO: "Traspaso",
  OTROS: "Otros",
};

export function isTramiteCambioEstadoTemplate(templateId: string): boolean {
  return templateId === "tramites.aprobado" || templateId === "tramites.rechazado";
}

export interface NotificacionProcedureTypeFieldsProps {
  types: ProcedureTypeSummary[];
  catalogStatus: UiStatus;
  onRetryCatalog: () => void;
  family: string;
  typeId: string;
  onFamilyChange: (family: string) => void;
  onTypeIdChange: (typeId: string) => void;
  idPrefix: string;
}

/**
 * Selects familia + tipo (solo activos), mismo patrón visual que el simulador FUR.
 */
export function NotificacionProcedureTypeFields({
  types,
  catalogStatus,
  onRetryCatalog,
  family,
  typeId,
  onFamilyChange,
  onTypeIdChange,
  idPrefix,
}: NotificacionProcedureTypeFieldsProps) {
  const activeTypes = useMemo(() => types.filter((t) => t.isActive), [types]);
  const families = useMemo(() => {
    const set = new Set<string>();
    for (const t of activeTypes) set.add(t.family);
    return [...set];
  }, [activeTypes]);
  const typesInFamily = useMemo(
    () => activeTypes.filter((t) => t.family === family),
    [activeTypes, family],
  );

  return (
    <UiStateBoundary
      status={catalogStatus}
      onRetry={onRetryCatalog}
      errorMessage="No se pudo cargar el catálogo de tipos de trámite."
      emptyMessage="No hay tipos de trámite activos en tramites.procedure_types."
      skeletonRows={2}
    >
      <div
        className="grid gap-4 md:grid-cols-2"
        data-testid="notificaciones-procedure-type-fields"
      >
        <SelectField
          id={`${idPrefix}-family`}
          label="Tipo de trámite padre (familia)"
          value={family}
          onChange={(v) => {
            onFamilyChange(v);
            onTypeIdChange("");
          }}
        >
          <option value="">Selecciona una familia</option>
          {families.map((f) => (
            <option key={f} value={f}>
              {FAMILY_LABEL[f] ?? f}
            </option>
          ))}
        </SelectField>
        <SelectField
          id={`${idPrefix}-type`}
          label="Tipo de trámite"
          value={typeId}
          disabled={!family}
          onChange={onTypeIdChange}
        >
          <option value="">Selecciona un tipo</option>
          {typesInFamily.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name} ({t.code})
            </option>
          ))}
        </SelectField>
      </div>
    </UiStateBoundary>
  );
}

function SelectField({
  id,
  label,
  value,
  onChange,
  disabled,
  children,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  children: ReactNode;
}) {
  return (
    <label className="flex flex-col gap-1.5 text-sm" htmlFor={id}>
      <span className="font-semibold text-[#162244] dark:text-white">{label}</span>
      <select
        id={id}
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        className="rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-sm text-[#162244] disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
      >
        {children}
      </select>
    </label>
  );
}

