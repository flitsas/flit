"use client";

import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import { ToggleSwitch } from "./ToggleSwitch";
import type { ConsultationRestrictionKind, OtConsultationRestriction, TransitOffice } from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";

// Matriz de restricciones de consulta por OT (HU #10761 / backend #10759). Una tarjeta
// por organismo HABILITADO para la compañía, con un switch por tipo de consulta.
// Semántica dispersa: solo llegan filas de los pares que el admin tocó, así que la
// ausencia de fila se resuelve como PERMITIDA. Al alternar se hace PUT con UI optimista
// y rollback ante error (AC4).

/** Consultas restringibles, en orden de presentación. `vehicle` queda fuera (rompe el FUR). */
const KINDS: { kind: ConsultationRestrictionKind; label: string }[] = [
  { kind: "rnmc", label: "RNMC (medidas correctivas)" },
  { kind: "fines", label: "Comparendos (SIMIT)" },
];

export interface OTConsultationRestrictionsMatrixProps {
  /** Solo los OT habilitados para la compañía: son los únicos restringibles (AC2). */
  offices: TransitOffice[];
  /** Filas configuradas explícitamente (tabla dispersa). */
  restrictions: OtConsultationRestriction[];
  /** Persiste el estado deseado (PUT idempotente). */
  onToggle: (
    transitOfficeId: string,
    kind: ConsultationRestrictionKind,
    enabled: boolean,
  ) => Promise<void>;
  /** Notificación opcional de error de persistencia (toast). */
  onError?: (message: string) => void;
}

const key = (officeId: string, kind: ConsultationRestrictionKind) => `${officeId}:${kind}`;

export function OTConsultationRestrictionsMatrix({
  offices,
  restrictions,
  onToggle,
  onError,
}: OTConsultationRestrictionsMatrixProps) {
  const [search, setSearch] = useState("");
  // Solo se guardan los pares explícitos; `get` ausente ⇒ permitida (dispersa).
  const [state, setState] = useState<Map<string, boolean>>(
    () => new Map(restrictions.map((r) => [key(r.transitOfficeId, r.consultationKind), r.enabled])),
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
    kind: ConsultationRestrictionKind,
    label: string,
    next: boolean,
  ) => {
    const cellKey = key(office.id, kind);
    const previous = state.get(cellKey) ?? true;

    // UI optimista.
    setState((current) => new Map(current).set(cellKey, next));
    setPending((current) => new Set(current).add(cellKey));

    try {
      await onToggle(office.id, kind, next);
    } catch (err) {
      // Rollback al estado anterior (AC4).
      setState((current) => new Map(current).set(cellKey, previous));
      // Si el backend rechaza (422: OT sin grant), mostrar su mensaje; si no, fallback.
      const serverMessage = err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError?.(
        serverMessage ??
          `No se pudo ${next ? "habilitar" : "inhabilitar"} ${label} en ${office.name}.`,
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
    <section aria-label="Restricciones de consulta por organismo" className="space-y-3">
      <div>
        <h4 className="text-sm font-semibold">Consultas por Organismo de Tránsito</h4>
        <p className="mt-0.5 max-w-2xl text-[11px] opacity-60">
          Define qué se consulta en cada organismo habilitado. Al inhabilitar una consulta, el
          trámite hacia ese organismo la omitirá y lo indicará en el pre-vuelo; no impide crear el
          trámite. Los cambios se guardan al instante; no requieren «Guardar todo».
        </p>
      </div>

      <div className="flex max-w-md items-center gap-2 rounded-xl border bg-white p-2.5 dark:bg-[#0B0F14]">
        <Search className="h-4 w-4 opacity-60" />
        <label htmlFor="ot-restrictions-search" className="sr-only">
          Buscar organismo de tránsito
        </label>
        <input
          id="ot-restrictions-search"
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
        <ul className="space-y-3" data-testid="ot-restrictions-list">
          {filtered.map((office) => {
            const headingId = `ot-restrictions-${office.id}-name`;
            return (
              <li
                key={office.id}
                data-testid={`ot-restrictions-${office.id}`}
                className="rounded-xl border p-3"
              >
                <p id={headingId} className="mb-2 text-xs">
                  <span className="font-semibold">{office.name}</span>
                  <span className="ml-2 font-mono opacity-60">{office.code}</span>
                </p>
                {/* Los switches se agrupan bajo el nombre del OT: la etiqueta del switch
                    solo dice el tipo de consulta, el grupo aporta de qué organismo es. */}
                <div
                  role="group"
                  aria-labelledby={headingId}
                  className="grid gap-2 sm:grid-cols-2"
                >
                  {KINDS.map(({ kind, label }) => {
                    const cellKey = key(office.id, kind);
                    return (
                      <ToggleSwitch
                        key={kind}
                        id={`restriction-${office.id}-${kind}`}
                        label={label}
                        checked={state.get(cellKey) ?? true}
                        disabled={pending.has(cellKey)}
                        onChange={(checked) => void handleToggle(office, kind, label, checked)}
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
