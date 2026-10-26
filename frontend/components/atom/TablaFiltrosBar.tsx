"use client";

/**
 * Filtros genéricos por columna, estilo Excel (HU #13997, Feature #13996, Épica #13514).
 *
 * ESTÁNDAR: cada columna filtrable lleva `<EncabezadoFiltrable>` en su `<th>` / `header` de DataTable
 * (embudo + desplegable por columna), y arriba de la tabla van los filtros de consulta PROPIOS de cada
 * tabla (buscador, selects, panel de filtros). Ambos se combinan con AND.
 * `TablaFiltrosBar` (barra con buscador · Periodo · «+ Filtro») queda SOLO por compatibilidad con los
 * consumidores aún no migrados: no se amplía y las tablas nuevas no la usan. Comparte el mismo estado
 * que los encabezados. Cada tabla solo DECLARA sus columnas; no escribe código propio de filtros.
 *
 * API
 * ---
 *   const COLUMNAS: ColumnaFiltro[] = [
 *     { id: 'nombre', label: 'Nombre', tipo: 'texto' },
 *     { id: 'estado', label: 'Estado', tipo: 'seleccion',
 *       opciones: [{ value: 'ACTIVE', label: 'Activa' }, { value: 'INACTIVE', label: 'Inactiva' }] },
 *     { id: 'creado', label: 'Creación', tipo: 'fecha' },   // Periodo + Rango propio, validado aquí
 *   ];
 *   const filtros = useTablaFiltros({ columnas: COLUMNAS, onCambio });
 *
 *   - `useTablaFiltros({ columnas, onCambio? })` -> `busqueda`/`setBusqueda`, `condicionesTotales`
 *     (para `filtrarFilas` / `condicionesAParams`), `count`, `hayFiltros`, `condicionDe(id)`,
 *     `aplicarColumna(id, condicion|null)`, `quitarColumna(id)`, `limpiar()`. `onCambio` se dispara en
 *     todo cambio aplicado: úsalo para volver a la página 1. `periodoColumnaId` (opcional) solo
 *     indica la columna `fecha` del botón «Periodo» de la barra.
 *   - El estado es UNO SOLO: lo aplicado desde un encabezado se ve en «+ Filtro»/«Periodo» y viceversa.
 *   - `<EncabezadoFiltrable filtros columnaId>Título</EncabezadoFiltrable>`: título + embudo. Si
 *     `columnaId` no está en `columnas`, pinta solo el título. Con filtro aplicado el embudo se pinta
 *     azul y anuncia «<Col>: filtro activo». Panel en portal; Escape / clic fuera lo cierran.
 *   - `<TablaFiltrosBar filtros buscador? columnSelector? acciones? />`: buscador, «Periodo» (si el hook
 *     recibe `periodoColumnaId`), «+ Filtro (n)» / «Empezar de cero» y los slots.
 *   - Texto saneado con `sanitizeNoAngleBrackets` y `SEARCH_TEXT_MAX_LENGTH`; nunca se aplican
 *     condiciones vacías (equivalen a quitar el filtro).
 *
 * Ejemplo con `<th>` manual
 *   <th className={TABLA_HEADER_CELL_CLS}>
 *     <EncabezadoFiltrable filtros={filtros} columnaId="nombre">Nombre</EncabezadoFiltrable>
 *   </th>
 *   <th className={TABLA_HEADER_CELL_CLS}>Acciones</th>   // sin embudo: no es filtrable
 *
 * Ejemplo con `DataTable` (`header` es un ReactNode)
 *   const columns = [{ key: 'estado', ...,
 *     header: <EncabezadoFiltrable filtros={filtros} columnaId="estado">Estado</EncabezadoFiltrable> }];
 *
 * Ejemplo CLIENTE (la tabla tiene todas las filas en memoria)
 *   const { setPagina } = usePaginacion();
 *   const filtros = useTablaFiltros({ columnas: COLUMNAS, onCambio: () => setPagina(1) });
 *   const visibles = useMemo(() => filtrarFilas(filas, filtros.condicionesTotales, filtros.busqueda, {
 *     columnas: { nombre: (f) => f.name, estado: (f) => f.status, creado: (f) => f.createdAt },
 *     busqueda: (f) => [f.name, f.nit],
 *   }), [filas, filtros.condicionesTotales, filtros.busqueda]);
 *   <TablaFiltrosBar filtros={filtros} buscador={{ placeholder: 'Buscar por nombre o NIT...' }} />
 *   {visibles.length === 0 && filtros.hayFiltros ? <TablaFiltrosVacio onLimpiar={filtros.limpiar} /> : ...}
 *
 * Ejemplo SERVIDOR (la API filtra)
 *   const params = useMemo(() => condicionesAParams(filtros.condicionesTotales, COLUMNAS,
 *                    filtros.busqueda, { busquedaParam: 'search' }), [filtros.condicionesTotales, filtros.busqueda]);
 *   // -> { search: 'abc', status: 'ACTIVE,INACTIVE', createdFrom: '2026-01-01', createdTo: '2026-01-31' }
 *   // Mapeo por columna: `param`, `paramDesde`, `paramHasta`. Re-consulta cuando `params` cambia.
 *
 * Ejemplo CLIENTE combinado con un filtro de arriba (p. ej. un buscador local propio de la tabla)
 *   const [q, setQ] = useState("");                       // buscador de arriba, propio de la tabla
 *   const filtros = useTablaFiltros({ columnas: COLUMNAS, onCambio: () => setPagina(1) });
 *   const visibles = useMemo(() => {
 *     const porColumna = filtrarFilas(filas, filtros.condicionesTotales, "", { columnas: ACCESORES });
 *     return porColumna.filter((f) => matchBuscadorPropio(f, q));      // AND con el de arriba
 *   }, [filas, filtros.condicionesTotales, q]);
 *   // Si prefieres el buscador del hook: <input value={filtros.busqueda} onChange={...setBusqueda} />
 *
 * Ejemplo SERVIDOR combinado con los params de un panel de arriba
 *   const paramsColumna = condicionesAParams(filtros.condicionesTotales, COLUMNAS, "");
 *   const params = { ...paramsPanelDeArriba, ...paramsColumna };    // MISMO param: gana el de la columna
 *   // Re-consulta cuando cambian; `onCambio` del hook y el panel de arriba vuelven a la página 1.
 *   // Nota: `limpiar()` quita solo lo del hook; el panel de arriba tiene su propio «Limpiar».
 */

