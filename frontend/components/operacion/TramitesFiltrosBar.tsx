'use client';

import { useEffect, useId, useRef, useState, type ReactNode, type RefObject } from 'react';
import { ChevronDown, Search, X } from 'lucide-react';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import { controlCls } from './tramites-control-styles';
import { describeCondition } from '@/components/consultas/QueryFilterBar';
import type { QueryCondition, QueryField } from '@/lib/api/queries';

/**
 * Fila de acciones compactas del listado de trámites (Track A). Reemplaza a la tarjeta blanca de
 * filtros SIEMPRE visible (~185px de alto, casi muda en reposo): ahora vive DENTRO de la fila de
 * tabs de `TramitesListToolbar` (prop `actions`), como búsqueda + dos popovers (Periodo, + Filtro)
 * + el `ColumnSelector`. La tabla queda como foco de la pantalla.
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

const INPUT_CLS =
  'h-9 rounded-xl border border-[#DFE5ED] bg-white px-3 text-xs text-[#162744] outline-none transition focus:border-[#557EFF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-white/5 dark:text-white';
const POPOVER_SURFACE_CLS =
  'rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_8px_24px_rgba(22,39,68,0.08)] dark:border-white/10 dark:bg-[#162744]';


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

/** Cierre por clic fuera Y por Escape, con el foco devuelto al disparador. */
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

const RANGO_SOBRE_OPTIONS: { value: RangoSobre; label: string }[] = [
  { value: 'created', label: 'Fecha de creación' },
  { value: 'updated', label: 'Última actualización' },
];

interface PeriodoPopoverProps {
  rangoSobre: RangoSobre;
  onRangoSobreChange: (v: RangoSobre) => void;
  periodo: string;
  onPeriodoChange: (v: string) => void;
  rangoPropioDesde: string;
  rangoPropioHasta: string;
  onRangoPropioDesdeChange: (v: string) => void;
  onRangoPropioHastaChange: (v: string) => void;
  onAplicar: () => void;
}

function PeriodoPopover({
  rangoSobre,
  onRangoSobreChange,
  periodo,
  onPeriodoChange,
  rangoPropioDesde,
  rangoPropioHasta,
  onRangoPropioDesdeChange,
  onRangoPropioHastaChange,
  onAplicar,
}: PeriodoPopoverProps) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const close = () => setOpen(false);
  const panelRef = usePopoverDismiss(open, close, triggerRef);
  const panelId = useId();
  const activo = periodo !== 'Sin periodo';

  const handleLimpiar = () => {
    onPeriodoChange('Sin periodo');
    onRangoPropioDesdeChange('');
    onRangoPropioHastaChange('');
    close();
  };
  const handleAplicar = () => {
    onAplicar();
    close();
  };

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        className={controlCls(activo)}
      >
        {activo ? periodo : 'Periodo'}
        <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label="Elegir periodo"
          className={`absolute right-0 top-full z-30 mt-2 w-64 p-3 ${POPOVER_SURFACE_CLS}`}
        >
          <Field label="Rango sobre">
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

          <div className="h-2" aria-hidden="true" />

          <Field label="Periodo">
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
            <>
              <div className="h-2" aria-hidden="true" />
              <Field label="Fecha inicial">
                <input
                  type="date"
                  value={rangoPropioDesde}
                  onChange={(e) => onRangoPropioDesdeChange(e.target.value)}
                  aria-label="Fecha inicial del rango propio"
                  className={INPUT_CLS}
                />
              </Field>
              <div className="h-2" aria-hidden="true" />
              <Field label="Fecha final">
                <input
                  type="date"
                  value={rangoPropioHasta}
                  onChange={(e) => onRangoPropioHastaChange(e.target.value)}
                  aria-label="Fecha final del rango propio"
                  className={INPUT_CLS}
                />
              </Field>
            </>
          ) : null}

          <div className="mt-3 flex items-center justify-end gap-2 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
            <button
              type="button"
              onClick={handleLimpiar}
              className="rounded-xl px-3 py-2 text-xs font-semibold text-[#59677D] transition hover:bg-[#DFE5ED]/40 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
            >
              Limpiar
            </button>
            <button
              type="button"
              onClick={handleAplicar}
              className="rounded-xl px-3 py-2 text-xs font-semibold text-white transition hover:opacity-95 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
              style={{ background: WIZARD_CTA_GRADIENT }}
            >
              Aplicar
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

