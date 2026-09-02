'use client';

import { Plus, X } from 'lucide-react';
import { WIZARD_LABEL } from './wizard-field-styles';

/**
 * Múltiple Propietario (ADR-0053, docs/design/MULTIPLE-PROPIETARIO-diseno-tecnico.md §8 "PR 2 —
 * Frontend"). Fila de pestañas (SIEMPRE visible, 1..4 propietarios) + control de porcentaje de un
 * LADO del trámite (vendedor o comprador) — este último solo desde el segundo propietario.
 *
 * Fidelidad visual: pestañas en píldora, color por participación (`colorDeParticipacion`) y bloque
 * de porcentaje replican la imagen de referencia del usuario, verificada contra `Propietarios.tsx`
 * del prototipo FLIT (repo `flit-2.0`, `src/components/atom/modules/Propietarios.tsx`) — adaptados
 * a los tokens de `flit` (sin copiar hex del prototipo: los colores de participación usan las
 * variables de `StatusBadge` ya definidas en `globals.css`). La LÓGICA es la de
 * `ownership-share.ts`, sin relación con la del prototipo (que reparte con el residuo en el
 * ÚLTIMO propietario, en enteros, y sin bloqueo — ninguna de las tres aplica aquí).
 *
 * Componente de presentación puro: no conoce `ActorsForm.tsx` ni el estado de identidad de cada
 * actor — solo recibe la lista de pestañas de ESTE lado (`items`, ya resuelta por índice real en
 * el array `actors`) y notifica intención (`onSelectTab`/`onAdd`/`onRemove`/`onPercentageChange`).
 * Toda la lógica de reparto (auto-absorción del solidario, redistribución al eliminar, validación
 * de suma=100) vive en `frontend/lib/tramites/ownership-share.ts`, testeada aparte sin RTL.
 *
 * `revealed` gobierna SOLO el bloque "Porcentaje de propiedad" (tarjeta con slider, casilla y
 * consolidado), no la fila de pestañas: la imagen de referencia del usuario confirma que con un
 * solo propietario la fila de pestañas se ve igual que con 2+ (una sola píldora activa al 100%,
 * en verde, sin `×`, más el botón `+`) — lo único que cambia es que el bloque de porcentaje no
 * aparece hasta el segundo propietario. Una vez `revealed`, ese bloque no vuelve a ocultarse
 * aunque quede un solo propietario (encargo cerrado).
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
      {/* Fila de pestañas: píldoras redondeadas con rótulo + porcentaje (coloreado por
          participación), la "×" dentro de la píldora activa al final, botón circular "+" al cierre
          de la fila, etiqueta de copropiedad a la derecha con el máximo resaltado en azul. */}
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
                className={`flex h-9 items-center gap-1.5 rounded-full border pl-3 text-xs font-semibold transition ${
                  showRemove ? 'pr-2' : 'pr-3'
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
                  aria-controls={revealed ? panelId : undefined}
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
                    className="grid place-items-center rounded opacity-70 transition hover:opacity-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
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
        <span className="text-xs opacity-70 whitespace-nowrap">
          Copropiedad: hasta <span className="font-semibold" style={{ color: '#557EFF' }}>4</span> propietarios
        </span>
      </div>

      {/* Control de porcentaje del actor de la pestaña activa — SOLO desde el segundo
          propietario (encargo cerrado): con uno solo, la fila de pestañas de arriba ya lo cubre
          por completo y este bloque no aporta nada nuevo. */}
      {revealed && (
      <div
        id={panelId}
        role="tabpanel"
        aria-labelledby={`${idPrefix}-tab-${active.index}`}
        className="rounded-xl border p-3"
        style={{ borderColor: 'rgba(223,229,237,0.6)', background: 'rgba(223,229,237,0.18)' }}
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
            className="h-1.5 w-full appearance-none rounded-full outline-none transition disabled:opacity-70 [&::-webkit-slider-thumb]:h-4 [&::-webkit-slider-thumb]:w-4 [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:rounded-full [&::-webkit-slider-thumb]:border-2 [&::-webkit-slider-thumb]:border-[#59677D] [&::-webkit-slider-thumb]:bg-white [&::-webkit-slider-thumb]:shadow-md [&::-moz-range-thumb]:h-4 [&::-moz-range-thumb]:w-4 [&::-moz-range-thumb]:appearance-none [&::-moz-range-thumb]:rounded-full [&::-moz-range-thumb]:border-2 [&::-moz-range-thumb]:border-[#59677D] [&::-moz-range-thumb]:bg-white [&::-moz-range-thumb]:shadow-md"
            style={{
              background: `linear-gradient(to right, ${activeColor} ${activeSliderValue}%, rgba(223,229,237,0.6) ${activeSliderValue}%)`,
              accentColor: activeColor,
            }}
          />
        </div>

        {/* Consolidado de todos los propietarios del lado: cada uno en negrita y coloreado por
            participación; el Total queda deliberadamente neutro (gris, sin negrita) salvo error. */}
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
      )}
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