import { useId, useRef, useState, type ReactNode } from "react";
import { ChevronDown, Search, SearchX } from "lucide-react";
import {
  INPUT_CLS,
  PeriodoPopover,
  POPOVER_SURFACE_CLS,
  rangoDePeriodo,
  usePopoverDismiss,
} from "@/components/operacion/TramitesFiltrosBar";
import { controlCls } from "@/components/operacion/tramites-control-styles";
import { EditorColumna, MSG_RANGO_INVERTIDO, periodoDe } from "@/components/atom/FiltroColumna";
import type { ColumnaFiltro, CondicionFiltro } from "@/lib/tablas/filtrar-filas";
import type { TablaFiltrosEstado } from "@/lib/tablas/useTablaFiltros";
import { SEARCH_TEXT_MAX_LENGTH } from "@/lib/validation/fieldRules";

export {
  EncabezadoFiltrable,
  type EncabezadoFiltrableProps,
} from "@/components/atom/FiltroColumna";

const BTN_FOCO =
  "focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2";

function describir(c: CondicionFiltro, col: ColumnaFiltro): string {
  if (c.tipo === "texto") return `${col.label}: «${c.texto}»`;
  if (c.tipo === "seleccion") {
    const nombres = c.valores.map((v) => col.opciones?.find((o) => o.value === v)?.label ?? v);
    return `${col.label}: ${nombres.join(", ")}`;
  }
  if (c.desde && c.hasta) return `${col.label}: ${c.desde} a ${c.hasta}`;
  return c.desde ? `${col.label}: desde ${c.desde}` : `${col.label}: hasta ${c.hasta}`;
}

