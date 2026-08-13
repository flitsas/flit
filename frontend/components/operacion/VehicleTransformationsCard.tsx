'use client';

import { ArrowRight, Fuel, Layers, Palette, Wand2 } from 'lucide-react';
import type { FieldValue } from '@/lib/api/types/procedure-runtime';
import { cn } from '@/lib/utils';
import {
  VEHICLE_FUEL_CATALOG,
} from '@/lib/catalogs/vehicle-transformations';
import { getBodyworksForVehicleClass, normalizeVehicleClass } from '@/lib/catalogs/bodywork-by-class';
import { CatalogSearchSelect } from './CatalogSearchSelect';
import { VehicleColorSearchSelect } from './VehicleColorSearchSelect';

/**
 * Tarjeta "Transformaciones del vehículo" (A4/B4 · HU #10674 · ADR-0029 + P2/P3).
 *
 * Permite DECLARAR, durante MI/TR, un cambio de color, combustible o carrocería del vehículo
 * frente al dato del RUNT. El valor RUNT queda como snapshot inmutable (`*_runt`) y el efectivo
 * es el que viaja al FUR; el flag `cambio_*` marca la declaración.
 *
 * Cuatro estados de cada fila:
 *  1. RUNT sin cambio (toggle apagado) — se muestra el valor del RUNT en solo lectura.
 *  2. Declarando (toggle encendido) — se habilita el selector del nuevo valor.
 *  3. Cambio declarado — el efectivo difiere del RUNT: se pinta el diff `RUNT → NUEVO`.
 *  4. Bloqueado — `readOnly` (post-envío) o `saving` en curso deshabilitan los controles.
 *
 * WCAG 2.1 AA: cada control tiene label asociado, el grupo usa `role="group"` con
 * `aria-labelledby`, el resumen es `aria-live="polite"` y el foco usa el anillo de marca.
 */
