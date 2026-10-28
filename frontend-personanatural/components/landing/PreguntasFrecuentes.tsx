"use client";

import { ChevronDown, CircleHelp } from "lucide-react";
import { useId, useState } from "react";
import { PREGUNTAS_FRECUENTES } from "@/lib/landing/contenido";

// Acordeón de una sola pregunta abierta (como el de radix del diseño), con los mismos atributos data-state que usan sus estilos.
export function PreguntasFrecuentes({ preguntas = PREGUNTAS_FRECUENTES }: { preguntas?: readonly { pregunta: string; respuesta: string }[] }) {
  const base = useId();
  const [abierta, setAbierta] = useState<number | null>(null);
  return (
    <section id="faq" className="dashboard-card dashboard-faq" aria-label="Preguntas Frecuentes">
      <div className="faq-heading">
        <span className="benefit-icon"><CircleHelp aria-hidden className="h-7 w-7 text-primary" /></span>
        <h2 className="dashboard-title flit-gradient-title">Preguntas Frecuentes</h2>
      </div>
      <div className="mt-3 space-y-2">
        {preguntas.map(({ pregunta, respuesta }, i) => {
          const estado = abierta === i ? "open" : "closed";
          return (
            <div key={`${i}-${pregunta}`} data-state={estado} className="rounded-[9px] border border-border px-4">
              <h3 className="flex">
                <button
                  type="button"
                  id={`${base}-t${i}`}
                  aria-expanded={estado === "open"}
                  aria-controls={`${base}-p${i}`}
                  data-state={estado}
                  onClick={() => setAbierta(estado === "open" ? null : i)}
                  className="flex flex-1 items-center justify-between py-4 text-left text-sm font-medium cursor-pointer transition-all hover:underline"
                >
                  {pregunta}
                  <ChevronDown aria-hidden className={`h-4 w-4 shrink-0 text-muted-foreground transition-transform duration-200 ${estado === "open" ? "rotate-180" : ""}`} />
                </button>
              </h3>
              <div id={`${base}-p${i}`} role="region" aria-labelledby={`${base}-t${i}`} hidden={estado !== "open"} data-state={estado} className="overflow-hidden text-sm">
                <div className="pb-4 pt-0">{respuesta}</div>
              </div>
            </div>
          );
        })}
      </div>
    </section>
  );
}
