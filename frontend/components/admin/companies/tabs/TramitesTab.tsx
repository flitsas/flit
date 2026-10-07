"use client";

import { useState, type ReactNode } from "react";
import { CheckCircle2, MinusCircle, ShieldAlert } from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { ToggleSwitch, type ToggleSwitchState } from "../ToggleSwitch";
import { Accordion } from "../ConfigUi";
import type { SettingsForm } from "../settingsForm";

/**
 * Pestaña Trámites — configuración por familia (`MATRICULAS` | `TRASPASO` | `OTROS`).
 * Cada familia es un acordeón con el resumen de sus restricciones en el encabezado; el primer toggle
 * bloquea la creación (activo = no crear). «Improntas» usa el mismo acordeón.
 */
export interface TramitesTabProps {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
  fieldErrors?: Record<string, string>;
  whitelistSlot?: ReactNode;
  /** HU #13401 — interruptor «Generación de improntas»; solo Super Admin. */
  showImprontas?: boolean;
}

type FamilyId = "matriculas" | "traspaso" | "otros";

// El valor guardado NO cambia: solo el texto de estado se lee en positivo, sin dobles negaciones.
/** «No permitir trámites…» = activo significa Bloqueado. */
const ESTADO_BLOQUEO: ToggleSwitchState = { on: "Bloqueado", off: "Permitido", onTone: "warning", offTone: "success" };
/** «Solo vehículos propios» = activo significa Restringido. */
const ESTADO_RESTRICCION: ToggleSwitchState = { on: "Restringido", off: "Sin restricción", onTone: "warning", offTone: "success" };
/** «Permitir…» = activo significa Permitido. */
const ESTADO_PERMISO: ToggleSwitchState = { on: "Permitido", off: "No permitido", onTone: "success", offTone: "neutral" };

export function TramitesTab({
  form,
  onChange,
  fieldErrors,
  whitelistSlot,
  showImprontas = false,
}: TramitesTabProps) {
  // Matrículas abierta por defecto (primera familia); el resto colapsado.
  const [open, setOpen] = useState<Record<FamilyId, boolean>>({
    matriculas: true,
    traspaso: false,
    otros: false,
  });

  const toggleOpen = (id: FamilyId) =>
    setOpen((prev) => ({ ...prev, [id]: !prev[id] }));

  // Resumen del encabezado: cuántas restricciones (bloqueo de la familia + «solo propios») siguen activas.
  const restrictions = {
    matriculas: countOn(form.blockProcedureFamilyMatriculas, form.onlyOwnVehiclesMatriculas),
    traspaso: countOn(form.blockProcedureFamilyTraspaso, form.onlyOwnVehiclesTraspaso),
    otros: countOn(form.blockProcedureFamilyOtros, form.onlyOwnVehiclesOtros),
  };

  return (
    <div className="space-y-6">
      <FamilySection
        id="matriculas"
        title="Matrículas"
        subtitle="Familia MATRICULAS — primera matrícula, cancelación y demás tipos de esta familia."
        restrictions={restrictions.matriculas}
        open={open.matriculas}
        onToggle={() => toggleOpen("matriculas")}
      >
        <ToggleSwitch
          id="blockProcedureFamilyMatriculas"
          label="No permitir trámites de matrículas"
          description="Si está activo, la compañía no podrá crear trámites de la familia Matrículas (incluida la matrícula inicial). Desactívalo para autorizar la radicación de estos trámites en la plataforma."
          state={ESTADO_BLOQUEO}
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
          state={ESTADO_PERMISO}
          checked={form.allowMiscNewVehicles}
          onChange={(v) => onChange({ allowMiscNewVehicles: v })}
        />
        <ToggleSwitch
          id="onlyOwnVehiclesMatriculas"
          label="Solo vehículos propios"
          description="Restringe los trámites de la familia Matrículas a vehículos que ya figuran como propiedad de esta compañía. Los correos en lista blanca (sección Traspaso) quedan exentos."
          state={ESTADO_RESTRICCION}
          checked={form.onlyOwnVehiclesMatriculas}
          onChange={(v) => onChange({ onlyOwnVehiclesMatriculas: v })}
        />
        {fieldErrors?.switchesMatricula && (
          <div className="lg:col-span-2">
            <FieldError message={fieldErrors.switchesMatricula} />
          </div>
        )}
      </FamilySection>

      <FamilySection
        id="traspaso"
        title="Traspaso"
        subtitle="Familia TRASPASO — traspaso de propiedad."
        restrictions={restrictions.traspaso}
        open={open.traspaso}
        onToggle={() => toggleOpen("traspaso")}
      >
        <ToggleSwitch
          id="blockProcedureFamilyTraspaso"
          label="No permitir trámites de traspaso"
          description="Si está activo, la compañía no podrá crear trámites de traspaso en la plataforma. Desactívalo para autorizar la radicación de traspasos."
          state={ESTADO_BLOQUEO}
          checked={form.blockProcedureFamilyTraspaso}
          onChange={(v) => onChange({ blockProcedureFamilyTraspaso: v })}
        />
        <ToggleSwitch
          id="onlyOwnVehiclesTraspaso"
          label="Solo vehículos propios"
          description="Restringe los traspasos a vehículos que ya figuran como propiedad de esta compañía. Los correos registrados en la lista blanca quedan exentos y pueden tramitar traspasos de otros vehículos."
          state={ESTADO_RESTRICCION}
          checked={form.onlyOwnVehiclesTraspaso}
          onChange={(v) => onChange({ onlyOwnVehiclesTraspaso: v, onlyOwnVehicles: v })}
        />
        {whitelistSlot && (
          <div className="rounded-[14px] border border-[#DFE5ED] p-4 lg:col-span-2 dark:border-white/10">{whitelistSlot}</div>
        )}
      </FamilySection>

      <FamilySection
        id="otros"
        title="Otros trámites"
        subtitle="Familia OTROS — blindaje, duplicados, prendas, cambios de color/carrocería y demás tipos."
        restrictions={restrictions.otros}
        open={open.otros}
        onToggle={() => toggleOpen("otros")}
      >
        <ToggleSwitch
          id="blockProcedureFamilyOtros"
          label="No permitir otros trámites"
          description="Si está activo, la compañía no podrá crear trámites de la familia Otros en la plataforma. Desactívalo para autorizar la radicación de esos tipos."
          state={ESTADO_BLOQUEO}
          checked={form.blockProcedureFamilyOtros}
          onChange={(v) => onChange({ blockProcedureFamilyOtros: v })}
        />
        <ToggleSwitch
          id="onlyOwnVehiclesOtros"
          label="Solo vehículos propios"
          description="Restringe los trámites de la familia Otros a vehículos que ya figuran como propiedad de esta compañía. Los correos en lista blanca (sección Traspaso) quedan exentos."
          state={ESTADO_RESTRICCION}
          checked={form.onlyOwnVehiclesOtros}
          onChange={(v) => onChange({ onlyOwnVehiclesOtros: v })}
        />
      </FamilySection>

      {showImprontas && <ImprontasSection form={form} onChange={onChange} />}
    </div>
  );
}

