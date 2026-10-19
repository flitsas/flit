"use client";

import type { AccessibleAction } from "@/lib/api/security";

// HU #13666 (Feature #13636 F-F) — par Ver / Crear de un tipo de servicio en la configuración de permisos por ROL
// (RbacAdmin). «Crear» queda deshabilitado mientras «Ver» esté inactivo (regla «crear exige ver»).

export const CREATE_NEEDS_READ_HINT = "Habilita «Ver» para poder habilitar «Crear».";

export function FamilyPairCheckboxes({
  moduleName,
  read,
  create,
  selected,
  onToggle,
}: {
  moduleName: string;
  read: AccessibleAction;
  create: AccessibleAction;
  selected: ReadonlySet<string>;
  onToggle: (id: string) => void;
}) {
  const readOn = selected.has(read.id);
  const createBlocked = !readOn;
  return (
    <div role="group" aria-label={`Ver y crear — ${moduleName}`} className="col-span-full flex flex-wrap items-center gap-x-6 gap-y-1">
      <label className="flex cursor-pointer items-center gap-2">
        <input
          type="checkbox"
          checked={readOn}
          onChange={() => onToggle(read.id)}
          aria-label={`Ver — ${moduleName}`}
          className="h-3.5 w-3.5 accent-[#557EFF]"
        />
        <span className="text-xs">Ver</span>
      </label>
      <label
        className={`flex items-center gap-2 ${createBlocked ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}
        title={createBlocked ? CREATE_NEEDS_READ_HINT : undefined}
      >
        <input
          type="checkbox"
          checked={selected.has(create.id)}
          disabled={createBlocked}
          onChange={() => onToggle(create.id)}
          aria-label={`Crear — ${moduleName}`}
          className="h-3.5 w-3.5 accent-[#557EFF]"
        />
        <span className="text-xs">Crear</span>
      </label>
      {createBlocked && <span className="sr-only">{CREATE_NEEDS_READ_HINT}</span>}
    </div>
  );
}
