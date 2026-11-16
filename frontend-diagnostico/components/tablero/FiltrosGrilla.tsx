"use client";

import { useId, useRef, type ReactNode, type RefObject } from "react";
import { CHIPS_FILTRO_FILA_CLS, ChipFiltro } from "@flit/ui/ChipsFiltro";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { iconoComo } from "@flit/ui/iconoComo";
import { AYUDA_CLS, BOTON_SECUNDARIO_CLS, CAMPO_CLS, ETIQUETA_CLS } from "@/components/configuracion/estilos";
import { nombreFuente, type Fuente } from "@/lib/configuracion";
import type { FilaVehiculo, Grilla } from "@/lib/tablero/api";
import {
  DICTAMENES_FILTRO,
  contarCambioPropietario,
  fuentesFiltrables,
  hayDatoCambioPropietario,
  hayVeredictos,
  opcionesEstado,
  type DictamenFiltro,
  type EstadoFiltro,
  type FiltrosGrilla as ValorFiltros,
} from "@/lib/tablero/filtrosTablero";
import { BuscadorGrilla } from "./BuscadorGrilla";

/** Select de la barra densa: 36 px de alto (T6), el resto del campo del kit. */
const SELECT_CLS = CAMPO_CLS.replace(/\bh-11\b/, "h-9").replace(/\btext-base\b/, "text-sm");
/** Control de la barra: ancho propio en escritorio y fila completa en celular (spec-ui §6.6). */
const CONTROL_CLS = "flex min-w-[11rem] flex-col gap-1 max-[640px]:w-full";
/** «Limpiar filtros»: botón fantasma pequeño (Button ghost sm del kit) con el foco de la app. */
const LIMPIAR_CLS =
  "inline-flex min-h-9 items-center rounded-full px-3 text-sm font-semibold text-[color:var(--di-action)] hover:bg-[var(--kit-hover)] " +
  "disabled:cursor-default disabled:opacity-55 disabled:hover:bg-transparent focus-visible:outline-2 focus-visible:outline-offset-2 " +
  "focus-visible:outline-[var(--di-action)] max-[640px]:w-full max-[640px]:justify-center";

export interface FiltrosGrillaProps {
  /** Campo del buscador: quien pinta la barra le devuelve el foco tras «Limpiar filtros» del vacío (CR-3, spec-ui §6.6). */
  buscadorRef?: RefObject<HTMLInputElement | null>;
  grilla: Pick<Grilla, "columnas">;
  /** Todas las filas de la grilla (las opciones y conteos salen de ellas, no de las filtradas). */
  vehiculos: readonly FilaVehiculo[];
  valor: ValorFiltros;
  onCambiar: (valor: ValorFiltros) => void;
  /** Limpia la barra y lo que controla quien la pinta (tarjeta-filtro, chips de saneamiento). */
  onLimpiar: () => void;
  /** Hay algún filtro activo, también fuera de la barra (tarjeta-filtro o saneamiento): habilita «Limpiar filtros». */
  hayActivos: boolean;
  /** Controles extra al final de la barra, antes de «Limpiar filtros» (chips Con / Sin saneamiento del tablero). */
  extra?: ReactNode;
  /** Contador «Mostrando N de M», a la derecha (aria-live). */
  resumen: ReactNode;
}

/**
 * Barra de filtros de la grilla del lote (HU #14584, spec-ui §6.6), la misma en el tablero de la compañía y en el backoffice:
 * búsqueda por placa o VIN, Fuente + Estado (el estado se habilita al elegir fuente y ofrece solo los presentes, con conteo),
 * Dictamen (si alguna fila trae veredicto), el chip «Cambio de propietario (n)» (si alguna fila trae el dato) y «Limpiar
 * filtros», siempre visible y deshabilitado sin filtros, que devuelve el foco al buscador. Filtra en memoria, sin parámetros
 * nuevos en la API (contrato §2) y sin escribir en la URL (SP-21).
 */
