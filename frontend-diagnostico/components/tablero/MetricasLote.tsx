"use client";

import { METRICAS_GRID_CLS, TarjetaMetrica } from "@flit/ui/TarjetaMetrica";
import type { Grilla } from "@/lib/tablero/api";
import { tarjetasVisibles, type FiltroTablero } from "@/lib/tablero/filtrosTablero";

export interface MetricasLoteProps {
  grilla: Grilla;
  filtro: FiltroTablero | null;
  onFiltro: (filtro: FiltroTablero | null) => void;
}

/**
 * Tarjetas-filtro del tablero (HU #14562 AC27/AC31): cifras de `grilla.conteos` (sin pedir el dictamen por vehículo), cada
 * tarjeta un botón con aria-pressed. Son exclusivas: activar una apaga las demás y volver a pulsarla quita el filtro.
 */
export function MetricasLote({ grilla, filtro, onFiltro }: MetricasLoteProps) {
  const tarjetas = tarjetasVisibles(grilla);
  return (
    <section aria-label="Filtros del lote" className={METRICAS_GRID_CLS}>
      {tarjetas.map((t) => (
        <TarjetaMetrica
          key={t.clave}
          etiqueta={t.etiqueta}
          cifra={t.cifra}
          aria-label={`${t.etiqueta}: ${t.cifra}`}
          activa={filtro === t.clave}
          onAlternar={(activa) => onFiltro(activa ? t.clave : null)}
        />
      ))}
    </section>
  );
}
