'use client';

import { useEffect, useId, useRef, useState, type ReactNode, type RefObject } from 'react';
import { CalendarDays, Search, X } from 'lucide-react';

/**
 * Barra de filtros del listado de trámites (Track A). Reemplaza al panel colapsable anterior:
 * es una tarjeta blanca SIEMPRE VISIBLE, composición tomada de `Reportes.tsx` (repo hermano,
 * `origin/main`), con los VALORES (colores, radios, tipografía, spacing) tomados de
 * `frontend/app/globals.css` / tokens FLIT — no del prototipo, que usa valores fuera de norma
 * (texto <12px, opacidades <0.7, `slate-*`/`#8A94A6`, superficie dark `#0B0F14`).
 *
 * Puramente presentacional: todo el estado (draft/aplicado) vive en `TramitesTable`, que decide
 * cuándo convertir "Periodo" en `createdFrom/createdTo` o `updatedFrom/updatedTo` (ver
 * `rangoDePeriodo` más abajo) y qué filtros específicos router a las llamadas del API.
 */

export type RangoSobre = 'created' | 'updated';

/** Opción inicial "Sin periodo" primero: no se impone un filtro de fechas que hoy no existe. */
export const PERIODOS = [
  'Sin periodo',
  'Hoy',
  'Últimos 7 días',
  'Últimos 30 días',
  'Últimos 90 días',
  'Mes actual',
  'Mes anterior',
  'Rango propio',
] as const;
export type Periodo = (typeof PERIODOS)[number];

/** Los 5 filtros específicos que YA existen hoy en el `<form>` legado: ninguno desaparece. */
export type FiltroEspecificoKey = 'placa' | 'vendedor' | 'comprador' | 'gestor' | 'firmado';

const FILTROS_ESPECIFICOS_GRUPOS: {
  grupo: string;
  items: { key: FiltroEspecificoKey; label: string }[];
}[] = [
  { grupo: 'VEHÍCULO', items: [{ key: 'placa', label: 'Placa' }] },
  {
    grupo: 'PERSONAS',
    items: [
      { key: 'vendedor', label: 'Propietario / vendedor' },
      { key: 'comprador', label: 'Comprador' },
    ],
  },
  {
    grupo: 'TRÁMITE',
    items: [
      { key: 'gestor', label: 'Gestor' },
      { key: 'firmado', label: 'Firmado' },
    ],
  },
];
/** Orden canónico (mismo de los grupos) para pintar la grilla de campos activos siempre igual,
 *  sin importar el orden en que el usuario los fue añadiendo. */
const FILTROS_ESPECIFICOS_ORDEN: FiltroEspecificoKey[] = FILTROS_ESPECIFICOS_GRUPOS.flatMap((g) =>
  g.items.map((i) => i.key),
);

const INPUT_CLS =
  'rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#162744] outline-none transition focus:border-[#557EFF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-white/5 dark:text-white';
const POPOVER_SURFACE_CLS =
  'rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_8px_24px_rgba(22,39,68,0.08)] dark:border-white/10 dark:bg-[#162744]';
const OUTLINE_BUTTON_CLS =
  'rounded-xl border border-[#557EFF] px-4 py-2.5 text-xs font-semibold text-[#557EFF] transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-40';

/**
 * Calcula el rango de fechas (local, `yyyy-mm-dd`) que corresponde a un periodo predefinido.
 * Aritmética de fecha LOCAL (no `toISOString()`, que desplaza por zona horaria). `Rango propio` y
 * `Sin periodo` devuelven `null`: el primero lo llena el usuario a mano, el segundo no filtra.
 */
export function rangoDePeriodo(periodo: string, hoy: Date): { desde: string; hasta: string } | null {
  const fmt = (d: Date) => {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  };
  const hoyLocal = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate());
  const restarDias = (dias: number) => {
    const copia = new Date(hoyLocal);
    copia.setDate(copia.getDate() - dias);
    return copia;
  };

  switch (periodo) {
    case 'Hoy':
      return { desde: fmt(hoyLocal), hasta: fmt(hoyLocal) };
    case 'Últimos 7 días':
      return { desde: fmt(restarDias(6)), hasta: fmt(hoyLocal) };
    case 'Últimos 30 días':
      return { desde: fmt(restarDias(29)), hasta: fmt(hoyLocal) };
    case 'Últimos 90 días':
      return { desde: fmt(restarDias(89)), hasta: fmt(hoyLocal) };
    case 'Mes actual': {
      const primero = new Date(hoyLocal.getFullYear(), hoyLocal.getMonth(), 1);
      return { desde: fmt(primero), hasta: fmt(hoyLocal) };
    }
    case 'Mes anterior': {
      const primero = new Date(hoyLocal.getFullYear(), hoyLocal.getMonth() - 1, 1);
      const ultimo = new Date(hoyLocal.getFullYear(), hoyLocal.getMonth(), 0);
      return { desde: fmt(primero), hasta: fmt(ultimo) };
    }
    default:
      // 'Rango propio' y 'Sin periodo'.
      return null;
  }
}

