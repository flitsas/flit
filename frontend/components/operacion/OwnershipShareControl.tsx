'use client';

import { Plus, X } from 'lucide-react';
import { WIZARD_LABEL } from './wizard-field-styles';

/**
 * Múltiple Propietario (ADR-0053, docs/design/MULTIPLE-PROPIETARIO-diseno-tecnico.md §8 "PR 2 —
 * Frontend"). Fila de pestañas + control de porcentaje de un LADO del trámite (vendedor o
 * comprador) cuando ese lado tiene 2+ propietarios.
 *
 * Componente de presentación puro: no conoce `ActorsForm.tsx` ni el estado de identidad de cada
 * actor — solo recibe la lista de pestañas de ESTE lado (`items`, ya resuelta por índice real en
 * el array `actors`) y notifica intención (`onSelectTab`/`onAdd`/`onRemove`/`onPercentageChange`).
 * Toda la lógica de reparto (auto-absorción del solidario, redistribución al eliminar, validación
 * de suma=100) vive en `frontend/lib/tramites/ownership-share.ts`, testeada aparte sin RTL.
 *
 * Con un solo propietario (`revealed=false`) el flujo NO se toca (encargo cerrado): no hay
 * pestañas ni bloque de porcentaje, solo el disparador para agregar un copropietario — el punto de
 * entrada que el diseño no especifica explícitamente (decisión de UI del frontend-agent, ver
 * handoff). Una vez `revealed`, el bloque no vuelve a ocultarse aunque quede un solo propietario.
 */

export interface OwnershipShareItem {
  /** Índice real del actor en el array `actors` de `ActorsForm.tsx` — NO la posición en `items`. */
  index: number;
  /** Posición dentro del lado, 1-based (1 = principal/solidario). */
  ordinal: number;
  /** Rótulo ya resuelto (`rotuloDelActor(rol, ordinal)` — «Comprador 1», «Vendedor 2», …). */
  label: string;
  /** Porcentaje actual (0 si aún no tiene valor). */
  percentage: number;
  /** El ordinal=1 nunca se puede eliminar (encargo cerrado). */
  removable: boolean;
}

export interface OwnershipShareControlProps {
  /** Pestañas de este lado, en orden de ordinal. */
  items: OwnershipShareItem[];
  /** Índice real (en `actors`) de la pestaña activa. */
  activeIndex: number;
  onSelectTab: (index: number) => void;
  onAdd: () => void;
  onRemove: (index: number) => void;
  onPercentageChange: (index: number, value: number) => void;
  /** `items.length > 1` en el momento de montar, o siguió siéndolo alguna vez (no se oculta). */
  revealed: boolean;
  /** Ya se alcanzó el máximo de 4 — el botón "+" se deshabilita. */
  maxReached: boolean;
  readOnly?: boolean;
  /** Prefijo único para ids de accesibilidad (evita colisión entre lados en la misma página). */
  idPrefix: string;
  /** Rótulo del lado sin ordinal (p. ej. «Comprador»), para textos generales del bloque. */
  sideLabel: string;
  /** Muestra los mensajes de bloqueo (se activa junto con `showErrors` del formulario). */
  showErrors?: boolean;
}

const ADD_BUTTON_CLASS =
  'grid h-8 w-8 shrink-0 place-items-center rounded-full text-white transition ' +
  'disabled:cursor-not-allowed disabled:opacity-40 focus:outline-none focus-visible:ring-2 ' +
  'focus-visible:ring-offset-2 focus-visible:ring-[#557EFF]';

