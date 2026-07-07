"use client";

import { useMemo, useState } from "react";
import { Check, Search } from "lucide-react";
import type { TransitOffice } from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";

/** Estado operativo de un OT en FLIT (HU #10518) para bloquear habilitación. */
export interface OtOperationalInfo {
  hasTenant: boolean;
  estadoActivo: boolean | null;
}

// Matriz de organismos de tránsito (HU #10194, AC4 + HU #10518). Catálogo completo en
// memoria con buscador client-side en tiempo real (insensible a mayúsculas y tildes).
// Cada checkbox refleja un grant del tenant; al alternar dispara POST/DELETE con UI
// optimista y rollback ante error. HU #10518: no se puede HABILITAR un OT sin alta o
// inactivo en FLIT (checkbox deshabilitado + badge); quitar el grant siempre se permite.
export interface OTMatrixProps {
  offices: TransitOffice[];
  grantedIds: string[];
  /** Estado operativo por OT (id → info). Ausente = se asume operable (no bloquea). */
  operationalById?: Record<string, OtOperationalInfo>;
  /** Persiste el cambio de grant (POST si enabled, DELETE si !enabled). */
  onToggle: (officeId: string, enabled: boolean) => Promise<void>;
  /** Notificación opcional de error de persistencia (toast). */
  onError?: (message: string) => void;
}

/** Deriva si un OT es operable y, si no, la razón para el badge. */
function operability(info: OtOperationalInfo | undefined): { operable: boolean; reason: string | null } {
  if (!info) {
    return { operable: true, reason: null };
  }
  if (!info.hasTenant) {
    return { operable: false, reason: "Sin alta" };
  }
  if (info.estadoActivo !== true) {
    return { operable: false, reason: "Inactivo" };
  }
  return { operable: true, reason: null };
}

export function OTMatrix({ offices, grantedIds, operationalById, onToggle, onError }: OTMatrixProps) {
  const [search, setSearch] = useState("");
  const [granted, setGranted] = useState<Set<string>>(() => new Set(grantedIds));
  const [pending, setPending] = useState<Set<string>>(() => new Set());
  // IDs con confirmación "Guardado" visible unos segundos tras persistir con éxito.
  const [justSaved, setJustSaved] = useState<Set<string>>(() => new Set());

  const filtered = useMemo(() => {
    const term = fold(search);
    if (!term) {
      return offices;
    }
    return offices.filter((o) => fold(o.name).includes(term) || fold(o.code).includes(term));
  }, [offices, search]);

  const handleToggle = async (office: TransitOffice) => {
    const enable = !granted.has(office.id);

    // UI optimista.
    setGranted((current) => {
      const next = new Set(current);
      if (enable) {
        next.add(office.id);
      } else {
        next.delete(office.id);
      }
      return next;
    });
    setPending((current) => new Set(current).add(office.id));

    try {
      await onToggle(office.id, enable);
      // Confirmación sutil: se guardó al instante (endpoint propio, sin "Guardar todo").
      setJustSaved((current) => new Set(current).add(office.id));
      setTimeout(() => {
        setJustSaved((current) => {
          const next = new Set(current);
          next.delete(office.id);
          return next;
        });
      }, 2500);
    } catch (err) {
      // Rollback.
      setGranted((current) => {
        const next = new Set(current);
        if (enable) {
          next.delete(office.id);
        } else {
          next.add(office.id);
        }
        return next;
      });
      // HU #10518: si el backend rechaza el grant (422), mostrar su mensaje (OT sin
      // alta / inactivo); si no, el fallback genérico.
      const serverMessage =
        err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError?.(
        serverMessage ?? `No se pudo ${enable ? "habilitar" : "deshabilitar"} ${office.name}.`,
      );
    } finally {
      setPending((current) => {
        const next = new Set(current);
        next.delete(office.id);
        return next;
      });
    }
  };

  return (
    <section aria-label="Organismos de tránsito" className="space-y-3">
      <div>
        <h4 className="text-sm font-semibold">Organismos de Tránsito</h4>
        <p className="mt-0.5 text-[11px] opacity-60">
          Los cambios se guardan al instante al marcar; no requieren «Guardar todo».
        </p>
      </div>

      <div
        className="flex max-w-md items-center gap-2 rounded-xl border bg-white p-2.5 dark:bg-[#0B0F14]"
      >
        <Search className="h-4 w-4 opacity-60" />
        <label htmlFor="ot-search" className="sr-only">
          Buscar organismo de tránsito
        </label>
        <input
          id="ot-search"
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
        <ul className="space-y-2" data-testid="ot-list">
          {filtered.map((office) => {
            const checked = granted.has(office.id);
            const { operable, reason } = operability(operationalById?.[office.id]);
            // No se puede habilitar un OT no operable; si ya está habilitado, sí se
            // permite desmarcar (quitar el grant no tiene restricción — HU #10518).
            const blockEnable = !operable && !checked;
            const isPending = pending.has(office.id);
            const rowDisabled = isPending || blockEnable;
            return (
              <li
                key={office.id}
                className="flex items-center justify-between rounded-xl border bg-white px-4 py-3 text-xs dark:bg-[#0B0F14]"
              >
                <label
                  htmlFor={`ot-${office.id}`}
                  className={`flex flex-1 items-center gap-3 ${rowDisabled ? "cursor-not-allowed" : "cursor-pointer"}`}
                >
                  <input
                    id={`ot-${office.id}`}
                    type="checkbox"
                    checked={checked}
                    disabled={rowDisabled}
                    aria-describedby={reason ? `ot-${office.id}-reason` : undefined}
                    onChange={() => handleToggle(office)}
                    className="h-4 w-4 accent-[#557EFF]"
                  />
                  <span className={blockEnable ? "opacity-60" : undefined}>
                    <span className="font-semibold">{office.name}</span>
                    <span className="ml-2 font-mono opacity-60">{office.code}</span>
                    {reason && (
                      <span
                        id={`ot-${office.id}-reason`}
                        className="ml-2 inline-block rounded-full border px-2 py-0.5 text-[10px] font-semibold"
                        style={{ color: "#b25a00", borderColor: "#f0c38e", background: "#fff7ed" }}
                      >
                        {reason} en FLIT
                      </span>
                    )}
                  </span>
                </label>
                {isPending ? (
                  <span className="shrink-0 text-[10px] opacity-60">Guardando…</span>
                ) : justSaved.has(office.id) ? (
                  <span
                    className="flex shrink-0 items-center gap-1 text-[10px] font-semibold"
                    style={{ color: "#0a8f8b" }}
                    role="status"
                  >
                    <Check className="h-3 w-3" /> Guardado
                  </span>
                ) : null}
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
