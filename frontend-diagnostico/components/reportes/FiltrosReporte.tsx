"use client";

import { useId, useState } from "react";
import { Button } from "@flit/ui/Button";
import { ChipFiltro, CHIPS_FILTRO_FILA_CLS } from "@flit/ui/ChipsFiltro";
import { CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";
import { FiltrosDashboard } from "@/components/estadisticas/FiltrosDashboard";
import { SelectorCompania } from "@/components/estadisticas/SelectorCompania";
import { normalizarPlaca } from "@/lib/reportes/api";
import { admitePlaca, filtrosEnum, filtrosPorDefecto, hayFiltros } from "@/lib/reportes/catalogo";
import { etiquetaFiltro, etiquetaValor } from "@/lib/reportes/formato";
import type { FiltroCatalogo, FiltrosReporteValor, ReporteCatalogo } from "@/lib/reportes/tipos";

/**
 * Filtros del reporte activo (spec-ui §5.3): el periodo (mismo par de fechas del dashboard, se aplica al cambiar), la
 * compañía (solo vista global), los enums que trae el catálogo como chips (`aria-pressed`; los repetibles acumulan) y la
 * placa como prefijo, que se aplica al confirmar y vive solo en el estado (nunca en la URL, SP-18). «Limpiar filtros»
 * aparece solo si algo difiere del predeterminado.
 */
export function FiltrosReporte({
  reporte,
  valor,
  hoy,
  vistaGlobal,
  onCambiar,
  errorRango = null,
}: {
  reporte: ReporteCatalogo;
  valor: FiltrosReporteValor;
  hoy: string;
  vistaGlobal: boolean;
  onCambiar: (valor: FiltrosReporteValor) => void;
  errorRango?: string | null;
}) {
  const id = useId();
  const [placa, setPlaca] = useState(valor.placa);
  const [placaAplicada, setPlacaAplicada] = useState(valor.placa);
  if (valor.placa !== placaAplicada) {
    setPlacaAplicada(valor.placa);
    setPlaca(valor.placa);
  }

  function alternar(f: FiltroCatalogo, v: string) {
    const actuales = valor.filtros[f.clave] ?? [];
    const activo = actuales.includes(v);
    const siguientes = f.repetible ? (activo ? actuales.filter((x) => x !== v) : [...actuales, v]) : activo ? [] : [v];
    const filtros = { ...valor.filtros };
    if (siguientes.length > 0) filtros[f.clave] = siguientes;
    else delete filtros[f.clave];
    onCambiar({ ...valor, filtros });
  }

  return (
    <div role="group" aria-label="Filtros del reporte" className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:flex lg:flex-wrap lg:items-end">
      <FiltrosDashboard valor={valor.rango} hoy={hoy} onAplicar={(rango) => onCambiar({ ...valor, rango })} errorServidor={errorRango} />
      {vistaGlobal ? <SelectorCompania value={valor.empresa} onChange={(empresa) => onCambiar({ ...valor, empresa })} /> : null}
      {filtrosEnum(reporte).map((f) => (
        <div key={f.clave} role="group" aria-labelledby={`${id}-${f.clave}`} className="flex flex-col gap-1">
          <span id={`${id}-${f.clave}`} className={ETIQUETA_CLS}>
            {etiquetaFiltro(f.clave)}
          </span>
          <div className={CHIPS_FILTRO_FILA_CLS}>
            {f.valores.map((v) => (
              <ChipFiltro key={v} etiqueta={etiquetaValor(reporte.tipo, f.clave, v)} activo={(valor.filtros[f.clave] ?? []).includes(v)} onClick={() => alternar(f, v)} />
            ))}
          </div>
        </div>
      ))}
      {admitePlaca(reporte) ? (
        <form
          role="search"
          aria-label="Buscar por placa"
          className="flex flex-col gap-1"
          onSubmit={(e) => {
            e.preventDefault();
            onCambiar({ ...valor, placa: normalizarPlaca(placa) });
          }}
        >
          <label htmlFor={`${id}-placa`} className={ETIQUETA_CLS}>
            Placa (inicio)
          </label>
          <div className="flex gap-2">
            <input
              id={`${id}-placa`}
              type="text"
              inputMode="text"
              autoComplete="off"
              maxLength={20}
              value={placa}
              onChange={(e) => setPlaca(e.target.value)}
              className={`${CAMPO_CLS} w-full uppercase sm:w-36`}
            />
            <Button type="submit" variant="secondary" size="sm">
              Buscar
            </Button>
          </div>
        </form>
      ) : null}
      {hayFiltros(valor, hoy) ? (
        <Button variant="ghost" size="sm" onClick={() => onCambiar(filtrosPorDefecto(hoy))}>
          Limpiar filtros
        </Button>
      ) : null}
    </div>
  );
}
