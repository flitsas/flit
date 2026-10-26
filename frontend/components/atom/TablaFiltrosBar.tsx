"use client";

/**
 * Barra genérica de filtros por columna (HU #13997, Feature #13996, Épica #13514).
 *
 * Misma forma que Trámites (`TramitesFiltrosBar`): UNA línea encima de la tabla con
 *   buscador (opcional) · Periodo (solo si hay columna de fecha) · «+ Filtro» · Columnas · acciones.
 * «+ Filtro» es un popover de un nivel: lista de columnas filtrables -> al elegir una, el mismo panel
 * muestra su editor con «Volver». El disparador se marca en azul y numera las condiciones aplicadas.
 * Cada tabla solo DECLARA sus columnas; no escribe código propio de filtros.
 *
 * API
 * ---
 *   const COLUMNAS: ColumnaFiltro[] = [
 *     { id: 'nombre', label: 'Nombre', tipo: 'texto' },
 *     { id: 'estado', label: 'Estado', tipo: 'seleccion',
 *       opciones: [{ value: 'ACTIVE', label: 'Activa' }, { value: 'INACTIVE', label: 'Inactiva' }] },
 *     { id: 'creado', label: 'Creación', tipo: 'fecha' },
 *   ];
 *   const filtros = useTablaFiltros({ columnas: COLUMNAS, periodoColumnaId: 'creado', onCambio });
 *   <TablaFiltrosBar filtros={filtros} buscador={{ placeholder: 'Buscar por nombre o NIT...' }}
 *                    columnSelector={<ColumnSelector .../>} acciones={<MiToggle />} />
 *   <TablaFiltrosVacio onLimpiar={filtros.limpiar} />   // cuando hay filtros y 0 filas
 *
 *   - `useTablaFiltros({ columnas, periodoColumnaId?, onCambio? })` -> estado: `busqueda`,
 *     `condiciones` (de «+ Filtro»), `condicionesTotales` (con el periodo), `count`, `hayFiltros`,
 *     `aplicar()`, `limpiar()`. `onCambio` se dispara en todo cambio aplicado: úsalo para volver a
 *     la página 1. Si hay varias columnas `fecha`, `periodoColumnaId` elige cuál usa Periodo; las
 *     demás quedan como rango en «+ Filtro».
 *   - `buscador` omitido = sin buscador. `columnSelector` y `acciones` son slots opcionales.
 *   - Texto saneado con `sanitizeNoAngleBrackets` y `SEARCH_TEXT_MAX_LENGTH`; «Aplicar» nunca agrega
 *     condiciones vacías. El «Aplicar» del editor de una columna aplica de una vez (un paso menos).
 *
 * Ejemplo CLIENTE (la tabla tiene todas las filas en memoria)
 *   const { setPagina } = usePaginacion();
 *   const filtros = useTablaFiltros({ columnas: COLUMNAS, periodoColumnaId: 'creado',
 *                                     onCambio: () => setPagina(1) });
 *   const visibles = useMemo(() => filtrarFilas(filas, filtros.condicionesTotales, filtros.busqueda, {
 *     columnas: { nombre: (f) => f.name, estado: (f) => f.status, creado: (f) => f.createdAt },
 *     busqueda: (f) => [f.name, f.nit],
 *   }), [filas, filtros.condicionesTotales, filtros.busqueda]);
 *   {visibles.length === 0 && filtros.hayFiltros ? <TablaFiltrosVacio onLimpiar={filtros.limpiar} /> : ...}
 *
 * Ejemplo SERVIDOR (la API filtra)
 *   const params = useMemo(() => condicionesAParams(filtros.condicionesTotales, COLUMNAS,
 *                    filtros.busqueda, { busquedaParam: 'search' }), [filtros.condicionesTotales, filtros.busqueda]);
 *   // -> { search: 'abc', status: 'ACTIVE,INACTIVE', createdFrom: '2026-01-01', createdTo: '2026-01-31' }
 *   // Mapeo por columna: `param`, `paramDesde`, `paramHasta`. Re-consulta cuando `params` cambia.
 */

