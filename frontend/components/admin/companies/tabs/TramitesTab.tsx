"use client";

import { useId, useState, type ReactNode } from "react";
import { ChevronDown } from "lucide-react";
import { ToggleSwitch } from "../ToggleSwitch";
import type { SettingsForm } from "../settingsForm";

/**
 * Pestaña Trámites — configuración por familia (`MATRICULAS` | `TRASPASO` | `OTROS`).
 * Cada familia es un desplegable; el primer toggle bloquea la creación (activo = no crear).
 */
export interface TramitesTabProps {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
  fieldErrors?: Record<string, string>;
  whitelistSlot?: ReactNode;
}

type FamilyId = "matriculas" | "traspaso" | "otros";

export function TramitesTab({ form, onChange, fieldErrors, whitelistSlot }: TramitesTabProps) {
  // Matrículas abierta por defecto (primera familia); el resto colapsado.
  const [open, setOpen] = useState<Record<FamilyId, boolean>>({
    matriculas: true,
    traspaso: false,
    otros: false,
  });

  const toggleOpen = (id: FamilyId) =>
    setOpen((prev) => ({ ...prev, [id]: !prev[id] }));

  return (
    <div className="space-y-3">
      <FamilySection
        id="matriculas"
        title="Matrículas"
        subtitle="Familia MATRICULAS — primera matrícula, cancelación y demás tipos de esta familia."
        open={open.matriculas}
        onToggle={() => toggleOpen("matriculas")}
      >
        <ToggleSwitch
          id="blockProcedureFamilyMatriculas"
          label="No permitir trámites de matrículas"
          description="Si está activo, la compañía no podrá crear trámites de la familia Matrículas (incluida la matrícula inicial). Desactívalo para autorizar la radicación de estos trámites en la plataforma."
          checked={form.blockProcedureFamilyMatriculas}
          onChange={(v) =>
            onChange({
              blockProcedureFamilyMatriculas: v,
              allowInitialRegistration: !v,
            })
          }
        />
        <ToggleSwitch
          id="allowMiscNewVehicles"
          label="Permitir vehículos de categorías misceláneas"
          description="Extiende la matrícula inicial a vehículos nuevos de categorías especiales o «misceláneas» —como remolques, semirremolques, maquinaria agrícola o industrial, motocarros y similares—, además de los automóviles y camiones convencionales. Si se desactiva, solo se podrán matricular las categorías estándar."
          checked={form.allowMiscNewVehicles}
          onChange={(v) => onChange({ allowMiscNewVehicles: v })}
        />
        <ToggleSwitch
          id="onlyOwnVehiclesMatriculas"
          label="Solo vehículos propios"
          description="Restringe los trámites de la familia Matrículas a vehículos que ya figuran como propiedad de esta compañía. Los correos en lista blanca (sección Traspaso) quedan exentos."
          checked={form.onlyOwnVehiclesMatriculas}
          onChange={(v) => onChange({ onlyOwnVehiclesMatriculas: v })}
        />
        <FieldError message={fieldErrors?.switchesMatricula} />
      </FamilySection>

      <FamilySection
        id="traspaso"
        title="Traspaso"
        subtitle="Familia TRASPASO — traspaso de propiedad."
        open={open.traspaso}
        onToggle={() => toggleOpen("traspaso")}
      >
        <ToggleSwitch
          id="blockProcedureFamilyTraspaso"
          label="No permitir trámites de traspaso"
          description="Si está activo, la compañía no podrá crear trámites de traspaso en la plataforma. Desactívalo para autorizar la radicación de traspasos."
          checked={form.blockProcedureFamilyTraspaso}
          onChange={(v) => onChange({ blockProcedureFamilyTraspaso: v })}
        />
        <ToggleSwitch
          id="onlyOwnVehiclesTraspaso"
          label="Solo vehículos propios"
          description="Restringe los traspasos a vehículos que ya figuran como propiedad de esta compañía. Los correos registrados en la lista blanca quedan exentos y pueden tramitar traspasos de otros vehículos."
          checked={form.onlyOwnVehiclesTraspaso}
          onChange={(v) => onChange({ onlyOwnVehiclesTraspaso: v, onlyOwnVehicles: v })}
        />
        {whitelistSlot && <div className="rounded-2xl border p-4">{whitelistSlot}</div>}
      </FamilySection>

      <FamilySection
        id="otros"
        title="Otros trámites"
        subtitle="Familia OTROS — blindaje, duplicados, prendas, cambios de color/carrocería y demás tipos."
        open={open.otros}
        onToggle={() => toggleOpen("otros")}
      >
        <ToggleSwitch
          id="blockProcedureFamilyOtros"
          label="No permitir otros trámites"
          description="Si está activo, la compañía no podrá crear trámites de la familia Otros en la plataforma. Desactívalo para autorizar la radicación de esos tipos."
          checked={form.blockProcedureFamilyOtros}
          onChange={(v) => onChange({ blockProcedureFamilyOtros: v })}
        />
        <ToggleSwitch
          id="onlyOwnVehiclesOtros"
          label="Solo vehículos propios"
          description="Restringe los trámites de la familia Otros a vehículos que ya figuran como propiedad de esta compañía. Los correos en lista blanca (sección Traspaso) quedan exentos."
          checked={form.onlyOwnVehiclesOtros}
          onChange={(v) => onChange({ onlyOwnVehiclesOtros: v })}
        />
      </FamilySection>
    </div>
  );
}

function FamilySection({
  id,
  title,
  subtitle,
  open,
  onToggle,
  children,
}: {
  id: FamilyId;
  title: string;
  subtitle: string;
  open: boolean;
  onToggle: () => void;
  children: ReactNode;
}) {
  const panelId = useId();
  const headerId = useId();

  return (
    <section
      className="overflow-hidden rounded-2xl border border-[#DFE5ED] dark:border-white/10"
      aria-labelledby={headerId}
    >
      <button
        type="button"
        id={headerId}
        aria-expanded={open}
        aria-controls={panelId}
        onClick={onToggle}
        className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left transition hover:bg-[rgba(85,126,255,0.04)]"
      >
        <div className="min-w-0">
          <p className="text-xs font-semibold text-[#162744] dark:text-white">{title}</p>
          <p className="mt-0.5 text-[11px] opacity-60">{subtitle}</p>
        </div>
        <ChevronDown
          className="h-4 w-4 shrink-0 opacity-70 transition-transform"
          style={{ transform: open ? "rotate(180deg)" : "rotate(0deg)" }}
          aria-hidden
        />
      </button>
      {open && (
        <div
          id={panelId}
          role="region"
          aria-labelledby={headerId}
          data-family={id}
          className="space-y-3 border-t border-[#DFE5ED] px-4 py-3 dark:border-white/10"
        >
          {children}
        </div>
      )}
    </section>
  );
}

function FieldError({ message }: { message?: string }) {
  if (!message) {
    return null;
  }
  return (
    <p className="text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
      {message}
    </p>
  );
}