/** Rótulo + control apilados, mismo patrón `Field` de la propuesta (label real, no placeholder). */
function Field({
  label,
  children,
  className = '',
}: {
  label: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <label className={`flex flex-col gap-1 ${className}`}>
      <span className="text-xs font-semibold uppercase tracking-wide text-[#59677D]">{label}</span>
      {children}
    </label>
  );
}

/** Cierre por clic fuera Y por Escape, con el foco devuelto al disparador — el `useOutside` de la
 *  propuesta no hacía ninguna de las dos cosas. */
function usePopoverDismiss(
  open: boolean,
  onClose: () => void,
  triggerRef: RefObject<HTMLElement | null>,
) {
  const panelRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
        triggerRef.current?.focus();
      }
    };
    const onPointerDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (panelRef.current?.contains(target) || triggerRef.current?.contains(target)) return;
      onClose();
    };
    document.addEventListener('keydown', onKeyDown);
    document.addEventListener('mousedown', onPointerDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.removeEventListener('mousedown', onPointerDown);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, onClose]);
  return panelRef;
}

function RangoPropioPopover({
  desde,
  hasta,
  onDesdeChange,
  onHastaChange,
}: {
  desde: string;
  hasta: string;
  onDesdeChange: (v: string) => void;
  onHastaChange: (v: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const close = () => setOpen(false);
  const panelRef = usePopoverDismiss(open, close, triggerRef);
  const panelId = useId();

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label="Elegir rango de fechas propio"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        className="grid h-[38px] w-[38px] shrink-0 place-items-center rounded-xl border border-[#557EFF] text-[#557EFF] transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
      >
        <CalendarDays className="h-4 w-4" aria-hidden="true" />
      </button>
      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label="Rango de fechas propio"
          className={`absolute left-0 top-full z-30 mt-2 w-64 p-3 ${POPOVER_SURFACE_CLS}`}
        >
          <Field label="Fecha inicial">
            <input
              type="date"
              value={desde}
              onChange={(e) => onDesdeChange(e.target.value)}
              aria-label="Fecha inicial del rango propio"
              className={INPUT_CLS}
            />
          </Field>
          <div className="h-2" aria-hidden="true" />
          <Field label="Fecha final">
            <input
              type="date"
              value={hasta}
              onChange={(e) => onHastaChange(e.target.value)}
              aria-label="Fecha final del rango propio"
              className={INPUT_CLS}
            />
          </Field>
        </div>
      ) : null}
    </div>
  );
}