export function OwnershipShareControl({
  items,
  activeIndex,
  onSelectTab,
  onAdd,
  onRemove,
  onPercentageChange,
  revealed,
  maxReached,
  readOnly = false,
  idPrefix,
  sideLabel,
  showErrors = false,
}: OwnershipShareControlProps) {
  if (!revealed) {
    return (
      <div className="mb-3 flex items-center justify-between gap-3 rounded-xl border border-dashed p-2.5" style={{ borderColor: '#DFE5ED' }}>
        <p className="text-xs opacity-70">
          ¿Hay más de un propietario? Puedes agregar hasta 3 copropietarios más.
        </p>
        <button
          type="button"
          onClick={onAdd}
          disabled={readOnly}
          aria-label={`Agregar copropietario de ${sideLabel.toLowerCase()}`}
          className={ADD_BUTTON_CLASS}
          style={{ backgroundColor: '#557EFF' }}
        >
          <Plus className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>
    );
  }

  const active = items.find((i) => i.index === activeIndex) ?? items[0];
  const total = items.reduce((s, i) => s + i.percentage, 0);
  const sumError = Math.abs(total - 100) > 0.005;
  const zeroError = items.some((i) => i.percentage <= 0);
  const tablistId = `${idPrefix}-ownership-tablist`;
  const panelId = `${idPrefix}-ownership-panel`;

  return (
    <div className="mb-3 space-y-3 rounded-xl border p-3" style={{ borderColor: '#DFE5ED' }}>
      {/* Fila de pestañas: rótulo + porcentaje inline, botón circular "+" al final, etiqueta de
          copropiedad a la derecha (maqueta de referencia del encargo). */}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div
          id={tablistId}
          role="tablist"
          aria-label={`Copropietarios — ${sideLabel}`}
          className="flex flex-wrap items-center gap-1.5"
        >
          {items.map((item) => {
            const isActive = item.index === activeIndex;
            return (
              <div key={item.index} className="flex items-center">
                <button
                  type="button"
                  role="tab"
                  id={`${idPrefix}-tab-${item.index}`}
                  aria-selected={isActive}
                  aria-controls={panelId}
                  onClick={() => onSelectTab(item.index)}
                  className={`rounded-full px-3 py-1.5 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] ${
                    item.removable ? 'rounded-r-none' : ''
                  }`}
                  style={
                    isActive
                      ? { background: '#557EFF', color: '#fff' }
                      : { background: 'rgba(85,126,255,0.08)', color: '#162744' }
                  }
                >
                  {item.label} · {formatPercent(item.percentage)}%
                </button>
                {item.removable && !readOnly && (
                  <button
                    type="button"
                    onClick={() => onRemove(item.index)}
                    aria-label={`Quitar ${item.label}`}
                    className="grid h-[26px] w-[22px] place-items-center rounded-r-full text-xs transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                    style={
                      isActive
                        ? { background: '#557EFF', color: '#fff' }
                        : { background: 'rgba(85,126,255,0.08)', color: '#162744' }
                    }
                  >
                    <X className="h-3 w-3" aria-hidden="true" />
                  </button>
                )}
              </div>
            );
          })}
          <button
            type="button"
            onClick={onAdd}
            disabled={readOnly || maxReached}
            aria-label={`Agregar copropietario de ${sideLabel.toLowerCase()}`}
            className={ADD_BUTTON_CLASS}
            style={{ backgroundColor: '#557EFF' }}
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
        <span className="text-xs opacity-70 whitespace-nowrap">
          Copropiedad · máximo {4} propietarios
        </span>
      </div>

      {/* Control de porcentaje del actor de la pestaña activa. */}
      <div id={panelId} role="tabpanel" aria-labelledby={`${idPrefix}-tab-${active.index}`} className="space-y-2">
        <p className={`${WIZARD_LABEL} mb-0`}>Porcentaje de propiedad — {active.label}</p>
        <div className="flex items-center gap-3">
          <label htmlFor={`${idPrefix}-slider-${active.index}`} className="sr-only">
            Porcentaje de {active.label} (aproximado, en enteros)
          </label>
          <input
            id={`${idPrefix}-slider-${active.index}`}
            type="range"
            min={0}
            max={100}
            step={1}
            value={clampDisplay(active.percentage)}
            disabled={readOnly}
            aria-valuetext={`${formatPercent(active.percentage)}%`}
            onChange={(e) => onPercentageChange(active.index, Number(e.target.value))}
            className="h-2 flex-1 accent-[#557EFF]"
          />
          <label htmlFor={`${idPrefix}-input-${active.index}`} className="sr-only">
            Porcentaje exacto de {active.label}, hasta dos decimales
          </label>
          <input
            id={`${idPrefix}-input-${active.index}`}
            type="number"
            min={0}
            max={100}
            step={0.01}
            inputMode="decimal"
            disabled={readOnly}
            value={active.percentage}
            onChange={(e) => onPercentageChange(active.index, Number(e.target.value))}
            className="w-20 rounded-lg border px-2 py-1 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            style={{ borderColor: '#DFE5ED' }}
          />
          <span className="text-xs opacity-70" aria-hidden="true">%</span>
        </div>

        {/* Consolidado de todos los propietarios del lado. */}
        <p className="text-xs" aria-live="polite">
          {items.map((item, i) => (
            <span key={item.index}>
              {i > 0 ? '   ' : ''}
              {item.label}: {formatPercent(item.percentage)}%
            </span>
          ))}
          {'   '}
          <span
            className="font-semibold"
            style={sumError ? { color: '#FF4E00' } : undefined}
          >
            Total: {formatPercent(total)}%
          </span>
        </p>

        {showErrors && (sumError || zeroError) && (
          <div role="alert" className="space-y-1 text-xs" style={{ color: '#FF4E00' }}>
            {sumError && <p>La suma de los porcentajes debe ser exactamente 100%.</p>}
            {zeroError && <p>Todos los propietarios deben tener un porcentaje mayor a 0%.</p>}
          </div>
        )}
      </div>
    </div>
  );
}

function formatPercent(value: number): string {
  const rounded = Math.round((value + Number.EPSILON) * 100) / 100;
  return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(2).replace(/0+$/, '').replace(/\.$/, '');
}

/** El slider (enteros 0..100) no puede mostrar un valor negativo o fuera de rango. */
function clampDisplay(value: number): number {
  if (Number.isNaN(value)) return 0;
  return Math.min(100, Math.max(0, Math.round(value)));
}