interface FiltroEspecificoPopoverProps {
  queryFilterBar: ReactNode;
  condicionesCount: number;
  onAplicar: () => void;
  onEmpezarDeCero: () => void;
  empezarDeCeroDisabled: boolean;
  /** #1 — filtro de compañía, SOLO SuperAdmin (ve trámites de todas las empresas). */
  isAdmin: boolean;
  companias: readonly string[];
  compania: string;
  onCompaniaChange: (v: string) => void;
}

function FiltroEspecificoPopover({
  queryFilterBar,
  condicionesCount,
  onAplicar,
  onEmpezarDeCero,
  empezarDeCeroDisabled,
  isAdmin,
  companias,
  compania,
  onCompaniaChange,
}: FiltroEspecificoPopoverProps) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const close = () => setOpen(false);
  const panelRef = usePopoverDismiss(open, close, triggerRef);
  const panelId = useId();
  const count = condicionesCount;

  const handleAplicar = () => {
    onAplicar();
    close();
  };
  const handleEmpezarDeCero = () => {
    onEmpezarDeCero();
    close();
  };

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        // Marcado en azul cuando hay filtros aplicados, igual que "Periodo": antes el contador
        // era la única señal, y el azul del texto estaba puesto en reposo, sin significar nada.
        className={controlCls(count > 0)}
      >
        {/* "Filtros" y no "+ Filtro": desde la HU #12107 el panel contiene un constructor
            completo, cuyo propio botón de añadir ya se llama "+ Filtro". Dos controles con el
            mismo nombre, uno dentro del otro, no se pueden nombrar en voz alta. */}
        {count > 0 ? `Filtros (${count})` : 'Filtros'}
        <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label="Agregar filtro"
          className={`absolute right-0 top-full z-50 mt-2 max-h-[480px] w-[26rem] overflow-y-auto rounded-xl border border-[#DFE5ED] bg-white p-3 shadow-lg dark:border-white/10 dark:bg-[#162744]`}
        >
          {isAdmin && companias.length > 0 ? (
            <div className="border-b border-[#DFE5ED] pb-2 dark:border-white/10">
              <p className="select-none px-2 py-1 text-xs font-bold uppercase tracking-wide text-[#162744] dark:text-white">
                ALCANCE
              </p>
              <div className="px-2 py-1">
                <Field label="Compañía">
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
              </div>
            </div>
          ) : null}

          {/* HU #12107 — la gramática de Consultas: campo, operador y valores, con cada condición
              como un chip. Sustituye a los checkboxes con un input fijo por campo, que solo sabían
              preguntar "contiene" y obligaban a añadir un campo nuevo aquí cada vez. Lo que se
              ofrece lo manda el servidor (`queryFields`), así que un campo nuevo aparece sin
              desplegar frontend. */}
          <div className="py-1">{queryFilterBar}</div>

          <div className="mt-2 flex items-center justify-end gap-2 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
            <button
              type="button"
              onClick={handleEmpezarDeCero}
              disabled={empezarDeCeroDisabled}
              className="rounded-xl border border-[#FF4E00] px-3 py-2 text-xs font-semibold text-[#C2410C] transition hover:bg-[#FF4E00]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-40"
            >
              Empezar de cero
            </button>
            <button
              type="button"
              onClick={handleAplicar}
              className="rounded-xl px-3 py-2 text-xs font-semibold text-white transition hover:opacity-95 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
              style={{ background: WIZARD_CTA_GRADIENT }}
            >
              Aplicar
            </button>
          </div>
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

  /** HU #12107 — `QueryFilterBar` ya montada por el contenedor, con el catálogo del servidor. */
  queryFilterBar: ReactNode;

  /** Cuántas condiciones hay aplicadas: es lo que numera y colorea el disparador "+ Filtro". */
  condicionesCount: number;

  search: string;
  onSearchChange: (v: string) => void;

  onAplicar: () => void;
  onEmpezarDeCero: () => void;
  empezarDeCeroDisabled?: boolean;

  /** `ColumnSelector` ya montado por el contenedor — penúltimo control de la fila. */
  columnSelector: ReactNode;

  /** HU #12104 — "Exportar a Excel", el último control: actúa sobre lo que los demás acotaron. */
  exportAction?: ReactNode;

  /** #1 — filtro de compañía, SOLO SuperAdmin (ve trámites de todas las empresas). */
  isAdmin: boolean;
  companias: readonly string[];
  compania: string;
  onCompaniaChange: (v: string) => void;
}

/**
 * Fila de acciones compactas: búsqueda (siempre visible) + popover "Periodo" + popover "+ Filtro"
 * + `ColumnSelector`, en ese orden. Pensada para pintarse dentro de la prop `actions` de
 * `TramitesListToolbar` — devuelve un fragmento (sin envoltorio) para que el `gap` del flex
 * contenedor separe sus hijos como si fueran hermanos directos.
 */