/**
 * HU #13401 — interruptor de la generación automática de improntas. Mismo acordeón que las familias
 * (abierto por defecto); el estado se distingue por color, icono y texto («Habilitada» / «Deshabilitada»)
 * en el chip del encabezado, por eso el switch no repite el chip. Se guarda con «Guardar todo».
 */
function ImprontasSection({
  form,
  onChange,
}: {
  form: SettingsForm;
  onChange: (patch: Partial<SettingsForm>) => void;
}) {
  const enabled = form.generacionImprontas;
  const [open, setOpen] = useState(true);
  return (
    <Accordion
      title="Improntas"
      subtitle="Generación automática de la impronta en el wizard."
      open={open}
      onToggle={() => setOpen((v) => !v)}
      bodyClassName="grid gap-4"
      badge={
        <span data-testid="improntas-estado">
          <StatusBadge
            tone={enabled ? "success" : "neutral"}
            ariaLabel={`Improntas: ${enabled ? "Habilitada" : "Deshabilitada"}`}
            label={
              <span className="inline-flex items-center gap-1">
                {enabled ? (
                  <CheckCircle2 className="h-3.5 w-3.5" aria-hidden />
                ) : (
                  <MinusCircle className="h-3.5 w-3.5" aria-hidden />
                )}
                {enabled ? "Habilitada" : "Deshabilitada"}
              </span>
            }
          />
        </span>
      }
    >
      <ToggleSwitch
        id="generacionImprontas"
        label="Generación de improntas"
        description="Si se desactiva, el wizard no genera la impronta automáticamente y el radicador la carga a mano."
        hint="Solo aplica a trámites nuevos."
        checked={enabled}
        onChange={(v) => onChange({ generacionImprontas: v })}
      />
    </Accordion>
  );
}

function countOn(...flags: boolean[]): number {
  return flags.filter(Boolean).length;
}

function FamilySection({
  id,
  title,
  subtitle,
  restrictions,
  open,
  onToggle,
  children,
}: {
  id: FamilyId;
  title: string;
  subtitle: string;
  /** Restricciones activas de la familia (resumen del encabezado). */
  restrictions: number;
  open: boolean;
  onToggle: () => void;
  children: ReactNode;
}) {
  const summary =
    restrictions === 0 ? "Sin restricciones" : `${restrictions} ${restrictions === 1 ? "restricción activa" : "restricciones activas"}`;
  return (
    <Accordion
      title={title}
      subtitle={subtitle}
      open={open}
      onToggle={onToggle}
      dataFamily={id}
      badge={
        <StatusBadge
          tone={restrictions === 0 ? "success" : "warning"}
          ariaLabel={`${title}: ${summary}`}
          label={
            <span className="inline-flex items-center gap-1">
              {restrictions === 0 ? (
                <CheckCircle2 className="h-3.5 w-3.5" aria-hidden />
              ) : (
                <ShieldAlert className="h-3.5 w-3.5" aria-hidden />
              )}
              {summary}
            </span>
          }
        />
      }
    >
      {children}
    </Accordion>
  );
}

function FieldError({ message }: { message?: string }) {
  if (!message) {
    return null;
  }
  return (
    <p className="text-xs font-medium" style={{ color: "#FF4E00" }} role="alert">
      {message}
    </p>
  );
}
