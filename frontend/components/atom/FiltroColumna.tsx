"use client";

/**
 * Filtro por columna estilo Excel (HU #13997, Feature #13996, Épica #13514).
 *
 * `EncabezadoFiltrable` se usa DENTRO del `<th>` (o como `header` de `DataTable`): pinta el título y,
 * a su lado, un embudo que despliega el editor de ESA columna (texto, selección o fecha/Periodo).
 * El panel va en un portal con posición fija anclada al embudo, porque la cabecera es sticky y los
 * contenedores de tabla recortan con `overflow`.
 */

import {
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import { Filter } from "lucide-react";
import {
  INPUT_CLS,
  PERIODOS,
  POPOVER_SURFACE_CLS,
  rangoDePeriodo,
} from "@/components/operacion/TramitesFiltrosBar";
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
// Normaliza lo que el `<th>` impone (versalita, 10px, negrita) dentro del panel.
const PANEL_TEXTO = "text-left text-xs font-normal normal-case tracking-normal leading-normal";
const ETIQUETA = "text-xs font-semibold normal-case tracking-normal text-[#59677D]";
const BTN_PRIMARIO = `min-h-9 rounded-xl px-3 py-2 text-xs font-semibold normal-case tracking-normal text-white transition hover:opacity-95 ${BTN_FOCO}`;
const BTN_SECUNDARIO = `min-h-9 rounded-xl border border-[#DFE5ED] px-3 py-2 text-xs font-semibold normal-case tracking-normal text-[#1E293B] transition hover:bg-[#EFF6FF] disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/15 dark:text-white ${BTN_FOCO}`;
export const MSG_RANGO_INVERTIDO = "La fecha inicial no puede ser posterior a la fecha final.";
const PANEL_ANCHO = 288;
const SEP = 6;

/** Periodo que corresponde a una condición de fecha ya aplicada (para reabrir el panel donde quedó). */
export function periodoDe(c: CondicionFiltro | undefined): string {
  if (!c || c.tipo !== "fecha") return "Sin periodo";
  const hoy = new Date();
  for (const p of PERIODOS) {
    if (p === "Sin periodo" || p === "Rango propio") continue;
    const r = rangoDePeriodo(p, hoy);
    if (r && r.desde === c.desde && r.hasta === c.hasta) return p;
  }
  return "Rango propio";
}

export interface EditorProps {
  columna: ColumnaFiltro;
  inicial?: CondicionFiltro;
  onAplicar: (c: CondicionFiltro | null) => void;
  onQuitar: () => void;
}

export function EditorColumna({ columna, inicial, onAplicar, onQuitar }: EditorProps) {
  const uid = useId();
  const [texto, setTexto] = useState(inicial?.tipo === "texto" ? inicial.texto : "");
  const [valores, setValores] = useState<string[]>(
    inicial?.tipo === "seleccion" ? inicial.valores : [],
  );
  const [periodo, setPeriodo] = useState(() => periodoDe(inicial));
  const [desde, setDesde] = useState(inicial?.tipo === "fecha" ? inicial.desde : "");
  const [hasta, setHasta] = useState(inicial?.tipo === "fecha" ? inicial.hasta : "");
  const [error, setError] = useState<string | null>(null);

  const aplicar = () => {
    setError(null);
    let c: CondicionFiltro;
    if (columna.tipo === "texto") {
      c = { columnaId: columna.id, tipo: "texto", texto: texto.trim() };
    } else if (columna.tipo === "seleccion") {
      c = { columnaId: columna.id, tipo: "seleccion", valores };
    } else if (periodo === "Rango propio") {
      if (desde && hasta && desde > hasta) {
        setError(MSG_RANGO_INVERTIDO);
        return;
      }
      c = { columnaId: columna.id, tipo: "fecha", desde, hasta };
    } else {
      const r = rangoDePeriodo(periodo, new Date());
      c = { columnaId: columna.id, tipo: "fecha", desde: r?.desde ?? "", hasta: r?.hasta ?? "" };
    }
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
          <fieldset className="flex flex-col gap-0.5">
            <legend className={`${ETIQUETA} mb-1`}>Periodo de {columna.label}</legend>
            {PERIODOS.map((p) => (
              <label
                key={p}
                className="flex min-h-8 cursor-pointer items-center gap-2 rounded-lg px-2 text-xs text-flit-primary hover:bg-[#EFF6FF] dark:text-white dark:hover:bg-white/5"
              >
                <input
                  type="radio"
                  name={`${uid}-periodo`}
                  checked={periodo === p}
                  onChange={() => {
                    setPeriodo(p);
                    setError(null);
                  }}
                  className="h-4 w-4 accent-[#557EFF]"
                />
                {p}
              </label>
            ))}
          </fieldset>
          {periodo === "Rango propio" ? (
            <div className="flex flex-col gap-2">
              <div className="flex flex-col gap-1">
                <label htmlFor={`${uid}-desde`} className={ETIQUETA}>
                  Desde
                </label>
                <input
                  id={`${uid}-desde`}
                  type="date"
                  value={desde}
                  onChange={(e) => setDesde(e.target.value)}
                  aria-invalid={error ? true : undefined}
                  className={INPUT_CLS}
                />
              </div>
              <div className="flex flex-col gap-1">
                <label htmlFor={`${uid}-hasta`} className={ETIQUETA}>
                  Hasta
                </label>
                <input
                  id={`${uid}-hasta`}
                  type="date"
                  value={hasta}
                  onChange={(e) => setHasta(e.target.value)}
                  aria-invalid={error ? true : undefined}
                  className={INPUT_CLS}
                />
              </div>
            </div>
          ) : null}
          {error ? (
            <p role="alert" className="text-xs font-semibold text-[#B91C1C]">
              {error}
            </p>
          ) : null}
        </>
      ) : null}

      <div className="flex items-center justify-end gap-2 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
        <button type="button" onClick={onQuitar} disabled={!inicial} className={BTN_SECUNDARIO}>
          Quitar filtro
        </button>
        <button type="submit" className={BTN_PRIMARIO} style={{ background: WIZARD_CTA_GRADIENT }}>
          Aplicar
        </button>
      </div>
    </form>
  );
}

