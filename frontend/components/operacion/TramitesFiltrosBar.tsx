'use client';

import { useEffect, useId, useRef, useState, type ReactNode, type RefObject } from 'react';
import { ChevronDown, Search, X } from 'lucide-react';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import {
  ESTADO_LABELS,
  type EstadoTramite,
} from '@/lib/tramites/estados';
import type { InstanceStatus } from '@/lib/api/types/procedure-runtime';

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

/** Los 5 filtros específicos que YA existen hoy: ninguno desaparece, solo se mudan al popover. */
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
/** Orden canónico (mismo de los grupos) para pintar chips y campos siempre igual, sin importar el
 *  orden en que el usuario los fue añadiendo. */
const FILTROS_ESPECIFICOS_ORDEN: FiltroEspecificoKey[] = FILTROS_ESPECIFICOS_GRUPOS.flatMap((g) =>
  g.items.map((i) => i.key),
);

export function filtroEspecificoLabel(key: FiltroEspecificoKey): string {
  return FILTROS_ESPECIFICOS_GRUPOS.flatMap((g) => g.items).find((i) => i.key === key)?.label ?? key;
}

const INPUT_CLS =
  'h-9 rounded-xl border border-[#DFE5ED] bg-white px-3 text-xs text-[#162744] outline-none transition focus:border-[#557EFF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-white/5 dark:text-white';
const POPOVER_SURFACE_CLS =
  'rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_8px_24px_rgba(22,39,68,0.08)] dark:border-white/10 dark:bg-[#162744]';

const FILTRO_BTN_CLS =
  'inline-flex h-9 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-xl border border-[#DFE5ED] bg-white px-3 text-xs font-semibold text-[#557EFF] transition hover:bg-[#EFF6FF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-[#0B0F14]';

const COLS_BTN_CLS =
  'inline-flex h-9 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-xl border border-[#DFE5ED] bg-white px-3 text-xs font-semibold text-[#1E293B] transition hover:bg-[#EFF6FF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-[#0B0F14] dark:text-white';

/** Orden KPI / filtro por estado (mismo ciclo de vida que EstadoFunnel). */
const ESTADO_FILTRO_ORDER: EstadoTramite[] = [
  'borrador',
  'preparado',
  'entregado',
  'aprobado',
  'subsanacion',
  'rechazado',
  'anulado',
];

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
        className={activo ? `${COLS_BTN_CLS} border-[#557EFF] text-[#3B4FD6]` : COLS_BTN_CLS}
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
  activos: ReadonlySet<FiltroEspecificoKey>;
  onToggle: (key: FiltroEspecificoKey) => void;
  estado: '' | InstanceStatus;
  onEstadoChange: (v: '' | InstanceStatus) => void;
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
  activos,
  onToggle,
  estado,
  onEstadoChange,
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
  const count = activos.size;

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
        className={FILTRO_BTN_CLS}
      >
        {count > 0 ? `+ Filtro (${count})` : '+ Filtro'}
        <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label="Agregar filtro"
          className={`absolute right-0 top-full z-50 mt-2 max-h-[420px] w-60 overflow-y-auto rounded-xl border border-[#DFE5ED] bg-white p-3 shadow-lg dark:border-white/10 dark:bg-[#162744]`}
        >
          <p className="text-[11px] font-bold uppercase tracking-wide text-[#1E293B] dark:text-white">
            Filtrar por estado
          </p>
          <div className="mt-2 flex flex-col">
            <button
              type="button"
              onClick={() => {
                onEstadoChange('');
                close();
              }}
              className="rounded-lg px-2 py-1.5 text-left text-xs transition hover:bg-[#EFF6FF] dark:hover:bg-white/5"
              style={{
                color: estado === '' ? '#557EFF' : '#334155',
                fontWeight: estado === '' ? 700 : 500,
              }}
            >
              Todos
            </button>
            {ESTADO_FILTRO_ORDER.map((e) => (
              <button
                key={e}
                type="button"
                onClick={() => {
                  onEstadoChange(e);
                  close();
                }}
                className="rounded-lg px-2 py-1.5 text-left text-xs transition hover:bg-[#EFF6FF] dark:hover:bg-white/5"
                style={{
                  color: estado === e ? '#557EFF' : '#334155',
                  fontWeight: estado === e ? 700 : 500,
                }}
              >
                {ESTADO_LABELS[e]}
              </button>
            ))}
          </div>

          <div className="my-3 border-t border-[#DFE5ED] dark:border-white/10" />

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

          {FILTROS_ESPECIFICOS_GRUPOS.map((grupo, gi) => (
            <div
              key={grupo.grupo}
              className={gi > 0 ? 'mt-2 border-t border-[#DFE5ED] pt-2 dark:border-white/10' : ''}
            >
              <p className="select-none px-2 py-1 text-xs font-bold uppercase tracking-wide text-[#162744] dark:text-white">
                {grupo.grupo}
              </p>
              {grupo.items.map((item) => (
                <div key={item.key}>
                  <label className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-xs font-medium text-[#162744] hover:bg-[#EEF5FF] dark:text-white dark:hover:bg-white/5">
                    <input
                      type="checkbox"
                      checked={activos.has(item.key)}
                      onChange={() => onToggle(item.key)}
                      className="h-3.5 w-3.5 accent-[#557EFF]"
                    />
                    {item.label}
                  </label>
                  {/* El campo real se despliega DEBAJO del checkbox, dentro del popover: en
                      pantalla solo queda su chip, nunca el input suelto. */}
                  {activos.has(item.key) ? (
                    <div className="px-2 pb-2 pl-8">
                      {item.key === 'placa' ? (
                        <input
                          type="search"
                          aria-label="Filtrar por placa"
                          value={placa}
                          onChange={(e) => onPlacaChange(e.target.value)}
                          placeholder="ABC123"
                          className={`${INPUT_CLS} w-full`}
                        />
                      ) : null}
                      {item.key === 'vendedor' ? (
                        <input
                          type="search"
                          aria-label="Filtrar por propietario o vendedor"
                          value={vendedor}
                          onChange={(e) => onVendedorChange(e.target.value)}
                          placeholder="Nombre"
                          className={`${INPUT_CLS} w-full`}
                        />
                      ) : null}
                      {item.key === 'comprador' ? (
                        <input
                          type="search"
                          aria-label="Filtrar por comprador"
                          value={comprador}
                          onChange={(e) => onCompradorChange(e.target.value)}
                          placeholder="Nombre"
                          className={`${INPUT_CLS} w-full`}
                        />
                      ) : null}
                      {item.key === 'gestor' ? (
                        <input
                          type="search"
                          aria-label="Filtrar por gestor"
                          value={gestor}
                          onChange={(e) => onGestorChange(e.target.value)}
                          placeholder="Nombre"
                          className={`${INPUT_CLS} w-full`}
                        />
                      ) : null}
                      {item.key === 'firmado' ? (
                        <select
                          aria-label="Filtrar por firma de compraventa"
                          value={firmado}
                          onChange={(e) => onFirmadoChange(e.target.value as '' | 'true' | 'false')}
                          title="Firma electrónica de la compraventa (completa o pendiente)"
                          className={`${INPUT_CLS} w-full`}
                        >
                          <option value="">Todos</option>
                          <option value="true">Firmado</option>
                          <option value="false">Pendiente</option>
                        </select>
                      ) : null}
                    </div>
                  ) : null}
                </div>
              ))}
            </div>
          ))}

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

  estado: '' | InstanceStatus;
  onEstadoChange: (v: '' | InstanceStatus) => void;

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

  /** `ColumnSelector` ya montado por el contenedor — último control de la fila. */
  columnSelector: ReactNode;

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
  estado,
  onEstadoChange,
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
        activos={filtrosEspecificos}
        onToggle={onToggleFiltroEspecifico}
        estado={estado}
        onEstadoChange={onEstadoChange}
        placa={placa}
        onPlacaChange={onPlacaChange}
        vendedor={vendedor}
        onVendedorChange={onVendedorChange}
        comprador={comprador}
        onCompradorChange={onCompradorChange}
        gestor={gestor}
        onGestorChange={onGestorChange}
        firmado={firmado}
        onFirmadoChange={onFirmadoChange}
        onAplicar={onAplicar}
        onEmpezarDeCero={onEmpezarDeCero}
        empezarDeCeroDisabled={empezarDeCeroDisabled}
        isAdmin={isAdmin}
        companias={companias}
        compania={compania}
        onCompaniaChange={onCompaniaChange}
      />

      {columnSelector}
    </>
  );
}

