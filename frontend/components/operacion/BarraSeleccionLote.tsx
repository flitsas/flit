'use client';

import { useEffect, useRef, type ReactNode } from 'react';
import { InlineAlert } from '@/components/atom/InlineAlert';
import type { EstadoCabeceraLote } from '@/hooks/useSeleccionLote';

/**
 * HU #13380 (épica #13216) — barra de selección para la descarga masiva de consolidados.
 *
 * <p>Lleva la casilla «Seleccionar todos (del filtro)» y el contador. Vive FUERA del cuerpo de la
 * tabla a propósito: la tabla desaparece en sus estados cargando/error/vacío, y la cabecera de
 * selección tiene que seguir ahí, deshabilitada y sin contador (AC7). En esta HU la barra solo
 * cuenta; el botón «Descargar ZIP» y su modal llegan con #13381 por `children`.</p>
 *
 * <p>Accesibilidad (WCAG 2.1 AA): la casilla de cabecera expone `aria-checked="mixed"` en parcial
 * (además de `indeterminate`), y el contador vive en una región `aria-live="polite"` que existe
 * desde el primer render — una región creada junto con su texto no se anuncia.</p>
 */

/** Estado del listado que alimenta la barra (los 4 estados de la tabla). */
export type EstadoTablaLote = 'cargando' | 'error' | 'vacio' | 'lleno';

export interface BarraSeleccionLoteProps {
  estadoTabla: EstadoTablaLote;
  estadoCabecera: EstadoCabeceraLote;
  contador: number;
  /** Selección «todos los del filtro» (cambia el texto del contador). */
  todosDelFiltro: boolean;
  onAlternarTodos: () => void;
  onLimpiar: () => void;
  /** Mensaje del tope de 10.000, o `null`. */
  mensajeTope: string | null;
  /** Acciones de la barra (p. ej. «Descargar ZIP», #13381). */
  children?: ReactNode;
  /**
   * Nombre accesible de la región. Por defecto el de la descarga masiva (bandeja del OT, #13393);
   * el listado de trámites la nombra por lo que hace hoy: descarga y pausa en lote (#13380).
   */
  etiquetaRegion?: string;
  /**
   * «Seleccionar todos (del filtro)» solo tiene sentido para la descarga masiva. Sin ese permiso la
   * barra del listado sirve únicamente a la pausa ICT, que se marca fila a fila. Por defecto `true`.
   */
  permiteSeleccionarTodos?: boolean;
}

const formatoMiles = (n: number) => n.toLocaleString('es-CO');

export function textoContadorLote(contador: number, todosDelFiltro: boolean): string {
  const base = `${formatoMiles(contador)} trámite${contador === 1 ? '' : 's'} seleccionado${contador === 1 ? '' : 's'}`;
  return todosDelFiltro ? `${base} (todos los del filtro)` : base;
}

export function BarraSeleccionLote({
  estadoTabla,
  estadoCabecera,
  contador,
  todosDelFiltro,
  onAlternarTodos,
  onLimpiar,
  mensajeTope,
  children,
  etiquetaRegion = 'Selección para descarga masiva de consolidados',
  permiteSeleccionarTodos = true,
}: BarraSeleccionLoteProps) {
  const casillaRef = useRef<HTMLInputElement>(null);
  const lleno = estadoTabla === 'lleno';
  const parcial = lleno && estadoCabecera === 'parcial';

  // `indeterminate` no tiene atributo HTML: solo se fija por propiedad del DOM.
  useEffect(() => {
    if (casillaRef.current) casillaRef.current.indeterminate = parcial;
  }, [parcial]);

  return (
    <div className="flex flex-col gap-2">
      <div
        role="region"
        aria-label={etiquetaRegion}
        className="flex flex-wrap items-center gap-3 rounded-xl border border-flit-brand/30 bg-flit-brand/[0.06] px-3 py-2 text-xs"
      >
        {permiteSeleccionarTodos ? (
          <label
            className={`inline-flex items-center gap-2 font-semibold text-flit-primary dark:text-white ${
              lleno ? 'cursor-pointer' : 'cursor-not-allowed opacity-60'
            }`}
          >
            <input
              ref={casillaRef}
              type="checkbox"
              disabled={!lleno}
              checked={lleno && estadoCabecera === 'todo'}
              aria-checked={parcial ? 'mixed' : lleno && estadoCabecera === 'todo'}
              onChange={onAlternarTodos}
              className="h-4 w-4 shrink-0 cursor-pointer accent-flit-brand focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-1 disabled:cursor-not-allowed"
            />
            Seleccionar todos (del filtro)
          </label>
        ) : null}

        {/* Región viva siempre montada; vacía mientras la tabla no está llena (AC7). */}
        <span
          role="status"
          aria-live="polite"
          aria-atomic="true"
          data-testid="contador-seleccion-lote"
          className="font-semibold text-flit-primary dark:text-white"
        >
          {lleno ? textoContadorLote(contador, todosDelFiltro) : ''}
        </span>

        {lleno && contador > 0 ? (
          <>
            {children}
            <button
              type="button"
              onClick={onLimpiar}
              className="ml-auto rounded-lg px-2 py-1 font-semibold text-flit-primary/70 transition hover:text-flit-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand dark:text-white/70 dark:hover:text-white"
            >
              Limpiar selección
            </button>
          </>
        ) : null}
      </div>
      {lleno && mensajeTope ? (
        <InlineAlert tone="warning" compact>
          {mensajeTope}
        </InlineAlert>
      ) : null}
    </div>
  );
}

export interface CasillaFilaLoteProps {
  /** Radicado de la fila: nombra la casilla para el lector de pantalla (AC1). */
  radicado: string;
  seleccionado: boolean;
  onAlternar: () => void;
  /**
   * Nombre accesible. Por defecto menciona la descarga masiva (bandeja del OT); el listado de
   * trámites lo pasa neutro, porque la misma casilla sirve también a la pausa ICT (#13380).
   */
  ariaLabel?: string;
  /** Pista visual opcional (p. ej. «Solo consulta: queda excluido de la pausa en lote»). */
  title?: string;
}

/**
 * Casilla de una fila. Detiene la propagación del clic y del teclado: la fila abre el detalle con
 * su `onClick`, y marcar no debe abrirlo (AC1). Espacio la alterna, como cualquier checkbox nativo.
 */
export function CasillaFilaLote({
  radicado,
  seleccionado,
  onAlternar,
  ariaLabel,
  title,
}: CasillaFilaLoteProps) {
  return (
    <span
      className="flex"
      onClick={(e) => e.stopPropagation()}
      onKeyDown={(e) => e.stopPropagation()}
    >
      <input
        type="checkbox"
        checked={seleccionado}
        onChange={onAlternar}
        aria-label={ariaLabel ?? `Seleccionar el trámite ${radicado} para la descarga masiva`}
        title={title}
        className="h-3.5 w-3.5 shrink-0 cursor-pointer accent-flit-brand focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-1"
      />
    </span>
  );
}
