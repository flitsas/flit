"use client";

import { InlineAlert } from "@flit/ui";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
import { Chip } from "@/components/ui/Chip";
import { apiFetch } from "@/lib/api";
import { infraccionConDescripcion, mensajeMotivo, razonLegible, type DecisionDp } from "@/lib/decisionDp";
import { useCarga } from "@/lib/useCarga";

/**
 * «Qué se puede alegar» y «Qué conviene hacer» (HU #14613): lo que la API decide con los datos de hoy, en palabras de la
 * gestora. Solo lee; no envía nada. Las acciones siguen en «Generar derecho de petición».
 */
export function PanelAlegar({ ctx }: { ctx: ContextoDetalle }) {
  const d = ctx.detalle;
  const carga = useCarga(`dp-decision:${d.id}`, (signal) => apiFetch<DecisionDp>(`/comparendos/${d.id}/dp/decision`, { signal }));
  const decision = carga.datos;
  const motivo = decision && !decision.elegible ? mensajeMotivo(decision.motivo) : null;
  const elegida = decision?.aplicables.find((a) => a.codigo === decision.elegida) ?? null;
  const infraccion = infraccionConDescripcion(d.infraccion.codigo, d.infraccion.descripcion);

  return (
    <section aria-label="Qué se puede alegar" className="flex min-w-0 flex-col gap-3">
      <h4 className="text-xs font-semibold">Qué se puede alegar</h4>
      {infraccion && <p className="break-words text-xs opacity-70">Infracción: {infraccion}</p>}
      {carga.estado === "loading" && <p className="text-xs opacity-60">Revisando qué se puede alegar…</p>}
      {carga.estado === "error" && <InlineAlert tone="error">{carga.error}</InlineAlert>}
      {decision && (
        <>
          {decision.aplicables.length === 0 ? (
            <p className="text-xs opacity-70">Todavía no aplica ningún argumento.</p>
          ) : (
            <ul className="flex flex-col gap-2">
              {decision.aplicables.map((a) => {
                const marcada = a.codigo === decision.elegida;
                return (
                  <li key={a.codigo} className={`flex min-w-0 flex-col gap-1 rounded-md border px-3 py-2 ${marcada ? "border-primary" : "border-border"}`}>
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="break-words text-xs font-semibold">{a.nombre}</span>
                      {marcada && <Chip e={{ texto: "La que conviene", tono: "success" }} />}
                      {!a.disponible && <Chip e={{ texto: "No habilitada en tu empresa", tono: "warning" }} />}
                    </div>
                    <p className="break-words text-xs opacity-80">{razonLegible(a.razon, d.infraccion)}</p>
                  </li>
                );
              })}
            </ul>
          )}
          <h4 className="text-xs font-semibold">Qué conviene hacer</h4>
          {decision.elegible && elegida ? (
            <InlineAlert tone="info" compact>
              Conviene alegar «{elegida.nombre}». Elígela abajo en «Generar derecho de petición» y revisa la vista previa antes de enviar.
            </InlineAlert>
          ) : (
            motivo && (
              <InlineAlert tone={motivo.tono} title={motivo.titulo} compact>
                {motivo.detalle}
              </InlineAlert>
            )
          )}
        </>
      )}
    </section>
  );
}