import { useId, useRef, useState, type ReactNode } from "react";
import { ChevronDown, Search, SearchX } from "lucide-react";
import {
  INPUT_CLS,
  PeriodoPopover,
  POPOVER_SURFACE_CLS,
  usePopoverDismiss,
} from "@/components/operacion/TramitesFiltrosBar";
import { controlCls } from "@/components/operacion/tramites-control-styles";
import { WIZARD_CTA_GRADIENT } from "@/components/operacion/wizard-field-styles";
import {
  condicionEsValida,
  type ColumnaFiltro,
  type CondicionFiltro,
} from "@/lib/tablas/filtrar-filas";
import type { TablaFiltrosEstado } from "@/lib/tablas/useTablaFiltros";
import { SEARCH_TEXT_MAX_LENGTH, sanitizeNoAngleBrackets } from "@/lib/validation/fieldRules";

const BTN_FOCO =
  "focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2";
const BTN_PRIMARIO = `min-h-9 rounded-xl px-3 py-2 text-xs font-semibold text-white transition hover:opacity-95 ${BTN_FOCO}`;
const ETIQUETA = "text-xs font-semibold uppercase tracking-wide text-[#59677D]";

function describir(c: CondicionFiltro, col: ColumnaFiltro): string {
  if (c.tipo === "texto") return `${col.label}: «${c.texto}»`;
  if (c.tipo === "seleccion") {
    const nombres = c.valores.map((v) => col.opciones?.find((o) => o.value === v)?.label ?? v);
    return `${col.label}: ${nombres.join(", ")}`;
  }
  if (c.desde && c.hasta) return `${col.label}: ${c.desde} a ${c.hasta}`;
  return c.desde ? `${col.label}: desde ${c.desde}` : `${col.label}: hasta ${c.hasta}`;
}

interface EditorProps {
  columna: ColumnaFiltro;
  inicial?: CondicionFiltro;
  onAplicar: (c: CondicionFiltro | null) => void;
}

/** Editor de UNA condición. «Aplicar» con valor vacío no agrega nada (quita la existente, si la hay). */
function EditorCondicion({ columna, inicial, onAplicar }: EditorProps) {
  const uid = useId();
  const [texto, setTexto] = useState(inicial?.tipo === "texto" ? inicial.texto : "");
  const [valores, setValores] = useState<string[]>(
    inicial?.tipo === "seleccion" ? inicial.valores : [],
  );
  const [desde, setDesde] = useState(inicial?.tipo === "fecha" ? inicial.desde : "");
  const [hasta, setHasta] = useState(inicial?.tipo === "fecha" ? inicial.hasta : "");

  const aplicar = () => {
    let c: CondicionFiltro;
    if (columna.tipo === "texto") c = { columnaId: columna.id, tipo: "texto", texto: texto.trim() };
    else if (columna.tipo === "seleccion")
      c = { columnaId: columna.id, tipo: "seleccion", valores };
    else c = { columnaId: columna.id, tipo: "fecha", desde, hasta };
    onAplicar(condicionEsValida(c) ? c : null);
  };

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        aplicar();
      }}
      className="flex flex-col gap-3"
    >
      {columna.tipo === "texto" ? (
        <div className="flex flex-col gap-1">
          <label htmlFor={`${uid}-texto`} className={ETIQUETA}>
            {columna.label} contiene
          </label>
          <input
            id={`${uid}-texto`}
            type="text"
            value={texto}
            maxLength={SEARCH_TEXT_MAX_LENGTH}
            onChange={(e) =>
              setTexto(sanitizeNoAngleBrackets(e.target.value).slice(0, SEARCH_TEXT_MAX_LENGTH))
            }
            className={INPUT_CLS}
            autoFocus
          />
        </div>
      ) : null}

      {columna.tipo === "seleccion" ? (
        <fieldset className="flex flex-col gap-1">
          <legend className={`${ETIQUETA} mb-1`}>{columna.label}</legend>
          <div className="flex max-h-56 flex-col overflow-y-auto">
            {(columna.opciones ?? []).map((o) => (
              <label
                key={o.value}
                className="flex min-h-9 cursor-pointer items-center gap-2 rounded-lg px-2 text-xs text-flit-primary hover:bg-[#EFF6FF] dark:text-white dark:hover:bg-white/5"
              >
                <input
                  type="checkbox"
                  checked={valores.includes(o.value)}
                  onChange={(e) =>
                    setValores((prev) =>
                      e.target.checked ? [...prev, o.value] : prev.filter((v) => v !== o.value),
                    )
                  }
                  className="h-4 w-4 accent-[#557EFF]"
                />
                {o.label}
              </label>
            ))}
          </div>
        </fieldset>
      ) : null}

      {columna.tipo === "fecha" ? (
        <>
          <div className="flex flex-col gap-1">
            <label htmlFor={`${uid}-desde`} className={ETIQUETA}>
              {columna.label} desde
            </label>
            <input
              id={`${uid}-desde`}
              type="date"
              value={desde}
              onChange={(e) => setDesde(e.target.value)}
              className={INPUT_CLS}
            />
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor={`${uid}-hasta`} className={ETIQUETA}>
              {columna.label} hasta
            </label>
            <input
              id={`${uid}-hasta`}
              type="date"
              value={hasta}
              onChange={(e) => setHasta(e.target.value)}
              className={INPUT_CLS}
            />
          </div>
        </>
      ) : null}

      <div className="flex justify-end border-t border-[#DFE5ED] pt-3 dark:border-white/10">
        <button type="submit" className={BTN_PRIMARIO} style={{ background: WIZARD_CTA_GRADIENT }}>
          Aplicar
        </button>
      </div>
    </form>
  );
}

