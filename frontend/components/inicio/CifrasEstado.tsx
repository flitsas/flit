"use client";

import Link from "next/link";
import { CHIP_EMBUDO_CLS, EMBUDO_CLS } from "@flit/ui/ChipsFiltro";
import { cn } from "@flit/ui/cn";
import { ESTADO_ICONO, ESTADO_LABELS, ESTADOS_TRAMITE, type EstadoTramite } from "@/lib/tramites/estados";
import { tramitesPorEstado } from "./destinos";
import type { ConteosEstado } from "./useConteosEstado";

/**
 * UI Kit 2026 — el mismo embudo del kit que `FranjaEstados` de /tramites (`EMBUDO_CLS` / `CHIP_EMBUDO_CLS`): una
 * columna por estado con su disco arriba, el nombre y la cifra, sin barra de color. Aquí cada columna es un enlace (no
 * un botón de filtro), así que se aplican las mismas clases en vez de reutilizar el componente.
 */
const FRANJA_CLS = cn(EMBUDO_CLS, "m-0 list-none");

/** El chip del embudo en columna normal: el DOM ya va en el orden visual (disco, nombre, cifra). */
const BALDOSA_CLS = cn(CHIP_EMBUDO_CLS, "h-full w-full flex-col justify-start no-underline");

/**
 * HU #14481 (AC2) — cifras de trámites por estado. Cada cifra es un enlace a /tramites filtrado por ese estado; el
 * número sale de estado-counts, nada más. Sin datos del servidor no se dibujan cifras.
 */
export function CifrasEstado({ conteos }: { conteos: ConteosEstado }) {
  return (
    <section aria-labelledby="inicio-cifras-titulo">
      <h2 id="inicio-cifras-titulo" className="text-2xl font-medium leading-tight text-[color:var(--kit-azul-texto)]">
        Tus trámites por estado
      </h2>
      <p className="mt-0.5 text-[0.8125rem] text-[color:var(--di-text-tertiary)]">Pulsa una cifra para ver esos trámites.</p>

      {conteos.estado === "cargando" ? (
        <p className="mt-4 text-sm text-[color:var(--di-text-tertiary)]" role="status">
          Cargando cifras…
        </p>
      ) : conteos.estado === "sin-datos" ? (
        <p className="mt-4 text-sm text-[color:var(--di-text-tertiary)]">Por ahora no hay cifras para mostrar.</p>
      ) : (
        <ul className={`mt-4 ${FRANJA_CLS}`}>
          {ESTADOS_TRAMITE.map((estado) => (
            <li key={estado} className="min-w-0">
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
      {/* Disco del estado del kit, decorativo: el nombre accesible del enlace ya dice el estado y la cifra. */}
      {/* eslint-disable-next-line @next/next/no-img-element -- disco estático del kit */}
      <img src={ESTADO_ICONO[estado]} alt="" aria-hidden="true" width={32} height={32} className="mb-0.5 h-8 w-8" />
      {/* Hasta dos líneas, como en /tramites: «Solicitud de revocatoria» no cabe en una. */}
      <span className="max-w-full text-xs font-medium leading-tight text-[color:var(--di-text-primary)]" aria-hidden="true">
        {etiqueta}
      </span>
      <span className="text-xl font-bold leading-none tabular-nums text-[color:var(--di-text-primary)]" aria-hidden="true">
        {total.toLocaleString("es-CO")}
      </span>
    </Link>
  );
}
