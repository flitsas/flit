'use client';

import { Plus, X } from 'lucide-react';
import { WIZARD_LABEL } from './wizard-field-styles';

/**
 * Múltiple Propietario (ADR-0053, docs/design/MULTIPLE-PROPIETARIO-diseno-tecnico.md §8 "PR 2 —
 * Frontend"). Fila de pestañas + control de porcentaje de un LADO del trámite (vendedor o
 * comprador) cuando ese lado tiene 2+ propietarios.
 *
 * Fidelidad visual: la estructura de pestañas, el color por participación (`colorDeParticipacion`)
 * y el bloque de porcentaje replican `Propietarios.tsx` del prototipo FLIT (repo `flit-2.0`,
 * `src/components/atom/modules/Propietarios.tsx`) — adaptados a los tokens de `flit` (sin copiar
 * hex del prototipo: los colores de participación usan las variables de `StatusBadge` ya definidas
 * en `globals.css`). La LÓGICA es la de `ownership-share.ts`, sin relación con la del prototipo
 * (que reparte con el residuo en el ÚLTIMO propietario, en enteros, y sin bloqueo — ninguna de las
 * tres aplica aquí).
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

/**
 * Color por participación relativa (`Propietarios.tsx` del prototipo, función `colorDeParticipacion`,
 * traída tal cual: el porcentaje más alto en verde, el más bajo en el tono de mayor énfasis, el resto
 * en el intermedio; todos iguales → verde). Se usa en las pestañas y en el consolidado del pie.
 *
 * Adaptación a `flit`: el prototipo pinta con tres hex sueltos (`#22C55E`/`#F97316`/`#EAB308`) que no
 * existen en los tokens del proyecto; aquí se resuelve con las variables de `StatusBadge`
 * (`globals.css`, ya theme-aware) que cubren el mismo rol semántico — éxito/alerta/aviso — sin
 * introducir una paleta nueva.
 */
function colorDeParticipacion(pct: number, all: number[]): string {
  if (all.length === 0) return 'var(--badge-success-fg)';
  const max = Math.max(...all);
  const min = Math.min(...all);
  if (max === min) return 'var(--badge-success-fg)';
  if (pct === max) return 'var(--badge-success-fg)';
  if (pct === min) return 'var(--badge-danger-fg)';
  return 'var(--badge-warning-fg)';
}