function FiltroPopover({ filtros }: { filtros: TablaFiltrosEstado }) {
  const { columnas, periodoColumnaId, borrador, count } = filtros;
  const [open, setOpen] = useState(false);
  const [editando, setEditando] = useState<string | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const close = () => {
    setOpen(false);
    setEditando(null);
  };
  const panelRef = usePopoverDismiss(open, close, triggerRef);
  const panelId = useId();

  // La columna que usa Periodo se filtra desde «Periodo», no desde aquí.
  const filtrables = columnas.filter((c) => c.id !== periodoColumnaId);
  const columnaEditada = filtrables.find((c) => c.id === editando);
  const enBorrador = borrador.condiciones;

  const quitar = (id: string) =>
    filtros.setCondicionesBorrador(enBorrador.filter((c) => c.columnaId !== id));

  const aplicarCondicion = (columnaId: string, nueva: CondicionFiltro | null) => {
    const resto = enBorrador.filter((c) => c.columnaId !== columnaId);
    filtros.aplicar(nueva ? [...resto, nueva] : resto);
    close();
  };

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => (open ? close() : setOpen(true))}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        className={controlCls(count > 0)}
      >
        {count > 0 ? `+ Filtro (${count})` : "+ Filtro"}
        <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label="Filtros de la tabla"
          className={`absolute right-0 top-full z-50 mt-2 w-[22rem] max-w-[calc(100vw-2rem)] p-3 ${POPOVER_SURFACE_CLS}`}
        >
          {columnaEditada ? (
            <>
              <button
                type="button"
                onClick={() => setEditando(null)}
                className={`mb-2 inline-flex min-h-9 items-center gap-1 text-xs font-semibold text-flit-brand hover:underline ${BTN_FOCO}`}
              >
                ← Volver
              </button>
              <EditorCondicion
                key={columnaEditada.id}
                columna={columnaEditada}
                inicial={enBorrador.find((c) => c.columnaId === columnaEditada.id)}
                onAplicar={(c) => aplicarCondicion(columnaEditada.id, c)}
              />
            </>
          ) : (
            <>
              {enBorrador.length > 0 ? (
                <ul
                  aria-label="Filtros agregados"
                  className="mb-2 flex flex-wrap gap-1.5 border-b border-[#DFE5ED] pb-2 dark:border-white/10"
                >
                  {enBorrador.map((c) => {
                    const col = columnas.find((x) => x.id === c.columnaId);
                    if (!col) return null;
                    return (
                      <li
                        key={c.columnaId}
                        className="inline-flex items-center gap-1 rounded-full border border-flit-brand/40 bg-flit-brand/10 py-1 pl-3 pr-1 text-xs font-semibold text-[#3355CC] dark:text-[#9DB5FF]"
                      >
                        <button
                          type="button"
                          onClick={() => setEditando(c.columnaId)}
                          className={`max-w-[14rem] truncate ${BTN_FOCO}`}
                        >
                          {describir(c, col)}
                        </button>
                        <button
                          type="button"
                          onClick={() => quitar(c.columnaId)}
                          aria-label={`Quitar filtro ${col.label}`}
                          className={`min-h-6 rounded-full px-1.5 text-sm leading-none opacity-60 hover:opacity-100 ${BTN_FOCO}`}
                        >
                          ×
                        </button>
                      </li>
                    );
                  })}
                </ul>
              ) : null}

              <ul aria-label="Columnas filtrables" className="max-h-[22rem] overflow-y-auto">
                {filtrables
                  .filter((c) => !enBorrador.some((x) => x.columnaId === c.id))
                  .map((c) => (
                    <li key={c.id}>
                      <button
                        type="button"
                        onClick={() => setEditando(c.id)}
                        className={`flex min-h-9 w-full items-center rounded-lg px-2 text-left text-xs font-semibold text-flit-primary hover:bg-[#EFF6FF] dark:text-white dark:hover:bg-white/5 ${BTN_FOCO}`}
                      >
                        {c.label}
                      </button>
                    </li>
                  ))}
              </ul>

              <div className="mt-2 flex items-center justify-end gap-2 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
                <button
                  type="button"
                  onClick={() => {
                    filtros.limpiar();
                    close();
                  }}
                  className={`min-h-9 rounded-xl border border-[#FF4E00] px-3 py-2 text-xs font-semibold text-[#C2410C] transition hover:bg-[#FF4E00]/10 ${BTN_FOCO}`}
                >
                  Empezar de cero
                </button>
                <button
                  type="button"
                  onClick={() => {
                    filtros.aplicar();
                    close();
                  }}
                  className={BTN_PRIMARIO}
                  style={{ background: WIZARD_CTA_GRADIENT }}
                >
                  Aplicar
                </button>
              </div>
            </>
          )}
        </div>
      ) : null}
    </div>
  );
}

