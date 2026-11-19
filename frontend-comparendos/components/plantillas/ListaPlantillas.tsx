"use client";

import { useState } from "react";
import { DataTable, StatusBadge } from "@flit/ui";
import { ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { opciones, TIPO_PLANTILLA, TIPOS_PLANTILLA_SIN_USO } from "@/lib/etiquetas";
import { formatoFecha } from "@/lib/format/date";
import type { PlantillaResumen } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

/** Plantillas por conjunto y tipo, con su versión publicada. La API ya filtra los conjuntos que el usuario puede ver. */
export function ListaPlantillas({ onElegir, recarga = 0 }: { onElegir: (p: PlantillaResumen) => void; recarga?: number }) {
  const [conjunto, setConjunto] = useState("");
  const [tipo, setTipo] = useState("");
  const lista = useCarga(`plantillas:${conjunto}:${tipo}:${recarga}`, (signal) =>
    apiFetch<PlantillaResumen[]>("/plantillas", { signal, query: { conjunto: conjunto || undefined, tipo: tipo || undefined } }),
  );

  return (
    <div className="flex flex-col gap-3">
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <label className={ETIQUETA_CLS}>
          Conjunto
          <select className={INPUT_CLS} value={conjunto} onChange={(e) => setConjunto(e.target.value)}>
            <option value="">Todos</option>
            <option value="Flit">FLIT</option>
            <option value="Renting">Renting</option>
          </select>
        </label>
        <label className={`${ETIQUETA_CLS} md:col-span-2`}>
          Tipo
          <select className={INPUT_CLS} value={tipo} onChange={(e) => setTipo(e.target.value)}>
            <option value="">Todos</option>
            {opciones(TIPO_PLANTILLA).filter((o) => !TIPOS_PLANTILLA_SIN_USO.has(o.valor)).map((o) => (
              <option key={o.valor} value={o.valor}>
                {o.texto}
              </option>
            ))}
          </select>
        </label>
      </div>
      <DataTable
        ariaLabel="Plantillas"
        rows={lista.datos ?? []}
        getRowKey={(p) => p.id}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        onRowClick={onElegir}
        emptyMessage="No hay plantillas con ese filtro."
        columns={[
          { key: "conjunto", header: "Conjunto", render: (p) => <StatusBadge label={p.conjunto === "Renting" ? "Renting" : "FLIT"} tone={p.conjunto === "Renting" ? "info" : "neutral"} /> },
          { key: "tipo", header: "Plantilla", render: (p) => <span className="font-semibold">{TIPO_PLANTILLA[p.tipo] ?? p.tipo}</span> },
          { key: "tipologia", header: "Tipología", render: (p) => p.tipologia ?? "—" },
          { key: "version", header: "Versión publicada", render: (p) => (p.publicada ? `v${p.publicada.numeroVersion}` : <StatusBadge label="Sin publicar" tone="warning" />) },
          { key: "fecha", header: "Publicada", render: (p) => formatoFecha(p.publicada?.publicadaEn) },
        ]}
      />
    </div>
  );
}