export function FiltrosGrilla({ grilla, vehiculos, valor, onCambiar, onLimpiar, hayActivos, extra, resumen, buscadorRef }: FiltrosGrillaProps) {
  const id = useId();
  const propio = useRef<HTMLInputElement>(null);
  const buscador = buscadorRef ?? propio;
  const fuentes = fuentesFiltrables(grilla.columnas);
  const estados = valor.fuente ? opcionesEstado(vehiculos, valor.fuente) : [];
  const conDictamen = hayVeredictos(vehiculos);
  const conCambio = hayDatoCambioPropietario(vehiculos);
  const cambios = contarCambioPropietario(vehiculos);
  const ayudaEstado = `${id}-ayuda-estado`;

  const limpiar = () => {
    onLimpiar();
    buscador.current?.focus();
  };

  return (
    <div className="flex flex-wrap items-end justify-between gap-3">
      <div className="flex flex-wrap items-end gap-3">
        <BuscadorGrilla ref={buscador} valor={valor.busqueda} onCambiar={(busqueda) => onCambiar({ ...valor, busqueda })} />
        <div className={CONTROL_CLS}>
          <label htmlFor={`${id}-fuente`} className={ETIQUETA_CLS}>
            Fuente
          </label>
          <select
            id={`${id}-fuente`}
            value={valor.fuente ?? ""}
            onChange={(e) => onCambiar({ ...valor, fuente: (e.target.value || null) as Fuente | null, estado: null })}
            className={SELECT_CLS}
          >
            <option value="">Todas</option>
            {fuentes.map((f) => (
              <option key={f} value={f}>
                {nombreFuente(f)}
              </option>
            ))}
          </select>
        </div>
        <div className={CONTROL_CLS}>
          <label htmlFor={`${id}-estado`} className={ETIQUETA_CLS}>
            Estado
          </label>
          <select
            id={`${id}-estado`}
            value={valor.estado ?? ""}
            disabled={!valor.fuente}
            aria-describedby={valor.fuente ? undefined : ayudaEstado}
            onChange={(e) => onCambiar({ ...valor, estado: (e.target.value || null) as EstadoFiltro | null })}
            className={SELECT_CLS}
          >
            <option value="">Todos</option>
            {estados.map((o) => (
              <option key={o.estado} value={o.estado}>
                {`${o.etiqueta} (${o.cantidad})`}
              </option>
            ))}
          </select>
          {!valor.fuente && (
            <span id={ayudaEstado} className={AYUDA_CLS}>
              Elige primero una fuente
            </span>
          )}
        </div>
        {conDictamen && (
          <div className={CONTROL_CLS}>
            <label htmlFor={`${id}-dictamen`} className={ETIQUETA_CLS}>
              Dictamen
            </label>
            <select
              id={`${id}-dictamen`}
              value={valor.dictamen ?? ""}
              onChange={(e) => onCambiar({ ...valor, dictamen: (e.target.value || null) as DictamenFiltro | null })}
              className={SELECT_CLS}
            >
              <option value="">Todos</option>
              {DICTAMENES_FILTRO.map((d) => (
                <option key={d.valor} value={d.valor}>
                  {d.etiqueta}
                </option>
              ))}
            </select>
          </div>
        )}
        {(conCambio || extra) && (
          <div className={CHIPS_FILTRO_FILA_CLS}>
            {conCambio && (
              <ChipFiltro
                etiqueta={`Cambio de propietario (${cambios})`}
                activo={valor.cambioPropietario}
                onClick={() => onCambiar({ ...valor, cambioPropietario: !valor.cambioPropietario })}
              />
            )}
            {extra}
          </div>
        )}
        <button type="button" onClick={limpiar} disabled={!hayActivos} className={LIMPIAR_CLS}>
          Limpiar filtros
        </button>
      </div>
      <p aria-live="polite" aria-atomic="true" className={AYUDA_CLS}>
        {resumen}
      </p>
    </div>
  );
}

const IconoBuscar = iconoComo("buscar");

/** Vacío por filtro (spec-ui §6.6): «Ningún vehículo coincide» con «Limpiar filtros» (texto unificado). */
export function VacioFiltros({ onLimpiar, mensaje = "Prueba con otra placa o VIN, o quita alguno de los filtros." }: { onLimpiar: () => void; mensaje?: string }) {
  return (
    <EstadoVacio
      anunciar
      nivelTitulo={3}
      icono={<IconoBuscar className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
      titulo="Ningún vehículo coincide"
      mensaje={mensaje}
      acciones={[
        <button key="limpiar" type="button" onClick={onLimpiar} className={BOTON_SECUNDARIO_CLS}>
          Limpiar filtros
        </button>,
      ]}
    />
  );
}