export interface EncabezadoFiltrableProps {
  filtros: TablaFiltrosEstado;
  /** `id` de la `ColumnaFiltro` declarada en `useTablaFiltros`. Si no es filtrable, solo se pinta el título. */
  columnaId: string;
  /** Título de la columna. */
  children: ReactNode;
  className?: string;
}

export function EncabezadoFiltrable({
  filtros,
  columnaId,
  children,
  className = "",
}: EncabezadoFiltrableProps) {
  const columna = filtros.columnas.find((c) => c.id === columnaId);
  if (!columna) return <span className={className}>{children}</span>;
  return (
    <span className={`inline-flex items-center gap-0.5 ${className}`}>
      <span>{children}</span>
      <BotonFiltro filtros={filtros} columna={columna} />
    </span>
  );
}

function BotonFiltro({
  filtros,
  columna,
}: {
  filtros: TablaFiltrosEstado;
  columna: ColumnaFiltro;
}) {
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const condicion = filtros.condicionDe(columna.id);
  const activo = !!condicion;

  const cerrar = useCallback((devolverFoco: boolean) => {
    setOpen(false);
    if (devolverFoco) triggerRef.current?.focus();
  }, []);

  const ubicar = useCallback(() => {
    const b = triggerRef.current?.getBoundingClientRect();
    if (!b) return;
    const ancho = Math.min(PANEL_ANCHO, window.innerWidth - 16);
    let left = b.left;
    if (left + ancho > window.innerWidth - 8) left = Math.max(8, b.right - ancho);
    setPos({ top: b.bottom + SEP, left });
  }, []);

  useLayoutEffect(() => {
    if (open) ubicar();
  }, [open, ubicar]);

  useEffect(() => {
    if (!open) return;
    panelRef.current?.querySelector<HTMLElement>("input, button")?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") cerrar(true);
    };
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node;
      if (panelRef.current?.contains(t) || triggerRef.current?.contains(t)) return;
      setOpen(false);
      // El foco vuelve al embudo salvo que el clic haya caído en otro control.
      setTimeout(() => {
        const a = document.activeElement;
        if (!a || a === document.body) triggerRef.current?.focus();
      }, 0);
    };
    document.addEventListener("keydown", onKey);
    document.addEventListener("mousedown", onDown);
    window.addEventListener("resize", ubicar);
    window.addEventListener("scroll", ubicar, true);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("mousedown", onDown);
      window.removeEventListener("resize", ubicar);
      window.removeEventListener("scroll", ubicar, true);
    };
  }, [open, cerrar, ubicar]);

  const nombre = activo
    ? `Filtrar por ${columna.label} (${columna.label}: filtro activo)`
    : `Filtrar por ${columna.label}`;
  const ancho =
    typeof window === "undefined" ? PANEL_ANCHO : Math.min(PANEL_ANCHO, window.innerWidth - 16);

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        onClick={(e) => {
          e.stopPropagation(); // el `<th>` puede ordenar al hacer clic
          setOpen((v) => !v);
        }}
        aria-label={nombre}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        data-activo={activo ? "true" : undefined}
        className={`-my-2 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg transition hover:bg-white/60 ${BTN_FOCO} ${
          activo ? "text-[#3B4FD6]" : "text-[#59677D]"
        }`}
      >
        <Filter className={`h-3.5 w-3.5 ${activo ? "fill-current" : ""}`} aria-hidden="true" />
      </button>
      {open && typeof document !== "undefined"
        ? createPortal(
            <div
              ref={panelRef}
              id={panelId}
              role="dialog"
              aria-label={`Filtro de ${columna.label}`}
              onClick={(e) => e.stopPropagation()}
              style={{
                position: "fixed",
                top: pos?.top ?? 0,
                left: pos?.left ?? 0,
                width: ancho,
                visibility: pos ? "visible" : "hidden",
              }}
              className={`z-[60] max-h-[calc(100vh-1rem)] overflow-y-auto p-3 ${PANEL_TEXTO} ${POPOVER_SURFACE_CLS}`}
            >
              <EditorColumna
                columna={columna}
                inicial={condicion}
                onAplicar={(c) => {
                  filtros.aplicarColumna(columna.id, c);
                  cerrar(true);
                }}
                onQuitar={() => {
                  filtros.quitarColumna(columna.id);
                  cerrar(true);
                }}
              />
            </div>,
            document.body,
          )
        : null}
    </>
  );
}
