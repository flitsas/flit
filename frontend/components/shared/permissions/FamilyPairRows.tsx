"use client";

import { RotateCcw, ShieldCheck, UserCog } from "lucide-react";
import type { ActionView } from "@/lib/permissions/modulePermissions";
import { formatFechaHora } from "@/lib/format/date";
import { CREATE_NEEDS_READ_HINT } from "./FamilyPairCheckboxes";

// HU #13666 (Feature #13636 F-F) — par Ver / Crear de un tipo de servicio en «Permisos por módulo» de un usuario.
// Cada fila lleva su estado («Heredado del rol» / «Individual»). «Crear» queda deshabilitado mientras el «Ver» EFECTIVO
// (heredado o individual) esté inactivo. Apagar «Ver» (denegar o volver al rol) apaga también «Crear»: lo hace la API
// (regla «crear exige ver» de overrides individuales) y la sección refresca la lista tras el cambio.

const ORIGIN_TEXT = { inherited: "Heredado del rol", individual: "Individual" } as const;
const ORIGIN_ICON = { inherited: ShieldCheck, individual: UserCog } as const;

function PairRow({
  label,
  moduleName,
  action,
  disabled,
  hint,
  readOnly,
  controlsOff,
  onToggle,
  onBackToRole,
}: {
  label: "Ver" | "Crear";
  moduleName: string;
  action: ActionView;
  disabled: boolean;
  hint?: string;
  readOnly: boolean;
  controlsOff: boolean;
  onToggle: (a: ActionView) => void;
  onBackToRole: (a: ActionView) => void;
}) {
  const Icon = ORIGIN_ICON[action.origin];
  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2" data-testid={`family-${label === "Ver" ? "read" : "create"}-${action.slug}`}>
      <button
        type="button"
        role="switch"
        aria-checked={action.enabled}
        aria-label={`${label}, ${moduleName}`}
        disabled={controlsOff || disabled}
        title={hint}
        onClick={() => onToggle(action)}
        className="relative h-5 w-9 shrink-0 rounded-full border transition disabled:opacity-50"
        style={{
          background: action.enabled ? "var(--permiso-habilitado)" : "transparent",
          borderColor: action.enabled ? "var(--permiso-habilitado)" : "var(--permiso-deshabilitado)",
        }}
      >
        <span
          aria-hidden="true"
          className="absolute top-0.5 h-3.5 w-3.5 rounded-full bg-white transition-all"
          style={{
            left: action.enabled ? "1.15rem" : "0.15rem",
            background: action.enabled ? "#FFFFFF" : "var(--permiso-deshabilitado)",
          }}
        />
      </button>
      <span className="min-w-0 flex-1 text-xs">
        <span className="font-medium">{label}</span>
        {action.origin === "individual" && action.grantedByName && (
          <span className="block text-[10px] opacity-60">
            Otorgado por {action.grantedByName}
            {action.grantedAt ? ` el ${formatFechaHora(action.grantedAt)}` : ""}
          </span>
        )}
        {hint && <span className="sr-only">{hint}</span>}
      </span>
      <span
        className="text-[10px] font-semibold"
        style={{ color: action.enabled ? "var(--permiso-habilitado)" : "var(--permiso-deshabilitado)" }}
      >
        {action.enabled ? "Habilitado" : "Deshabilitado"}
      </span>
      <span
        className="inline-flex items-center gap-1 rounded-full border border-[#DFE5ED] px-2 py-0.5 text-[10px] font-semibold text-[#162744] dark:border-white/20 dark:text-white"
        data-origin={action.origin}
      >
        <Icon className="h-3 w-3" aria-hidden="true" />
        {ORIGIN_TEXT[action.origin]}
      </span>
      {action.origin === "individual" && !readOnly && (
        <button
          type="button"
          disabled={controlsOff}
          onClick={() => onBackToRole(action)}
          aria-label={`Volver ${label} (${moduleName}) a lo que dicta el rol`}
          className="inline-flex items-center gap-1 rounded-lg border border-[#DFE5ED] px-2 py-1 text-[10px] font-semibold disabled:opacity-50"
        >
          <RotateCcw className="h-3 w-3" aria-hidden="true" />
          Volver al rol
        </button>
      )}
    </li>
  );
}

export function FamilyPairRows({
  moduleName,
  read,
  create,
  readOnly,
  controlsOff,
  onToggle,
  onBackToRole,
}: {
  moduleName: string;
  read: ActionView;
  create: ActionView;
  readOnly: boolean;
  controlsOff: boolean;
  onToggle: (a: ActionView) => void;
  onBackToRole: (a: ActionView) => void;
}) {
  const createBlocked = !read.enabled;
  return (
    <>
      <PairRow
        label="Ver"
        moduleName={moduleName}
        action={read}
        disabled={false}
        readOnly={readOnly}
        controlsOff={controlsOff}
        onToggle={onToggle}
        onBackToRole={onBackToRole}
      />
      <PairRow
        label="Crear"
        moduleName={moduleName}
        action={create}
        disabled={createBlocked}
        hint={createBlocked ? CREATE_NEEDS_READ_HINT : undefined}
        readOnly={readOnly}
        controlsOff={controlsOff}
        onToggle={onToggle}
        onBackToRole={onBackToRole}
      />
    </>
  );
}