/** «Periodo» de la barra: edita la misma condición `fecha` que el encabezado de esa columna. */
function PeriodoBarra({ filtros, columnaId }: { filtros: TablaFiltrosEstado; columnaId: string }) {
  const condicion = filtros.condicionDe(columnaId);
  const [periodo, setPeriodo] = useState(() => periodoDe(condicion));
  const [desde, setDesde] = useState(condicion?.tipo === "fecha" ? condicion.desde : "");
  const [hasta, setHasta] = useState(condicion?.tipo === "fecha" ? condicion.hasta : "");

  // Si el encabezado (o «Empezar de cero») cambia la condición, el borrador local se resincroniza.
  const [vista, setVista] = useState(condicion);
  if (vista !== condicion) {
    setVista(condicion);
    setPeriodo(periodoDe(condicion));
    setDesde(condicion?.tipo === "fecha" ? condicion.desde : "");
    setHasta(condicion?.tipo === "fecha" ? condicion.hasta : "");
  }

  const [error, setError] = useState<string | null>(null);

  const aplicar = () => {
    setError(null);
    let d = desde;
    let h = hasta;
    if (periodo !== "Rango propio") {
      const r = rangoDePeriodo(periodo, new Date());
      d = r?.desde ?? "";
      h = r?.hasta ?? "";
    } else if (d && h && d > h) {
      setError(MSG_RANGO_INVERTIDO); // rango invertido: no se aplica
      return;
    }
    filtros.aplicarColumna(columnaId, { columnaId, tipo: "fecha", desde: d, hasta: h });
  };

  return (
    <>
      <PeriodoPopover
        periodo={periodo}
        onPeriodoChange={(v) => {
          setPeriodo(v);
          if (v === "Sin periodo") {
            setDesde("");
            setHasta("");
            setError(null);
            filtros.quitarColumna(columnaId);
          }
        }}
        rangoPropioDesde={desde}
        rangoPropioHasta={hasta}
        onRangoPropioDesdeChange={setDesde}
        onRangoPropioHastaChange={setHasta}
        onAplicar={aplicar}
      />
      {error ? (
        <p role="alert" className="text-xs font-semibold text-[#C2410C]">
          {error}
        </p>
      ) : null}
    </>
  );
}

/** «+ Filtro» de la barra: opera sobre las mismas condiciones que los encabezados. */
function FiltroPopover({
  filtros,
  excluirId,
}: {
  filtros: TablaFiltrosEstado;
  excluirId?: string;
}) {
  const { columnas, condiciones } = filtros;
  const [open, setOpen] = useState(false);
  const [editando, setEditando] = useState<string | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const close = () => {
    setOpen(false);
    setEditando(null);
  };
  const panelRef = usePopoverDismiss(open, close, triggerRef);
  const panelId = useId();

  const filtrables = columnas.filter((c) => c.id !== excluirId);
  const columnaEditada = filtrables.find((c) => c.id === editando);
  const visibles = condiciones.filter((c) => c.columnaId !== excluirId);

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => (open ? close() : setOpen(true))}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        className={controlCls(visibles.length > 0)}
      >
        {visibles.length > 0 ? `+ Filtro (${visibles.length})` : "+ Filtro"}
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
              <EditorColumna
                key={columnaEditada.id}
                columna={columnaEditada}
                inicial={condiciones.find((c) => c.columnaId === columnaEditada.id)}
                onAplicar={(c) => {
                  filtros.aplicarColumna(columnaEditada.id, c);
                  close();
                }}
                onQuitar={() => {
                  filtros.quitarColumna(columnaEditada.id);
                  close();
                }}
              />
            </>
          ) : (
            <>
              {visibles.length > 0 ? (
                <ul
                  aria-label="Filtros agregados"
                  className="mb-2 flex flex-wrap gap-1.5 border-b border-[#DFE5ED] pb-2 dark:border-white/10"
                >
                  {visibles.map((c) => {
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
                          onClick={() => filtros.quitarColumna(c.columnaId)}
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
                  .filter((c) => !visibles.some((x) => x.columnaId === c.id))
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
              <div className="mt-2 flex items-center justify-end border-t border-[#DFE5ED] pt-3 dark:border-white/10">
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
  const periodoId =
    filtros.periodoColumnaId &&
    filtros.columnas.some((c) => c.id === filtros.periodoColumnaId && c.tipo === "fecha")
      ? filtros.periodoColumnaId
      : undefined;

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

      {periodoId ? <PeriodoBarra filtros={filtros} columnaId={periodoId} /> : null}
      <FiltroPopover filtros={filtros} excluirId={periodoId} />

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