function FiltroEspecificoPopover({
  activos,
  onToggle,
}: {
  activos: ReadonlySet<FiltroEspecificoKey>;
  onToggle: (key: FiltroEspecificoKey) => void;
}) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const close = () => setOpen(false);
  const panelRef = usePopoverDismiss(open, close, triggerRef);
  const panelId = useId();

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        className={OUTLINE_BUTTON_CLS}
      >
        + Agregar filtro específico
      </button>
      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label="Agregar filtro específico"
          className={`absolute left-0 top-full z-30 mt-2 w-72 max-h-[330px] overflow-y-auto p-2 ${POPOVER_SURFACE_CLS}`}
        >
          {FILTROS_ESPECIFICOS_GRUPOS.map((grupo, gi) => (
            <div
              key={grupo.grupo}
              className={gi > 0 ? 'mt-2 border-t border-[#DFE5ED] pt-2 dark:border-white/10' : ''}
            >
              <p className="select-none px-2 py-1 text-xs font-bold uppercase tracking-wide text-[#162744] dark:text-white">
                {grupo.grupo}
              </p>
              {grupo.items.map((item) => (
                <label
                  key={item.key}
                  className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-xs font-medium text-[#162744] hover:bg-[#EEF5FF] dark:text-white dark:hover:bg-white/5"
                >
                  <input
                    type="checkbox"
                    checked={activos.has(item.key)}
                    onChange={() => onToggle(item.key)}
                    className="h-3.5 w-3.5 accent-[#557EFF]"
                  />
                  {item.label}
                </label>
              ))}
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}

export interface TramitesFiltrosBarProps {
  rangoSobre: RangoSobre;
  onRangoSobreChange: (v: RangoSobre) => void;
  periodo: string;
  onPeriodoChange: (v: string) => void;
  rangoPropioDesde: string;
  rangoPropioHasta: string;
  onRangoPropioDesdeChange: (v: string) => void;
  onRangoPropioHastaChange: (v: string) => void;

  filtrosEspecificos: ReadonlySet<FiltroEspecificoKey>;
  onToggleFiltroEspecifico: (key: FiltroEspecificoKey) => void;

  placa: string;
  onPlacaChange: (v: string) => void;
  vendedor: string;
  onVendedorChange: (v: string) => void;
  comprador: string;
  onCompradorChange: (v: string) => void;
  gestor: string;
  onGestorChange: (v: string) => void;
  firmado: '' | 'true' | 'false';
  onFirmadoChange: (v: '' | 'true' | 'false') => void;

  search: string;
  onSearchChange: (v: string) => void;

  onAplicar: () => void;
  onEmpezarDeCero: () => void;
  empezarDeCeroDisabled?: boolean;

  /** `ColumnSelector` ya montado por el contenedor — se muda aquí desde la fila de tabs. */
  columnSelector: ReactNode;

  /** #1 — filtro de compañía, SOLO SuperAdmin (ve trámites de todas las empresas). */
  isAdmin: boolean;
  companias: readonly string[];
  compania: string;
  onCompaniaChange: (v: string) => void;
}

const RANGO_SOBRE_OPTIONS: { value: RangoSobre; label: string }[] = [
  { value: 'created', label: 'Fecha de creación' },
  { value: 'updated', label: 'Última actualización' },
];

export function TramitesFiltrosBar({
  rangoSobre,
  onRangoSobreChange,
  periodo,
  onPeriodoChange,
  rangoPropioDesde,
  rangoPropioHasta,
  onRangoPropioDesdeChange,
  onRangoPropioHastaChange,
  filtrosEspecificos,
  onToggleFiltroEspecifico,
  placa,
  onPlacaChange,
  vendedor,
  onVendedorChange,
  comprador,
  onCompradorChange,
  gestor,
  onGestorChange,
  firmado,
  onFirmadoChange,
  search,
  onSearchChange,
  onAplicar,
  onEmpezarDeCero,
  empezarDeCeroDisabled = false,
  columnSelector,
  isAdmin,
  companias,
  compania,
  onCompaniaChange,
}: TramitesFiltrosBarProps) {
  const activosOrdenados = FILTROS_ESPECIFICOS_ORDEN.filter((k) => filtrosEspecificos.has(k));
  const filtroLabel = (key: FiltroEspecificoKey): string =>
    FILTROS_ESPECIFICOS_GRUPOS.flatMap((g) => g.items).find((i) => i.key === key)?.label ?? key;

  return (
    <div className="rounded-2xl border border-[#DFE5ED] bg-white p-5 dark:border-white/10 dark:bg-[#162744]">
      {/* Fila principal. */}
      <div className="flex flex-wrap items-end gap-3">
        {/* #1 — Compañía del SuperAdmin: no está en el orden numerado de la fila principal del
            prototipo (que asume un único tenant); entra primero por ser un alcance más amplio que
            el resto de filtros (decide QUÉ compañía se está mirando antes de refinar por fecha o
            campos específicos). */}
        {isAdmin && companias.length > 0 ? (
          <Field label="Compañía" className="min-w-[170px]">
            <select
              value={compania}
              onChange={(e) => onCompaniaChange(e.target.value)}
              className={INPUT_CLS}
            >
              <option value="">Todas</option>
              {companias.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </Field>
        ) : null}

        <Field label="Rango sobre" className="min-w-[190px]">
          <select
            value={rangoSobre}
            onChange={(e) => onRangoSobreChange(e.target.value as RangoSobre)}
            className={INPUT_CLS}
          >
            {RANGO_SOBRE_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Periodo" className="min-w-[170px]">
          <select
            value={periodo}
            onChange={(e) => onPeriodoChange(e.target.value)}
            className={INPUT_CLS}
          >
            {PERIODOS.map((p) => (
              <option key={p} value={p}>
                {p}
              </option>
            ))}
          </select>
        </Field>

        {periodo === 'Rango propio' ? (
          <RangoPropioPopover
            desde={rangoPropioDesde}
            hasta={rangoPropioHasta}
            onDesdeChange={onRangoPropioDesdeChange}
            onHastaChange={onRangoPropioHastaChange}
          />
        ) : null}

        <FiltroEspecificoPopover activos={filtrosEspecificos} onToggle={onToggleFiltroEspecifico} />

        <Field label="Búsqueda rápida" className="min-w-[240px] flex-1">
          <div className="relative">
            <Search
              className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[#59677D]"
              aria-hidden="true"
            />
            <input
              type="search"
              aria-label="Buscar trámites"
              value={search}
              onChange={(e) => onSearchChange(e.target.value)}
              placeholder="Placa, VIN, referencia, comprador u organismo…"
              className={`${INPUT_CLS} w-full pl-9`}
            />
          </div>
        </Field>

        <button
          type="button"
          onClick={onAplicar}
          className="rounded-xl bg-[#557EFF] px-4 py-2.5 text-xs font-semibold text-white transition hover:opacity-95 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
        >
          Aplicar filtros
        </button>

        <button
          type="button"
          onClick={onEmpezarDeCero}
          disabled={empezarDeCeroDisabled}
          className="rounded-xl border border-[#FF4E00] px-4 py-2.5 text-xs font-semibold text-[#C2410C] transition hover:bg-[#FF4E00]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-40"
        >
          Empezar de cero
        </button>
      </div>

      {/* Zona de filtros específicos activos: un campo real por cada uno que el usuario añadió. */}
      {activosOrdenados.length > 0 ? (
        <div className="mt-3 grid gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
          {activosOrdenados.includes('placa') ? (
            <Field label="Placa">
              <input
                type="search"
                aria-label="Filtrar por placa"
                value={placa}
                onChange={(e) => onPlacaChange(e.target.value)}
                placeholder="ABC123"
                className={INPUT_CLS}
              />
            </Field>
          ) : null}
          {activosOrdenados.includes('vendedor') ? (
            <Field label="Propietario / vendedor">
              <input
                type="search"
                aria-label="Filtrar por propietario o vendedor"
                value={vendedor}
                onChange={(e) => onVendedorChange(e.target.value)}
                placeholder="Nombre"
                className={INPUT_CLS}
              />
            </Field>
          ) : null}
          {activosOrdenados.includes('comprador') ? (
            <Field label="Comprador">
              <input
                type="search"
                aria-label="Filtrar por comprador"
                value={comprador}
                onChange={(e) => onCompradorChange(e.target.value)}
                placeholder="Nombre"
                className={INPUT_CLS}
              />
            </Field>
          ) : null}
          {activosOrdenados.includes('gestor') ? (
            <Field label="Gestor">
              <input
                type="search"
                aria-label="Filtrar por gestor"
                value={gestor}
                onChange={(e) => onGestorChange(e.target.value)}
                placeholder="Nombre"
                className={INPUT_CLS}
              />
            </Field>
          ) : null}
          {activosOrdenados.includes('firmado') ? (
            <Field label="Firmado">
              <select
                aria-label="Filtrar por firma de compraventa"
                value={firmado}
                onChange={(e) => onFirmadoChange(e.target.value as '' | 'true' | 'false')}
                title="Firma electrónica de la compraventa (completa o pendiente)"
                className={INPUT_CLS}
              >
                <option value="">Todos</option>
                <option value="true">Firmado</option>
                <option value="false">Pendiente</option>
              </select>
            </Field>
          ) : null}
        </div>
      ) : null}

      {/* Fila inferior: columnas + chips de periodo/filtros activos. */}
      <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
        {columnSelector}
        {periodo !== 'Sin periodo' ? (
          <span className="rounded-full bg-[#EEF5FF] px-2.5 py-1 text-xs font-semibold text-[#3B4FD6]">
            {periodo}
          </span>
        ) : null}
        {activosOrdenados.map((key) => (
          <span
            key={key}
            className="flex items-center gap-1.5 rounded-full border border-[#557EFF] px-2.5 py-1 text-xs font-semibold text-[#3B4FD6]"
          >
            {filtroLabel(key)}
            <button
              type="button"
              onClick={() => onToggleFiltroEspecifico(key)}
              aria-label={`Quitar filtro ${filtroLabel(key)}`}
              className="rounded-full transition hover:opacity-70 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
            >
              <X className="h-3 w-3" aria-hidden="true" />
            </button>
          </span>
        ))}
        {activosOrdenados.length === 0 ? (
          <span className="text-xs text-[#59677D]">Sin filtros adicionales aplicados.</span>
        ) : null}
      </div>
    </div>
  );
}
