"use client";

import { ChipFiltro, EMBUDO_CLS } from "@flit/ui/ChipsFiltro";
import type { Grilla } from "@/lib/tablero/api";
import { tarjetasVisibles, type FiltroTablero } from "@/lib/tablero/filtrosTablero";

export interface MetricasLoteProps {
  grilla: Grilla;
  filtro: FiltroTablero | null;
  onFiltro: (filtro: FiltroTablero | null) => void;
}

/** Tono de la cifra (spec-ui §6.3): sufijo de --badge-*-fg, o «entregado» (--estado-entregado-fg). */
type TonoMetrica = "info" | "success" | "danger" | "warning" | "entregado" | "neutral";

/** Ícono (disco del kit) y tono de cada casilla, por la clave de la métrica. */
const VISTA: Readonly<Record<FiltroTablero, { icono: string; tono: TonoMetrica }>> = {
  vehiculos: { icono: "/assets/kit/estado-placa.svg", tono: "info" },
  terminados: { icono: "/assets/kit/estado-aprobado.svg", tono: "success" },
  con_error: { icono: "/assets/kit/aviso-error.svg", tono: "danger" },
  con_deuda: { icono: "/assets/kit/aviso-alerta.svg", tono: "warning" },
  bloqueados: { icono: "/assets/kit/estado-anulado.svg", tono: "danger" },
  listos_tramite: { icono: "/assets/kit/estado-entregado.svg", tono: "entregado" },
};

const colorDe = (tono: TonoMetrica) => (tono === "entregado" ? "var(--estado-entregado-fg)" : `var(--badge-${tono}-fg)`);

/**
 * Embudo del kit en UNA fila (A6.5): se sustituye la rejilla auto-fit (que partía las seis casillas en dos líneas) por
 * columnas iguales en flujo horizontal; por debajo de 640 px sigue el carril deslizable de EMBUDO_CLS.
 */
const EMBUDO_UNA_FILA_CLS = EMBUDO_CLS.replace(/\bgrid-cols-\[[^\]]+\]/, "grid-flow-col auto-cols-fr");

/**
 * Métricas-filtro del tablero (HU #14562 AC27/AC31; una fila con ícono y tono HU #14584, spec-ui §6.3): cifras de
 * `grilla.conteos` (sin pedir el dictamen por vehículo). Cada casilla del embudo es un botón con aria-pressed y nombre
 * «Etiqueta: cifra»; son exclusivas: activar una apaga las demás y volver a pulsarla quita el filtro. La cifra lleva el tono
 * de su estado por token (T1); una cifra 0 va en neutral para no alarmar. El ícono es decorativo (alt="").
 * El tono se pinta en la cifra que recibe ChipFiltro (ReactNode) sin cambiar `@flit/ui` (prop `tono` pendiente del kit).
 */
export function MetricasLote({ grilla, filtro, onFiltro }: MetricasLoteProps) {
  const tarjetas = tarjetasVisibles(grilla);
  return (
    <div role="group" aria-label="Filtros del lote" className={EMBUDO_UNA_FILA_CLS}>
      {tarjetas.map((t) => {
        const { icono, tono: tonoBase } = VISTA[t.clave];
        const tono = t.cifra === 0 ? "neutral" : tonoBase;
        return (
          <ChipFiltro
            key={t.clave}
            etiqueta={t.etiqueta}
            icono={icono}
            cifra={
              <span data-tono={tono} style={{ color: colorDe(tono) }}>
                {t.cifra}
              </span>
            }
            aria-label={`${t.etiqueta}: ${t.cifra}`}
            activo={filtro === t.clave}
            onClick={() => onFiltro(filtro === t.clave ? null : t.clave)}
          />
        );
      })}
    </div>
  );
}
