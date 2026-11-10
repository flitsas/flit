"use client";

import Link from "next/link";
import { ESTADO_LABELS, ESTADOS_TRAMITE, estadoAcentoVar, type EstadoTramite } from "@/lib/tramites/estados";
import { tramitesPorEstado } from "./destinos";
import type { ConteosEstado } from "./useConteosEstado";

/**
 * HU #14481 (referencia de Samuel) — la misma tira de baldosas que `FranjaEstados` de /tramites (HU #14482): franja
 * blanca, una baldosa por estado con el nombre, la cifra grande y la barra del color del estado, sin íconos. Aquí cada
 * baldosa es un enlace (no un botón de filtro), así que se repite su receta en vez de reutilizar el componente.
 */
const FRANJA_CLS =
  "flex list-none flex-wrap gap-2 rounded-2xl bg-[var(--di-surface-elevated)] p-2 shadow-[var(--shadow-card)]";

const BALDOSA_CLS =
  "flex h-full flex-col items-center justify-start gap-1 rounded-xl border border-transparent " +
  "bg-[var(--di-surface-elevated)] px-3 pb-2.5 pt-3 text-center no-underline transition-colors " +
  "hover:bg-[var(--di-accent-soft)] focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand " +
  "focus-visible:ring-offset-2 focus-visible:ring-offset-background";

/**
 * HU #14481 (AC2) — cifras de trámites por estado. Cada cifra es un enlace a /tramites filtrado por ese estado; el
 * número sale de estado-counts, nada más. Sin datos del servidor no se dibujan cifras.
 */
export function CifrasEstado({ conteos }: { conteos: ConteosEstado }) {
  return (
    <section aria-labelledby="inicio-cifras-titulo">
      <h2 id="inicio-cifras-titulo" className="text-base font-semibold text-[color:var(--di-heading)]">
        Tus trámites por estado
      </h2>
      <p className="mt-0.5 text-xs text-[color:var(--di-text-tertiary)]">Pulsa una cifra para ver esos trámites.</p>

      {conteos.estado === "cargando" ? (
        <p className="mt-4 text-sm text-[color:var(--di-text-tertiary)]" role="status">
          Cargando cifras…
        </p>
      ) : conteos.estado === "sin-datos" ? (
        <p className="mt-4 text-sm text-[color:var(--di-text-tertiary)]">Por ahora no hay cifras para mostrar.</p>
      ) : (
        <ul className={`mt-4 ${FRANJA_CLS}`}>
          {ESTADOS_TRAMITE.map((estado) => (
            <li key={estado} className="min-w-[7.5rem] flex-1">
              <Cifra estado={estado} total={conteos.conteos[estado] ?? 0} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function Cifra({ estado, total }: { estado: EstadoTramite; total: number }) {
  const etiqueta = ESTADO_LABELS[estado];
  return (
    <Link
      href={tramitesPorEstado(estado)}
      className={BALDOSA_CLS}
      aria-label={`${etiqueta}: ${total} ${total === 1 ? "trámite" : "trámites"}`}
    >
      {/* Hasta dos líneas, como en /tramites: «Solicitud de revocatoria» no cabe en una. */}
      <span className="max-w-full text-[0.8125rem] font-medium leading-tight text-[color:var(--di-text-primary)]" aria-hidden="true">
        {etiqueta}
      </span>
      <span className="text-[1.375rem] font-bold leading-none tabular-nums text-[color:var(--di-heading)]" aria-hidden="true">
        {total.toLocaleString("es-CO")}
      </span>
      <span className="mt-1.5 h-1 w-3/5 rounded-full" style={{ background: estadoAcentoVar(estado) }} aria-hidden="true" />
    </Link>
  );
}
