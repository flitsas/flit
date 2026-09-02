'use client';

import { Plus, X } from 'lucide-react';
import { WIZARD_LABEL } from './wizard-field-styles';

/**
 * Múltiple Propietario (ADR-0053, docs/design/MULTIPLE-PROPIETARIO-diseno-tecnico.md §8 "PR 2 —
 * Frontend"). Dos piezas de presentación separadas — antes vivían juntas en un solo componente,
 * pero la maqueta las ubica en puntos distintos de la tarjeta del actor:
 *
 * - `OwnershipTabsBar`: fila de pestañas (etiqueta de copropiedad + píldoras + botón "+"). Va
 *   ARRIBA de la tarjeta, junto al inicio de "Datos del comprador/vendedor". Se renderiza SIEMPRE,
 *   con 1..4 propietarios.
 * - `OwnershipPercentagePanel`: bloque "Porcentaje de propiedad" (slider + casilla + consolidado).
 *   Va al FINAL de la tarjeta, después de Datos de contacto (y del representante legal si aplica) —
 *   coincide con la maqueta original. Solo se muestra si el lado tiene 2 o más propietarios EN ESTE
 *   MOMENTO — no es una bandera histórica: si el lado vuelve a 1 (se agregó un segundo y se quitó),
 *   el bloque se oculta otra vez. El caller (`ActorsForm.tsx`) decide si lo monta comparando
 *   `items.length >= 2`, no con estado persistido.
 *
 * Aunque viven en puntos distintos del DOM, siguen relacionados por accesibilidad: cada pestaña
 * activa controla (`aria-controls`) el mismo `id` que trae el panel (`aria-labelledby` apunta de
 * vuelta a la pestaña activa) — ambos IDs se derivan del mismo `idPrefix`, que el caller debe pasar
 * IGUAL a los dos componentes.
 *
 * Fidelidad visual: pestañas en píldora, color por participación (`colorDeParticipacion`) y bloque
 * de porcentaje replican la imagen de referencia del usuario, verificada contra `Propietarios.tsx`
 * del prototipo FLIT (repo `flit-2.0`, `src/components/atom/modules/Propietarios.tsx`) — adaptados
 * a los tokens de `flit` (sin copiar hex del prototipo: los colores de participación usan las
 * variables de `StatusBadge` ya definidas en `globals.css`). La LÓGICA es la de
 * `ownership-share.ts`, sin relación con la del prototipo (que reparte con el residuo en el
 * ÚLTIMO propietario, en enteros, y sin bloqueo — ninguna de las tres aplica aquí).
 *
 * Ambos son componentes de presentación puros: no conocen `ActorsForm.tsx` ni el estado de
 * identidad de cada actor — solo reciben la lista de pestañas de ESTE lado (`items`, ya resuelta
 * por índice real en el array `actors`) y notifican intención
 * (`onSelectTab`/`onAdd`/`onRemove`/`onPercentageChange`). Toda la lógica de reparto
 * (auto-absorción del solidario, redistribución al eliminar, validación de suma=100) vive en
 * `frontend/lib/tramites/ownership-share.ts`, testeada aparte sin RTL.
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

/** `${idPrefix}-ownership-panel` — el mismo id que ambos componentes usan para enlazarse. */
function ownershipPanelId(idPrefix: string): string {
  return `${idPrefix}-ownership-panel`;
}

const ADD_BUTTON_CLASS =
  'grid h-9 w-9 shrink-0 place-items-center rounded-full text-white transition ' +
  'disabled:cursor-not-allowed disabled:opacity-40 focus:outline-none focus-visible:ring-2 ' +
  'focus-visible:ring-offset-2 focus-visible:ring-[#557EFF]';

export interface OwnershipTabsBarProps {
  /** Pestañas de este lado, en orden de ordinal. */
  items: OwnershipShareItem[];
  /** Índice real (en `actors`) de la pestaña activa. */
  activeIndex: number;
  onSelectTab: (index: number) => void;
  onAdd: () => void;
  onRemove: (index: number) => void;
  /** Ya se alcanzó el máximo de 4 — el botón "+" se deshabilita. */
  maxReached: boolean;
  readOnly?: boolean;
  /** Prefijo único para ids de accesibilidad — DEBE coincidir con el de `OwnershipPercentagePanel`. */
  idPrefix: string;
  /** Rótulo del lado sin ordinal (p. ej. «Comprador»), para el aria-label de la fila y el botón "+". */
  sideLabel: string;
  /** Hay panel de porcentaje montado en este render (2+ propietarios) — enlaza `aria-controls`. */
  hasPercentagePanel: boolean;
}

/**
 * Fila de pestañas: SIEMPRE visible, con 1 a 4 propietarios. Con un solo propietario es una única
 * píldora activa al 100%, sin `×` (el ordinal=1 no se elimina) — el punto de entrada para agregar
 * copropietarios (botón "+"), no un disparador aparte.
 */
