"use client";

import { DataTable } from "@flit/ui";
import { apiFetch } from "@/lib/api";
import { estadoLegal } from "@/lib/etiquetas";
import type { Tipologia } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

/**
 * Tipologías de derecho de petición habilitadas para la empresa, en solo lectura (AC1 de la HU #13476). Las habilita el
 * SuperAdmin en el parametrizador; aquí la empresa sabe cuáles puede usar.
 */
export function TipologiasEmpresa({ empresa }: { empresa?: string | null }) {
  const lista = useCarga(`tipologias-empresa:${empresa ?? "propia"}`, (signal) =>
    apiFetch<Tipologia[]>("/tipologias", { signal, query: { soloHabilitadas: true, empresa: empresa ?? undefined } }),
  );
  return (
    <section className="flex flex-col gap-2">
      <h2 className="text-sm font-semibold">Tipologías de derecho de petición habilitadas</h2>
      <p className="text-[11px] text-[#5B6B82] dark:text-slate-300">Las habilita FLIT. Si necesitas otra, solicítala a operaciones.</p>
      <DataTable
        ariaLabel="Tipologías habilitadas"
        rows={lista.datos ?? []}
        getRowKey={(t) => t.id}
        status={lista.estado}
        errorMessage={lista.error ?? undefined}
        onRetry={lista.recargar}
        emptyMessage="Tu empresa no tiene tipologías de derecho de petición habilitadas."
        columns={[
          { key: "codigo", header: "Código", render: (t) => <span className="font-mono font-semibold">{t.codigo}</span> },
          { key: "nombre", header: "Tipología", render: (t) => t.nombre },
          { key: "estados", header: "Procede en", render: (t) => t.estadosLegalesPermitidos.map((e) => estadoLegal(e).texto).join(", ") },
          { key: "plazo", header: "Plazo de respuesta del OT", align: "right", render: (t) => `${t.plazoRespuestaOtDiasHabiles} días hábiles` },
          { key: "masivo", header: "Envío masivo", render: (t) => (t.permiteMasivo ? "Sí" : "No") },
        ]}
      />
    </section>
  );
}
