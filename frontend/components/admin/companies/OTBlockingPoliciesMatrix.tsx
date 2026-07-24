"use client";

import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import { ToggleSwitch } from "./ToggleSwitch";
import type { BlockingCriterion, OtBlockingPolicy, TransitOffice } from "@/lib/api/types";
import { BLOCKING_CRITERION_DEFAULTS, ApiValidationError } from "@/lib/api/types";

// Matriz de políticas de bloqueo de preflight por OT (FEATURE 05). Una tarjeta por organismo
// HABILITADO para la compañía, con un switch por criterio. El switch ON = el criterio BLOQUEA
// (rojo, subsanable); OFF = solo ADVIERTE (amarillo, el usuario decide continuar).
// Semántica dispersa: la ausencia de fila se resuelve al DEFAULT del criterio. Al alternar se
// hace PUT con UI optimista y rollback ante error.

/** Criterios configurables, en orden de presentación. */
const CRITERIA: { criterion: BlockingCriterion; label: string }[] = [
  { criterion: "soat", label: "SOAT vencido" },
  { criterion: "rtm", label: "RTM no vigente" },
  { criterion: "estado_vehiculo", label: "Estado del vehículo (RUNT)" },
  { criterion: "fines", label: "Comparendos (SIMIT)" },
  { criterion: "rnmc", label: "RNMC (medidas correctivas)" },
];

export interface OTBlockingPoliciesMatrixProps {
  /** Solo los OT habilitados para la compañía: son los únicos configurables. */
  offices: TransitOffice[];
  /** Filas configuradas explícitamente (tabla dispersa). */
  policies: OtBlockingPolicy[];
  /** Persiste el estado deseado (PUT idempotente). */
  onToggle: (
    transitOfficeId: string,
    criterion: BlockingCriterion,
    blocks: boolean,
  ) => Promise<void>;
  /** Notificación opcional de error de persistencia (toast). */
  onError?: (message: string) => void;
}

const key = (officeId: string, criterion: BlockingCriterion) => `${officeId}:${criterion}`;

export function OTBlockingPoliciesMatrix({
  offices,
  policies,
  onToggle,
  onError,
}: OTBlockingPoliciesMatrixProps) {
  const [search, setSearch] = useState("");
  // Solo se guardan los pares explícitos; `get` ausente ⇒ default del criterio (dispersa).
  const [state, setState] = useState<Map<string, boolean>>(
    () => new Map(policies.map((p) => [key(p.transitOfficeId, p.criterion), p.blocks])),
  );
  const [pending, setPending] = useState<Set<string>>(() => new Set());

  const filtered = useMemo(() => {
    const term = fold(search);
    if (!term) {
      return offices;
    }
    return offices.filter((o) => fold(o.name).includes(term) || fold(o.code).includes(term));
  }, [offices, search]);

  const handleToggle = async (
    office: TransitOffice,
    criterion: BlockingCriterion,
    label: string,
    next: boolean,
  ) => {
    const cellKey = key(office.id, criterion);
    const previous = state.get(cellKey) ?? BLOCKING_CRITERION_DEFAULTS[criterion];

    // UI optimista.
    setState((current) => new Map(current).set(cellKey, next));
    setPending((current) => new Set(current).add(cellKey));

    try {
      await onToggle(office.id, criterion, next);
    } catch (err) {
      // Rollback al estado anterior.
      setState((current) => new Map(current).set(cellKey, previous));
      const serverMessage = err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError?.(
        serverMessage ??
          `No se pudo cambiar el bloqueo de ${label} en ${office.name}.`,
      );
    } finally {
      setPending((current) => {
        const nextPending = new Set(current);
        nextPending.delete(cellKey);
        return nextPending;
      });
    }
  };

  return (
    <section aria-label="Criterios de bloqueo por organismo" className="space-y-3">
      <div>
        <h4 className="text-sm font-semibold">Criterios de bloqueo por Organismo de Tránsito</h4>
        <p className="mt-0.5 max-w-2xl text-[11px] opacity-60">
          Define, por organismo habilitado, si cada hallazgo bloquea el trámite o solo advierte.
          Activado = bloquea (el gestor debe subsanar o aceptar el riesgo); desactivado = solo
          advierte y el usuario decide continuar. Ningún criterio impide crear el trámite. Los
          cambios se guardan al instante; no requieren «Guardar todo».
        </p>
      </div>

      <div className="flex max-w-md items-center gap-2 rounded-xl border bg-white p-2.5 dark:bg-[#0B0F14]">
        <Search className="h-4 w-4 opacity-60" />
        <label htmlFor="ot-blocking-search" className="sr-only">
          Buscar organismo de tránsito
        </label>
        <input
          id="ot-blocking-search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Buscar por nombre o código…"
          className="flex-1 bg-transparent text-xs outline-none"
        />
      </div>

      {filtered.length === 0 ? (
        <p className="rounded-xl border p-4 text-center text-xs opacity-60">
          Ningún organismo coincide con la búsqueda.
        </p>
      ) : (
        <ul className="space-y-3" data-testid="ot-blocking-list">
          {filtered.map((office) => {
            const headingId = `ot-blocking-${office.id}-name`;
            return (
              <li
                key={office.id}
                data-testid={`ot-blocking-${office.id}`}
                className="rounded-xl border p-3"
              >
                <p id={headingId} className="mb-2 text-xs">
                  <span className="font-semibold">{office.name}</span>
                  <span className="ml-2 font-mono opacity-60">{office.code}</span>
                </p>
                {/* Los switches se agrupan bajo el nombre del OT: la etiqueta del switch
                    solo dice el criterio, el grupo aporta de qué organismo es. */}
                <div
                  role="group"
                  aria-labelledby={headingId}
                  className="grid gap-2 sm:grid-cols-2"
                >
                  {CRITERIA.map(({ criterion, label }) => {
                    const cellKey = key(office.id, criterion);
                    return (
                      <ToggleSwitch
                        key={criterion}
                        id={`blocking-${office.id}-${criterion}`}
                        label={label}
                        checked={state.get(cellKey) ?? BLOCKING_CRITERION_DEFAULTS[criterion]}
                        disabled={pending.has(cellKey)}
                        onChange={(checked) => void handleToggle(office, criterion, label, checked)}
                      />
                    );
                  })}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/** Normaliza para comparación: minúsculas + sin tildes españolas. */
function fold(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[áàäâã]/g, "a")
    .replace(/[éèëê]/g, "e")
    .replace(/[íìïî]/g, "i")
    .replace(/[óòöôõ]/g, "o")
    .replace(/[úùüû]/g, "u")
    .replace(/ñ/g, "n");
}