export function OwnershipTabsBar({
  items,
  activeIndex,
  onSelectTab,
  onAdd,
  onRemove,
  maxReached,
  readOnly = false,
  idPrefix,
  sideLabel,
  hasPercentagePanel,
}: OwnershipTabsBarProps) {
  const panelId = ownershipPanelId(idPrefix);
  const allPct = items.map((i) => i.percentage);

  return (
    // Dos renglones dentro de la tarjeta (maqueta de referencia): la etiqueta de copropiedad
    // arriba, alineada a la derecha (en la maqueta acompaña al título de la tarjeta, en la MISMA
    // línea — aquí no hay título propio: `OwnershipTabsBar` se monta COMO PRIMER elemento del
    // cuerpo del `WizardAccordion`, así que su propio renglón alineado a la derecha es el sustituto
    // más fiel sin invadir la cabecera del acordeón — ni desplazar el badge de Persona
    // Natural/Jurídica que ya vive ahí, ni arriesgar que el título se comprima en tarjetas
    // angostas). Las píldoras van SIEMPRE debajo, en su propio renglón, alineadas a la izquierda.
    <div className="mb-4">
      <div className="flex justify-end">
        <span className="text-xs opacity-70 whitespace-nowrap">
          Copropiedad: hasta <span className="font-semibold" style={{ color: '#557EFF' }}>4</span> propietarios
        </span>
      </div>
      <div
        role="tablist"
        aria-label={`Copropietarios — ${sideLabel}`}
        className="mt-3 flex flex-wrap items-center gap-1.5"
      >
        {items.map((item) => {
          const isActive = item.index === activeIndex;
          const showRemove = isActive && item.removable && !readOnly;
          // La píldora entera es el control: el borde, el fondo, la altura y el padding viven en
          // el propio `<button role="tab">`, no en un `<div>` envolvente sin `onClick` — así toda
          // su superficie (no solo el texto) cambia de pestaña al clic. La "×", cuando aplica, es
          // un `<button>` HERMANO (nunca anidado dentro de otro botón) que completa visualmente la
          // píldora: mismo borde/fondo/color, sin borde compartido en el punto de unión
          // (`border-r-0`/`border-l-0`) para que se vea como una sola forma continua.
          const pillStyle = isActive
            ? { borderColor: '#557EFF', color: '#557EFF', background: 'rgba(85,126,255,0.08)' }
            : { borderColor: '#DFE5ED', color: '#59677D', background: '#fff' };
          return (
            <div key={item.index} className="flex items-center">
              <button
                type="button"
                role="tab"
                id={`${idPrefix}-tab-${item.index}`}
                aria-selected={isActive}
                aria-controls={hasPercentagePanel ? panelId : undefined}
                onClick={() => onSelectTab(item.index)}
                className={`flex h-9 items-center gap-1.5 border pl-3 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF] ${
                  showRemove ? 'rounded-l-full border-r-0 pr-2' : 'rounded-full pr-3'
                }`}
                style={pillStyle}
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
                  className="grid h-9 place-items-center rounded-r-full border border-l-0 pl-1 pr-3 opacity-70 transition hover:opacity-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[#557EFF]"
                  style={pillStyle}
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
    </div>
  );
}

export interface OwnershipPercentagePanelProps {
  /** Pestañas de este lado — el panel siempre pinta TODAS (consolidado) + la activa (control). */
  items: OwnershipShareItem[];
  /** Índice real (en `actors`) de la pestaña activa. */
  activeIndex: number;
  onPercentageChange: (index: number, value: number) => void;
  readOnly?: boolean;
  /** Prefijo único para ids de accesibilidad — DEBE coincidir con el de `OwnershipTabsBar`. */
  idPrefix: string;
  /** Muestra los mensajes de bloqueo (se activa junto con `showErrors` del formulario). */
  showErrors?: boolean;
}

/**
 * Bloque "Porcentaje de propiedad": el CALLER decide cuándo montarlo (2+ propietarios en este
 * momento, sin memoria histórica — con un solo propietario NUNCA se monta, así se haya llegado a
 * tener más antes). Va al final de la tarjeta del actor, después de todos sus datos.
 */
export function OwnershipPercentagePanel({
  items,
  activeIndex,
  onPercentageChange,
  readOnly = false,
  idPrefix,
  showErrors = false,
}: OwnershipPercentagePanelProps) {
  const active = items.find((i) => i.index === activeIndex) ?? items[0];
  if (!active) return null;

  const allPct = items.map((i) => i.percentage);
  const total = allPct.reduce((s, p) => s + p, 0);
  const sumError = Math.abs(total - 100) > 0.005;
  const zeroError = items.some((i) => i.percentage <= 0);
  const panelId = ownershipPanelId(idPrefix);
  const activeColor = colorDeParticipacion(active.percentage, allPct);
  const activeSliderValue = clampDisplay(active.percentage);

  return (
    // Mismo tratamiento que la caja anidada "Datos de contacto" de ActorsForm.tsx (borde sólido
    // #DFE5ED, fondo blanco/navy — NO un tinte gris translúcido): son dos secciones hermanas de la
    // misma tarjeta, no una tenue y otra sólida. Corrección explícita del usuario sobre un ajuste
    // de una ronda anterior que se pasó de sutil.
    <div
      id={panelId}
      role="tabpanel"
      aria-labelledby={`${idPrefix}-tab-${active.index}`}
      className="mt-4 rounded-xl border bg-white p-4 dark:bg-[#162744]"
      style={{ borderColor: '#DFE5ED' }}
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
