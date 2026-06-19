"use client";

import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import type { TransitOffice } from "@/lib/api/types";

// Matriz de organismos de tránsito (HU #10194, AC4). Catálogo completo en memoria
// con buscador client-side en tiempo real (insensible a mayúsculas y tildes). Cada
// checkbox refleja un grant del tenant; al alternar dispara POST/DELETE con UI
// optimista y rollback ante error.
export interface OTMatrixProps {
  offices: TransitOffice[];
  grantedIds: string[];
  /** Persiste el cambio de grant (POST si enabled, DELETE si !enabled). */
  onToggle: (officeId: string, enabled: boolean) => Promise<void>;
  /** Notificación opcional de error de persistencia (toast). */
  onError?: (message: string) => void;
}

export function OTMatrix({ offices, grantedIds, onToggle, onError }: OTMatrixProps) {
  const [search, setSearch] = useState("");
  const [granted, setGranted] = useState<Set<string>>(() => new Set(grantedIds));
  const [pending, setPending] = useState<Set<string>>(() => new Set());

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
    } catch {
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
      onError?.(`No se pudo ${enable ? "habilitar" : "deshabilitar"} ${office.name}.`);
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
      <h4 className="text-sm font-semibold">Organismos de Tránsito</h4>

      <div
        className="flex max-w-md items-center gap-2 rounded-xl border bg-white p-2.5 dark:bg-[#0B0F14]"
        style={{ borderColor: "#DFE5ED" }}
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
        <p className="rounded-xl border p-4 text-center text-xs opacity-60" style={{ borderColor: "#DFE5ED" }}>
          Ningún organismo coincide con la búsqueda.
        </p>
      ) : (
        <ul className="space-y-2" data-testid="ot-list">
          {filtered.map((office) => {
            const checked = granted.has(office.id);
            return (
              <li
                key={office.id}
                className="flex items-center justify-between rounded-xl border bg-white px-4 py-3 text-xs dark:bg-[#0B0F14]"
                style={{ borderColor: "#DFE5ED" }}
              >
                <label htmlFor={`ot-${office.id}`} className="flex flex-1 cursor-pointer items-center gap-3">
                  <input
                    id={`ot-${office.id}`}
                    type="checkbox"
                    checked={checked}
                    disabled={pending.has(office.id)}
                    onChange={() => handleToggle(office)}
                    className="h-4 w-4 accent-[#557EFF]"
                  />
                  <span>
                    <span className="font-semibold">{office.name}</span>
                    <span className="ml-2 font-mono opacity-60">{office.code}</span>
                  </span>
                </label>
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
