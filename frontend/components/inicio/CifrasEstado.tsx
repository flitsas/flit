"use client";

import Link from "next/link";
import { ESTADO_LABELS, ESTADOS_TRAMITE, type EstadoTramite } from "@/lib/tramites/estados";
import { tramitesPorEstado } from "./destinos";
import type { ConteosEstado } from "./useConteosEstado";

/**
 * HU #14481 (AC2) — cifras de trámites por estado. Cada cifra es un enlace a /tramites filtrado por ese estado; el
 * número sale de estado-counts, nada más. Sin datos del servidor no se dibujan cifras.
 */
export function CifrasEstado({ conteos }: { conteos: ConteosEstado }) {
  return (
    <section aria-labelledby="inicio-cifras-titulo" className="rounded-[var(--radius-xl)] border border-[var(--border)] bg-[var(--card)] p-4 text-[var(--card-foreground)] shadow-[var(--shadow-card)] md:p-5">
      <h2 id="inicio-cifras-titulo" className="text-base font-semibold text-[var(--foreground)]">
        Tus trámites por estado
      </h2>
      <p className="mt-0.5 text-xs text-[var(--muted-foreground)]">Pulsa una cifra para ver esos trámites.</p>

      {conteos.estado === "cargando" ? (
        <p className="mt-4 text-sm text-[var(--muted-foreground)]" role="status">
          Cargando cifras…
        </p>
      ) : conteos.estado === "sin-datos" ? (
        <p className="mt-4 text-sm text-[var(--muted-foreground)]">Por ahora no hay cifras para mostrar.</p>
      ) : (
        <ul className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
          {ESTADOS_TRAMITE.map((estado) => (
            <li key={estado}>
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
      aria-label={`${etiqueta}: ${total} ${total === 1 ? "trámite" : "trámites"}`}
      className="flex min-h-16 flex-col justify-between gap-1 rounded-[var(--radius-lg)] border px-3 py-2 no-underline transition-shadow hover:shadow-[var(--shadow-card)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--ring)]"
      style={{ background: `var(--estado-${estado}-bg)`, borderColor: `var(--estado-${estado}-border)` }}
    >
      <span className="text-xs font-medium" style={{ color: `var(--estado-${estado}-fg)` }}>
        {etiqueta}
      </span>
      <span className="text-2xl font-bold tabular-nums text-[var(--foreground)]">{total.toLocaleString("es-CO")}</span>
    </Link>
  );
}