export function TramitesFiltrosBar({
  rangoSobre,
  onRangoSobreChange,
  periodo,
  onPeriodoChange,
  rangoPropioDesde,
  rangoPropioHasta,
  onRangoPropioDesdeChange,
  onRangoPropioHastaChange,
  queryFilterBar,
  condicionesCount,
  search,
  onSearchChange,
  onAplicar,
  onEmpezarDeCero,
  empezarDeCeroDisabled = false,
  columnSelector,
  exportAction,
  isAdmin,
  companias,
  compania,
  onCompaniaChange,
}: TramitesFiltrosBarProps) {
  return (
    <>
      {/* Búsqueda: ancho fijo w-64, icono marca — spec flit-tramites-chrome. */}
      <div className="relative w-64 shrink-0">
        <Search
          className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[#557EFF]"
          aria-hidden="true"
        />
        <input
          type="search"
          aria-label="Buscar trámites"
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder="Buscar radicado, placa, VIN..."
          className={`${INPUT_CLS} w-full pl-9`}
        />
      </div>

      <PeriodoPopover
        rangoSobre={rangoSobre}
        onRangoSobreChange={onRangoSobreChange}
        periodo={periodo}
        onPeriodoChange={onPeriodoChange}
        rangoPropioDesde={rangoPropioDesde}
        rangoPropioHasta={rangoPropioHasta}
        onRangoPropioDesdeChange={onRangoPropioDesdeChange}
        onRangoPropioHastaChange={onRangoPropioHastaChange}
        onAplicar={onAplicar}
      />

      <FiltroEspecificoPopover
        queryFilterBar={queryFilterBar}
        condicionesCount={condicionesCount}
        onAplicar={onAplicar}
        onEmpezarDeCero={onEmpezarDeCero}
        empezarDeCeroDisabled={empezarDeCeroDisabled}
        isAdmin={isAdmin}
        companias={companias}
        compania={compania}
        onCompaniaChange={onCompaniaChange}
      />

      {columnSelector}
      {exportAction}
    </>
  );
}

export interface TramitesFiltrosChipsProps {
  periodo: string;
  onQuitarPeriodo: () => void;
  /** HU #12107 — condiciones APLICADAS (no el borrador): la tira dice qué está filtrando ahora. */
  condiciones: QueryCondition[];
  /** El catálogo, para leer la etiqueta del campo y de sus opciones en vez del identificador. */
  fields: QueryField[];
  onQuitarCondicion: (fieldId: string) => void;
}

/**
 * Tira de chips de lo que está filtrando AHORA MISMO.
 *
 * <p>Muestra lo aplicado, no el borrador: un chip que anuncia un filtro que todavía no se ha
 * pulsado «Aplicar» diría que la tabla está acotada cuando no lo está.</p>
 *
 * <p>El texto de cada condición lo arma `describeCondition`, el mismo de Consultas, para que una
 * condición se lea igual en las dos pantallas — incluido el resumen «3 valores» cuando se pegó una
 * lista larga, que dentro de un chip es lo único legible.</p>
 */
export function TramitesFiltrosChips({
  periodo,
  onQuitarPeriodo,
  condiciones,
  fields,
  onQuitarCondicion,
}: TramitesFiltrosChipsProps) {
  const periodoActivo = periodo !== 'Sin periodo';

  if (!periodoActivo && condiciones.length === 0) return null;

  return (
    <div className="mt-2 flex flex-wrap items-center gap-2">
      {periodoActivo ? (
        <span className="flex items-center gap-1.5 rounded-full bg-[#EEF5FF] px-2.5 py-1 text-xs font-semibold text-[#3B4FD6]">
          {periodo}
          <button
            type="button"
            onClick={onQuitarPeriodo}
            aria-label="Quitar filtro de periodo"
            className="rounded-full transition hover:opacity-70 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          >
            <X className="h-3 w-3" aria-hidden="true" />
          </button>
        </span>
      ) : null}
      {condiciones.map((condicion) => (
        <span
          key={condicion.fieldId}
          className="flex items-center gap-1.5 rounded-full border border-[#557EFF] px-2.5 py-1 text-xs font-semibold text-[#3B4FD6]"
        >
          {describeCondition(condicion, fields)}
          <button
            type="button"
            onClick={() => onQuitarCondicion(condicion.fieldId)}
            aria-label={`Quitar filtro ${describeCondition(condicion, fields)}`}
            className="rounded-full transition hover:opacity-70 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          >
            <X className="h-3 w-3" aria-hidden="true" />
          </button>
        </span>
      ))}
    </div>
  );
}