const ADD_BUTTON_CLASS =
  'grid h-9 w-9 shrink-0 place-items-center rounded-full text-white transition ' +
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
  const allPct = items.map((i) => i.percentage);
  const total = allPct.reduce((s, p) => s + p, 0);
  const sumError = Math.abs(total - 100) > 0.005;
  const zeroError = items.some((i) => i.percentage <= 0);
  const tablistId = `${idPrefix}-ownership-tablist`;
  const panelId = `${idPrefix}-ownership-panel`;
  const activeColor = colorDeParticipacion(active.percentage, allPct);
  const activeSliderValue = clampDisplay(active.percentage);

  return (
    <div className="mb-3 space-y-3 rounded-xl border p-3" style={{ borderColor: '#DFE5ED' }}>
      {/* Fila de pestañas: rótulo + porcentaje inline (coloreado por participación), botón circular
          "+" al final, etiqueta de copropiedad a la derecha — estructura de `Propietarios.tsx`. */}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div
          id={tablistId}
          role="tablist"
          aria-label={`Copropietarios — ${sideLabel}`}
          className="flex flex-wrap items-center gap-1.5"
        >
          {items.map((item) => {
            const isActive = item.index === activeIndex;
            const showRemove = isActive && item.removable && !readOnly;
            return (
              <div
                key={item.index}
                className={`flex h-9 items-center gap-1.5 rounded-xl border text-xs font-semibold transition ${
                  showRemove ? 'pl-3 pr-1.5' : 'px-3'
                }`}
                style={
                  isActive
                    ? { borderColor: '#557EFF', color: '#557EFF', background: 'rgba(85,126,255,0.08)' }
                    : { borderColor: '#DFE5ED', color: '#59677D', background: '#fff' }
                }
              >
                <button
                  type="button"
                  role="tab"
                  id={`${idPrefix}-tab-${item.index}`}
                  aria-selected={isActive}
                  aria-controls={panelId}
                  onClick={() => onSelectTab(item.index)}
                  className="flex items-center gap-1.5 rounded focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                >
                  <span>{item.label}</span>{' '}
                  <span className="font-bold" style={{ color: colorDeParticipacion(item.percentage, allPct) }}>
                    {formatPercent(item.percentage)}%
                  </span>
                </button>
                {showRemove && (
                  <button
                    type="button"
                    onClick={() => onRemove(item.index)}
                    aria-label={`Quitar ${item.label}`}
                    className="grid place-items-center rounded p-0.5 hover:bg-black/5 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
                  >
                    <X className="h-3.5 w-3.5" aria-hidden="true" />
                  </button>
                )}
              </div>
            );
          })}
          <button
            type="button"
            onClick={onAdd}
            disabled={readOnly || maxReached}
            title={maxReached ? 'Máximo 4 propietarios permitidos por trámite.' : 'Agregar copropietario'}
            aria-label={`Agregar copropietario de ${sideLabel.toLowerCase()}`}
            className={ADD_BUTTON_CLASS}
            style={{ backgroundColor: '#557EFF' }}
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
        <span className="text-xs opacity-70 whitespace-nowrap">Copropiedad: hasta 4 propietarios</span>
      </div>

      {/* Control de porcentaje del actor de la pestaña activa. */}
      <div
        id={panelId}
        role="tabpanel"
        aria-labelledby={`${idPrefix}-tab-${active.index}`}
        className="rounded-xl border p-3"
        style={{ borderColor: '#DFE5ED', background: 'rgba(223,229,237,0.35)' }}
      >
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className={`${WIZARD_LABEL} text-[13px] font-bold`} style={{ color: '#162744' }}>
            Porcentaje de propiedad
          </p>
          <div className="flex items-center gap-2">
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
              className="w-20 rounded-xl border bg-white px-3 py-2 text-right text-xs outline-none transition focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: '#DFE5ED', color: '#162744' }}
            />
            <span className="text-xs font-semibold" style={{ color: '#334155' }} aria-hidden="true">
              %
            </span>
          </div>
        </div>

        <div className="mt-3">
          <label htmlFor={`${idPrefix}-slider-${active.index}`} className="sr-only">
            Porcentaje de {active.label} (aproximado, en enteros)
          </label>
          <input
            id={`${idPrefix}-slider-${active.index}`}
            type="range"
            min={0}
            max={100}
            step={1}
            value={activeSliderValue}
            disabled={readOnly}
            aria-valuetext={`${formatPercent(active.percentage)}%`}
            onChange={(e) => onPercentageChange(active.index, Number(e.target.value))}
            className="h-2 w-full appearance-none rounded-full outline-none transition disabled:opacity-70 [&::-webkit-slider-thumb]:h-4 [&::-webkit-slider-thumb]:w-4 [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:border-2 [&::-webkit-slider-thumb]:bg-white [&::-webkit-slider-thumb]:shadow"
            style={{
              background: `linear-gradient(to right, ${activeColor} ${activeSliderValue}%, #DFE5ED ${activeSliderValue}%)`,
              accentColor: activeColor,
              borderColor: activeColor,
            }}
          />
        </div>

        {/* Consolidado de todos los propietarios del lado, cada uno coloreado por participación. */}
        <div className="mt-3 flex flex-wrap items-center gap-3">
          {items.map((item) => (
            <span
              key={item.index}
              className="text-xs font-semibold"
              style={{ color: colorDeParticipacion(item.percentage, allPct) }}
            >
              {item.label}: {formatPercent(item.percentage)}%
            </span>
          ))}
          <span className="text-xs" style={sumError ? { color: 'var(--badge-danger-fg)', fontWeight: 600 } : { opacity: 0.7 }}>
            Total: {formatPercent(total)}%
          </span>
        </div>

        {showErrors && (sumError || zeroError) && (
          <div role="alert" className="mt-2 space-y-1 text-xs" style={{ color: '#FF4E00' }}>
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