export interface TramitesFiltrosChipsProps {
  periodo: string;
  filtrosEspecificos: ReadonlySet<FiltroEspecificoKey>;
  onToggleFiltroEspecifico: (key: FiltroEspecificoKey) => void;
  onQuitarPeriodo: () => void;
  /** Valores YA APLICADOS (no el borrador): el chip solo muestra `Etiqueta: valor` una vez que el
   *  usuario pulsó "Aplicar" — mientras tanto queda solo la etiqueta, sin adelantar un valor que
   *  todavía no rige la consulta. */
  appliedPlaca: string;
  appliedVendedor: string;
  appliedComprador: string;
  appliedGestor: string;
  appliedFirmado: '' | 'true' | 'false';
}

/**
 * Tira de chips debajo de la fila de tabs: SOLO se renderiza si hay algo activo (periodo o algún
 * filtro específico). Sin tarjeta ni borde — es lo único que delata que hay filtros aplicados,
 * ahora que sus campos viven escondidos dentro de los popovers.
 */
export function TramitesFiltrosChips({
  periodo,
  filtrosEspecificos,
  onToggleFiltroEspecifico,
  onQuitarPeriodo,
  appliedPlaca,
  appliedVendedor,
  appliedComprador,
  appliedGestor,
  appliedFirmado,
}: TramitesFiltrosChipsProps) {
  const activosOrdenados = FILTROS_ESPECIFICOS_ORDEN.filter((k) => filtrosEspecificos.has(k));
  const periodoActivo = periodo !== 'Sin periodo';

  const valorDe = (key: FiltroEspecificoKey): string => {
    switch (key) {
      case 'placa':
        return appliedPlaca;
      case 'vendedor':
        return appliedVendedor;
      case 'comprador':
        return appliedComprador;
      case 'gestor':
        return appliedGestor;
      case 'firmado':
        return appliedFirmado === 'true' ? 'Firmado' : appliedFirmado === 'false' ? 'Pendiente' : '';
      default:
        return '';
    }
  };

  if (!periodoActivo && activosOrdenados.length === 0) return null;

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
      {activosOrdenados.map((key) => {
        const label = filtroEspecificoLabel(key);
        const valor = valorDe(key).trim();
        return (
          <span
            key={key}
            className="flex items-center gap-1.5 rounded-full border border-[#557EFF] px-2.5 py-1 text-xs font-semibold text-[#3B4FD6]"
          >
            {valor ? `${label}: ${valor}` : label}
            <button
              type="button"
              onClick={() => onToggleFiltroEspecifico(key)}
              aria-label={`Quitar filtro ${label}`}
              className="rounded-full transition hover:opacity-70 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
            >
              <X className="h-3 w-3" aria-hidden="true" />
            </button>
          </span>
        );
      })}
    </div>
  );
}