export function VehicleTransformationsCard({
  fieldValues,
  readOnly,
  saving,
  onPatch,
  bare = false,
}: {
  fieldValues: FieldValue[];
  readOnly: boolean;
  saving: boolean;
  onPatch: (items: { fieldKey: string; valueText: string }[]) => Promise<void>;
  /**
   * Se monta dentro de un acordeón que ya pinta la tarjeta y el título. Quita el marco y la
   * cabecera propia; el texto de ayuda se conserva, porque explica qué se declara aquí y eso el
   * título del acordeón no lo dice.
   */
  bare?: boolean;
}) {
  const byKey = (key: string) =>
    fieldValues.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';

  // El snapshot RUNT es la referencia; si aún no existe (consulta antigua), se cae al efectivo.
  const colorRunt = byKey('vehicle_color_runt') || byKey('vehicle_color');
  const colorEff = byKey('vehicle_color');
  const colorActive = byKey('cambio_color') === 'true' || isChanged(colorRunt, colorEff);

  const fuelRunt = byKey('vehicle_fuel_runt') || byKey('vehicle_fuel');
  const fuelEff = byKey('vehicle_fuel');
  const fuelActive = byKey('cambio_combustible') === 'true' || isChanged(fuelRunt, fuelEff);

  // Carrocería (P2/P3): opciones filtradas por clase RUNT del vehículo.
  const bodyworkRunt = byKey('vehicle_body_type_runt') || byKey('vehicle_body_type');
  const bodyworkEff = byKey('vehicle_body_type');
  const bodyworkActive = byKey('cambio_carroceria') === 'true' || isChanged(bodyworkRunt, bodyworkEff);
  const vehicleClass = normalizeVehicleClass(byKey('vehicle_class'));
  const bodyworkOptions = getBodyworksForVehicleClass(vehicleClass).map((o) => o.name);
  const bodyworkEmptyMessage = vehicleClass
    ? `No hay carrocerías disponibles para la clase ${vehicleClass}`
    : 'Consulta el RUNT para obtener la clase';

  // Antes de consultar el RUNT no hay datos del vehículo → no se renderiza (paridad con VehicleDataCard).
  const hasVehicle = [
    byKey('plate'),
    byKey('vin'),
    byKey('vehicle_brand'),
    colorRunt,
    fuelRunt,
  ].some((v) => v !== '');
  if (!hasVehicle) return null;

  const disabled = readOnly || saving;

  const setColor = (on: boolean, value?: string) =>
    on
      ? onPatch([
          { fieldKey: 'cambio_color', valueText: 'true' },
          { fieldKey: 'vehicle_color', valueText: value ?? colorEff ?? colorRunt },
        ])
      : onPatch([
          { fieldKey: 'cambio_color', valueText: 'false' },
          // Al desactivar se restaura el efectivo al snapshot RUNT (no queda un cambio implícito).
          { fieldKey: 'vehicle_color', valueText: colorRunt },
        ]);

  const setFuel = (on: boolean, value?: string) =>
    on
      ? onPatch([
          { fieldKey: 'cambio_combustible', valueText: 'true' },
          { fieldKey: 'vehicle_fuel', valueText: value ?? fuelEff ?? fuelRunt },
        ])
      : onPatch([
          { fieldKey: 'cambio_combustible', valueText: 'false' },
          { fieldKey: 'vehicle_fuel', valueText: fuelRunt },
        ]);

  const setBodywork = (on: boolean, value?: string) =>
    on
      ? onPatch([
          { fieldKey: 'cambio_carroceria', valueText: 'true' },
          { fieldKey: 'vehicle_body_type', valueText: value ?? bodyworkEff ?? bodyworkRunt },
        ])
      : onPatch([
          { fieldKey: 'cambio_carroceria', valueText: 'false' },
          { fieldKey: 'vehicle_body_type', valueText: bodyworkRunt },
        ]);

  // El resumen refleja lo que se imprime en el FUR: solo el valor NUEVO.
  const changes = summarizeDeclaredTransformations(fieldValues);

  return (
    <section
      aria-labelledby="veh-transf-title"
      className={
        bare ? 'space-y-3' : 'space-y-3 rounded-2xl border bg-white p-4 dark:bg-[#0B0F14]'
      }
    >
      {/* Embebido, el título vive en la cabecera del acordeón. Se conserva un rótulo oculto porque
          el `role="group"` de abajo lo referencia por `aria-labelledby`. */}
      {bare ? (
        <p id="veh-transf-title" className="sr-only">
          Transformaciones del vehículo
        </p>
      ) : (
        <div className="flex items-center gap-2">
          <span
            aria-hidden="true"
            className="grid h-7 w-7 place-items-center rounded-lg"
            style={{ background: 'rgba(85,126,255,0.10)' }}
          >
            <Wand2 className="h-4 w-4" style={{ color: '#557EFF' }} />
          </span>
          <p id="veh-transf-title" className="text-xs font-semibold opacity-80">
            Transformaciones del vehículo
          </p>
        </div>
      )}
      <p className={`text-xs opacity-55 ${bare ? '' : '-mt-1'}`}>
        Declara un cambio de color, combustible o carrocería frente al RUNT. Se registrará en el
        FUR. La carrocería puede requerir factura como documento de soporte.
      </p>

      <div role="group" aria-labelledby="veh-transf-title" className="space-y-2.5">
        <TransformationRow
          idPrefix="transf-color"
          icon={Palette}
          label="Color"
          runtValue={colorRunt}
          effectiveValue={colorEff}
          active={colorActive}
          colorCatalog
          disabled={disabled}
          onToggle={(on) => void setColor(on)}
          onSelect={(v) => void setColor(true, v)}
        />
        <TransformationRow
          idPrefix="transf-fuel"
          icon={Fuel}
          label="Combustible"
          runtValue={fuelRunt}
          effectiveValue={fuelEff}
          active={fuelActive}
          options={VEHICLE_FUEL_CATALOG}
          disabled={disabled}
          onToggle={(on) => void setFuel(on)}
          onSelect={(v) => void setFuel(true, v)}
        />
        <TransformationRow
          idPrefix="transf-bodywork"
          icon={Layers}
          label="Carrocería"
          toggleLabel="Cambió la carrocería"
          selectLabel="Nueva carrocería"
          runtValue={bodyworkRunt}
          effectiveValue={bodyworkEff}
          active={bodyworkActive}
          options={bodyworkOptions}
          emptyMessage={bodyworkEmptyMessage}
          disabled={disabled}
          onToggle={(on) => void setBodywork(on)}
          onSelect={(v) => void setBodywork(true, v)}
        />
      </div>

      <p
        aria-live="polite"
        className={cn(
          'rounded-xl px-3 py-2 text-xs',
          changes.length > 0
            ? 'font-medium'
            : 'opacity-55',
        )}
        style={
          changes.length > 0
            ? { background: 'rgba(85,126,255,0.08)', color: '#557EFF' }
            : undefined
        }
      >
        {changes.length > 0
          ? `Se registrará en el FUR — ${changes.join(' · ')}`
          : 'Sin transformaciones declaradas: se registrará el dato del RUNT.'}
      </p>
    </section>
  );
}

