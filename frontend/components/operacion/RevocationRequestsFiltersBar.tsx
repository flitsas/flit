'use client';

import { useEffect, useId, useRef, useState, type RefObject } from 'react';
import { ChevronDown } from 'lucide-react';
import { controlCls } from './tramites-control-styles';
import {
  REVOCATION_REQUEST_LIST_STATUSES,
  revocationRequestListLabel,
  type RevocationRequestListStatus,
} from '@/lib/tramites/estados';

/**
 * HU #12578 (Feature #12565) — filtros de la vista dedicada "Revocatorias": fecha (rango sobre
 * `requestedAt`), organismo de tránsito y estado (multi-select de los 4 sub-estados), AC1 ("los mismos
 * filtros del listado general"). Compartido por gestor (`app/tramites/revocatorias`) y OT
 * (`RevocationRequestsSection`, hub `/admin/transit-offices/[id]/revocation-requests`): el lado OT
 * omite `transitOfficeOptions` porque su alcance ya está resuelto por sesión/URL (mismo criterio que
 * el resto de la bandeja OT).
 *
 * Mismo lenguaje visual que `TramitesFiltrosBar` (`controlCls`, popover `role="dialog"` con cierre por
 * click-fuera/Escape) — presentacional, sin estado "draft/aplicado": cada cambio dispara `onChange` de
 * inmediato y el contenedor decide cuándo volver a pedir la página 1.
 */

export interface RevocationRequestsFiltersValue {
  requestedFrom: string;
  requestedTo: string;
  statuses: RevocationRequestListStatus[];
  /** `''` = todos los organismos. Ignorado si `transitOfficeOptions` no se pasa (lado OT). */
  transitOfficeId: string;
}

export interface TransitOfficeFilterOption {
  id: string;
  name: string;
  code?: string;
}

export interface RevocationRequestsFiltersBarProps {
  value: RevocationRequestsFiltersValue;
  onChange: (value: RevocationRequestsFiltersValue) => void;
  /** Presente = ofrece el selector de OT (lado gestor). Ausente = alcance ya resuelto (lado OT). */
  transitOfficeOptions?: TransitOfficeFilterOption[];
  disabled?: boolean;
}

const INPUT_CLS =
  'h-9 rounded-xl border border-[#DFE5ED] bg-white px-3 text-xs text-[#162744] outline-none transition focus:border-[#557EFF] focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/15 dark:bg-white/5 dark:text-white';
const POPOVER_SURFACE_CLS =
  'rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_8px_24px_rgba(22,39,68,0.08)] dark:border-white/10 dark:bg-[#162744]';

/** Cierre por clic fuera Y por Escape, con el foco devuelto al disparador — mismo criterio que `TramitesFiltrosBar`. */
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

interface EstadoPopoverProps {
  selected: RevocationRequestListStatus[];
  onChange: (statuses: RevocationRequestListStatus[]) => void;
  disabled?: boolean;
}

function EstadoPopover({ selected, onChange, disabled }: EstadoPopoverProps) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const close = () => setOpen(false);
  const panelRef = usePopoverDismiss(open, close, triggerRef);
  const panelId = useId();
  const activo = selected.length > 0;

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        disabled={disabled}
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        className={controlCls(activo)}
      >
        {activo ? `Estado (${selected.length})` : 'Estado'}
        <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
      {open ? (
        <div
          ref={panelRef}
          id={panelId}
          role="dialog"
          aria-label="Filtrar por estado de la revocatoria"
          className={`absolute left-0 top-full z-30 mt-2 w-64 p-3 ${POPOVER_SURFACE_CLS}`}
        >
          <fieldset className="flex flex-col gap-2">
            <legend className="text-xs font-semibold uppercase tracking-wide text-[#59677D]">
              Sub-estado de revocatoria
            </legend>
            {REVOCATION_REQUEST_LIST_STATUSES.map((estado) => {
              const checked = selected.includes(estado);
              return (
                <label key={estado} className="flex items-center gap-2 text-xs text-[#162744] dark:text-white">
                  <input
                    type="checkbox"
                    checked={checked}
                    onChange={() =>
                      onChange(
                        checked ? selected.filter((s) => s !== estado) : [...selected, estado],
                      )
                    }
                    className="h-4 w-4 accent-[#557EFF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
                  />
                  {revocationRequestListLabel(estado)}
                </label>
              );
            })}
          </fieldset>
          <div className="mt-3 flex items-center justify-between">
            <button
              type="button"
              onClick={() => onChange([])}
              className="text-xs font-semibold text-[#557EFF] hover:underline disabled:opacity-40"
              disabled={selected.length === 0}
            >
              Limpiar
            </button>
            <button
              type="button"
              onClick={close}
              className="rounded-lg px-3 py-1.5 text-xs font-semibold text-white"
              style={{ background: '#557EFF' }}
            >
              Cerrar
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

export function RevocationRequestsFiltersBar({
  value,
  onChange,
  transitOfficeOptions,
  disabled,
}: RevocationRequestsFiltersBarProps) {
  const hasActiveFilters =
    Boolean(value.requestedFrom) ||
    Boolean(value.requestedTo) ||
    value.statuses.length > 0 ||
    Boolean(value.transitOfficeId);

  return (
    <div className="flex flex-wrap items-end gap-3" role="group" aria-label="Filtros de revocatorias">
      <label className="flex flex-col gap-1">
        <span className="text-xs font-semibold uppercase tracking-wide text-[#59677D]">
          Solicitada desde
        </span>
        <input
          type="date"
          value={value.requestedFrom}
          onChange={(e) => onChange({ ...value, requestedFrom: e.target.value })}
          disabled={disabled}
          aria-label="Fecha inicial de solicitud"
          className={INPUT_CLS}
        />
      </label>

      <label className="flex flex-col gap-1">
        <span className="text-xs font-semibold uppercase tracking-wide text-[#59677D]">
          Solicitada hasta
        </span>
        <input
          type="date"
          value={value.requestedTo}
          onChange={(e) => onChange({ ...value, requestedTo: e.target.value })}
          disabled={disabled}
          aria-label="Fecha final de solicitud"
          className={INPUT_CLS}
        />
      </label>

      {transitOfficeOptions ? (
        <label className="flex flex-col gap-1">
          <span className="text-xs font-semibold uppercase tracking-wide text-[#59677D]">
            Organismo de tránsito
          </span>
          <select
            value={value.transitOfficeId}
            onChange={(e) => onChange({ ...value, transitOfficeId: e.target.value })}
            disabled={disabled}
            aria-label="Filtrar por organismo de tránsito"
            className={INPUT_CLS}
          >
            <option value="">Todos</option>
            {transitOfficeOptions.map((o) => (
              <option key={o.id} value={o.id}>
                {o.code ? `${o.name} (${o.code})` : o.name}
              </option>
            ))}
          </select>
        </label>
      ) : null}

      <EstadoPopover
        selected={value.statuses}
        onChange={(statuses) => onChange({ ...value, statuses })}
        disabled={disabled}
      />

      {hasActiveFilters ? (
        <button
          type="button"
          onClick={() =>
            onChange({ requestedFrom: '', requestedTo: '', statuses: [], transitOfficeId: '' })
          }
          disabled={disabled}
          className={controlCls(false)}
        >
          Limpiar filtros
        </button>
      ) : null}
    </div>
  );
}