export interface TablaFiltrosBarProps {
  filtros: TablaFiltrosEstado;
  /** Omitirlo = sin buscador. */
  buscador?: { placeholder: string; ariaLabel?: string };
  /** `ColumnSelector` ya montado por el contenedor. */
  columnSelector?: ReactNode;
  /** Acciones extra a la derecha (p. ej. un toggle existente). */
  acciones?: ReactNode;
}

export function TablaFiltrosBar({
  filtros,
  buscador,
  columnSelector,
  acciones,
}: TablaFiltrosBarProps) {
  const periodoOk =
    !!filtros.periodoColumnaId &&
    filtros.columnas.some((c) => c.id === filtros.periodoColumnaId && c.tipo === "fecha");
  const { borrador } = filtros;

  return (
    <div className="flex flex-wrap items-center gap-2">
      {buscador ? (
        <div className="relative w-64 shrink-0">
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-flit-brand"
            aria-hidden="true"
          />
          <input
            type="search"
            aria-label={buscador.ariaLabel ?? buscador.placeholder}
            value={filtros.busqueda}
            maxLength={SEARCH_TEXT_MAX_LENGTH}
            onChange={(e) => filtros.setBusqueda(e.target.value)}
            placeholder={buscador.placeholder}
            className={`${INPUT_CLS} w-full pl-9`}
          />
        </div>
      ) : null}

      {periodoOk ? (
        <PeriodoPopover
          periodo={borrador.periodo}
          onPeriodoChange={filtros.setPeriodo}
          rangoPropioDesde={borrador.rangoDesde}
          rangoPropioHasta={borrador.rangoHasta}
          onRangoPropioDesdeChange={filtros.setRangoDesde}
          onRangoPropioHastaChange={filtros.setRangoHasta}
          onAplicar={() => filtros.aplicar()}
        />
      ) : null}

      <FiltroPopover filtros={filtros} />

      {columnSelector}
      {acciones}
    </div>
  );
}

/** Estado vacío cuando los filtros no dejan ningún registro. «Limpiar filtros» también limpia el buscador. */
export function TablaFiltrosVacio({
  onLimpiar,
  mensaje = "Ningún registro coincide con los filtros",
}: {
  onLimpiar: () => void;
  mensaje?: string;
}) {
  return (
    <div role="status" className="flex flex-col items-center gap-3 px-4 py-10 text-center">
      <SearchX className="h-8 w-8 text-[#59677D]" aria-hidden="true" />
      <p className="text-sm font-semibold text-flit-primary dark:text-white">{mensaje}</p>
      <button
        type="button"
        onClick={onLimpiar}
        className={`min-h-9 rounded-xl border border-flit-brand px-3 py-2 text-xs font-semibold text-[#3B4FD6] transition hover:bg-[#EFF6FF] ${BTN_FOCO}`}
      >
        Limpiar filtros
      </button>
    </div>
  );
}