function TransformationRow({
  idPrefix,
  icon: Icon,
  label,
  toggleLabel,
  selectLabel,
  runtValue,
  effectiveValue,
  active,
  options = [],
  colorCatalog = false,
  emptyMessage,
  disabled,
  onToggle,
  onSelect,
}: {
  idPrefix: string;
  icon: typeof Palette;
  label: string;
  /** Etiqueta del checkbox de toggle. Por defecto: `"Cambió el ${label.toLowerCase()}"`. */
  toggleLabel?: string;
  /** Etiqueta del selector cuando está activo. Por defecto: `"Nuevo ${label.toLowerCase()}"`. */
  selectLabel?: string;
  runtValue: string;
  effectiveValue: string;
  active: boolean;
  options?: readonly string[];
  /** Usa el catálogo persistido de colores (API) en lugar de `options` locales. */
  colorCatalog?: boolean;
  /** Mensaje cuando `active && options.length === 0` (clase desconocida o sin clase). */
  emptyMessage?: string;
  disabled: boolean;
  onToggle: (on: boolean) => void;
  onSelect: (value: string) => void;
}) {
  const toggleId = `${idPrefix}-toggle`;
  const selectId = `${idPrefix}-select`;
  const hintId = `${idPrefix}-hint`;
  const changed = active && isChanged(runtValue, effectiveValue);
  const computedToggleLabel = toggleLabel ?? `Cambió el ${label.toLowerCase()}`;
  const computedSelectLabel = selectLabel ?? `Nuevo ${label.toLowerCase()}`;
  // El efectivo siempre debe ser una opción válida del selector aunque el catálogo placeholder
  // no lo incluya (p. ej. un color del RUNT fuera de la lista).
  const selectOptions = mergeOption(options, effectiveValue || runtValue);

  return (
    <div className="rounded-xl border p-3 space-y-2">
      <div className="flex items-start gap-2.5">
        <input
          id={toggleId}
          type="checkbox"
          checked={active}
          onChange={(e) => onToggle(e.target.checked)}
          disabled={disabled}
          aria-describedby={hintId}
          className="mt-0.5 h-4 w-4 shrink-0 accent-[#557EFF] disabled:opacity-60"
        />
        <div className="min-w-0 text-xs">
          <label
            htmlFor={toggleId}
            className="flex cursor-pointer items-center gap-1.5 font-semibold"
          >
            <Icon aria-hidden="true" className="h-3.5 w-3.5 opacity-70" />
            {computedToggleLabel}
          </label>
          <span id={hintId} className="mt-0.5 block opacity-55">
            {`RUNT: ${up(runtValue) || '—'}`}
          </span>
        </div>
      </div>

      {active && (
        <div className="pl-6 space-y-1.5">
          {colorCatalog ? (
            <VehicleColorSearchSelect
              id={selectId}
              label={computedSelectLabel}
              value={effectiveValue}
              disabled={disabled}
              placeholder={`Buscar ${label.toLowerCase()}…`}
              onChange={onSelect}
            />
          ) : options.length === 0 && emptyMessage ? (
            <p className="text-xs opacity-55">{emptyMessage}</p>
          ) : (
            <CatalogSearchSelect
              id={selectId}
              label={computedSelectLabel}
              value={effectiveValue}
              options={selectOptions}
              disabled={disabled}
              placeholder={`Buscar ${label.toLowerCase()}…`}
              onChange={onSelect}
            />
          )}
          {changed && (
            <p className="flex items-center gap-1.5 text-xs" style={{ color: '#557EFF' }}>
              <span className="opacity-70">{up(runtValue)}</span>
              <ArrowRight aria-hidden="true" className="h-3 w-3" />
              <span className="font-semibold">{up(effectiveValue)}</span>
            </p>
          )}
        </div>
      )}
    </div>
  );
}

function isChanged(a: string, b: string): boolean {
  return (
    a.trim() !== '' &&
    b.trim() !== '' &&
    a.trim().toUpperCase() !== b.trim().toUpperCase()
  );
}

function up(value: string): string {
  return value.trim().toUpperCase();
}

/**
 * Transformaciones declaradas (color / combustible / carrocería) listas para el FUR
 * y el resumen del trámite. Vacío si no hay cambios frente al RUNT.
 */
export function summarizeDeclaredTransformations(fieldValues: FieldValue[]): string[] {
  const byKey = (key: string) =>
    fieldValues.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';

  const colorRunt = byKey('vehicle_color_runt') || byKey('vehicle_color');
  const colorEff = byKey('vehicle_color');
  const colorActive = byKey('cambio_color') === 'true' || isChanged(colorRunt, colorEff);

  const fuelRunt = byKey('vehicle_fuel_runt') || byKey('vehicle_fuel');
  const fuelEff = byKey('vehicle_fuel');
  const fuelActive = byKey('cambio_combustible') === 'true' || isChanged(fuelRunt, fuelEff);

  const bodyworkRunt = byKey('vehicle_body_type_runt') || byKey('vehicle_body_type');
  const bodyworkEff = byKey('vehicle_body_type');
  const bodyworkActive =
    byKey('cambio_carroceria') === 'true' || isChanged(bodyworkRunt, bodyworkEff);

  const changes: string[] = [];
  if (colorActive && isChanged(colorRunt, colorEff)) changes.push(`Color: ${up(colorEff)}`);
  if (fuelActive && isChanged(fuelRunt, fuelEff))
    changes.push(`Combustible: ${up(fuelEff)}`);
  if (bodyworkActive && isChanged(bodyworkRunt, bodyworkEff))
    changes.push(`Carrocería: ${up(bodyworkEff)}`);
  return changes;
}

/** Garantiza que `current` esté en la lista de opciones (al inicio) sin duplicar. */
function mergeOption(options: readonly string[], current: string): string[] {
  const cur = current.trim();
  if (cur === '') return [...options];
  const exists = options.some((o) => o.toUpperCase() === cur.toUpperCase());
  return exists ? [...options] : [cur.toUpperCase(), ...options];
}
