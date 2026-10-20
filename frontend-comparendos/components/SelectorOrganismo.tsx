"use client";

import { useState } from "react";
import { apiFetch } from "@/lib/api";
import type { Organismo, Pagina } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";
import { INPUT_CLS } from "@/components/ui/estilos";

/** Elige un organismo de tránsito buscando por nombre, código o municipio. */
export function SelectorOrganismo({ valor, onCambio, etiqueta = "Organismo de tránsito" }: { valor: string | null; onCambio: (id: string | null, nombre: string | null) => void; etiqueta?: string }) {
  const [buscar, setBuscar] = useState("");
  const ots = useCarga(`sel-ot:${buscar}`, (signal) => apiFetch<Pagina<Organismo>>("/organismos", { signal, query: { buscar, tamano: 30 } }));
  return (
    <div className="flex flex-col gap-1">
      <input className={INPUT_CLS} value={buscar} onChange={(e) => setBuscar(e.target.value)} placeholder="Buscar organismo…" aria-label={`Buscar ${etiqueta}`} />
      <select
        className={INPUT_CLS}
        value={valor ?? ""}
        aria-label={etiqueta}
        onChange={(e) => {
          const ot = ots.datos?.items.find((o) => o.id === e.target.value);
          onCambio(e.target.value || null, ot?.nombre ?? null);
        }}
      >
        <option value="">{ots.estado === "loading" ? "Buscando…" : "Sin organismo"}</option>
        {(ots.datos?.items ?? []).map((o) => (
          <option key={o.id} value={o.id}>
            {o.nombre} ({o.codigo})
          </option>
        ))}
      </select>
    </div>
  );
}
