"use client";

import Link from "next/link";
import { TarjetaMetrica, METRICAS_GRID_CLS } from "@flit/ui/TarjetaMetrica";
import { rutaIlustracion, type Ilustracion } from "@flit/ui/ilustraciones";
import { ESTADO_LABELS, ESTADOS_TRAMITE, type EstadoTramite } from "@/lib/tramites/estados";
import { tramitesPorEstado } from "./destinos";
import type { ConteosEstado } from "./useConteosEstado";

/**
 * Ilustración de cada estado en su tarjeta (prototipo «Diseño Inteligente», HU #14481). Las que el prototipo usa en su
 * Inicio real se toman de ahí (subsanación → pendientes, entregado → organismos, aprobado → trazabilidad); las demás,
 * la del módulo más cercano.
 */
const ILUSTRACION_ESTADO: Record<EstadoTramite, Ilustracion> = {
  borrador: "documentos",
  anulado: "vencidos",
  preparado: "procesos",
  preasignacion: "preasignacion",
  asignado: "consulta-placa",
  entregado: "organismos",
  aprobado: "trazabilidad",
  rechazado: "causales",
  revocado: "revocatorias",
  subsanacion: "pendientes",
};

/** El estado que espera que alguien haga algo: su tarjeta lleva el borde turquesa del prototipo si tiene trámites. */
const ESTADO_PENDIENTE: EstadoTramite = "subsanacion";

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
        <ul className={`mt-4 list-none p-0 ${METRICAS_GRID_CLS}`}>
          {ESTADOS_TRAMITE.map((estado) => (
            <li key={estado} className="grid">
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
    <TarjetaMetrica
      href={tramitesPorEstado(estado)}
      enlace={Link}
      etiqueta={etiqueta}
      cifra={total.toLocaleString("es-CO")}
      ilustracion={rutaIlustracion(ILUSTRACION_ESTADO[estado])}
      pendiente={estado === ESTADO_PENDIENTE && total > 0}
      aria-label={`${etiqueta}: ${total} ${total === 1 ? "trámite" : "trámites"}`}
    />
  );
}
