'use client';

import { useId, useState } from 'react';
import { ArrowRight, Fuel, Layers, Palette, X } from 'lucide-react';
import type { FieldValue } from '@/lib/api/types/procedure-runtime';
import { cn } from '@/lib/utils';
import {
  VEHICLE_FUEL_CATALOG,
} from '@/lib/catalogs/vehicle-transformations';
import { getBodyworksForVehicleClass, normalizeVehicleClass } from '@/lib/catalogs/bodywork-by-class';
import { CatalogSearchSelect } from './CatalogSearchSelect';
import { VehicleColorSearchSelect } from './VehicleColorSearchSelect';
import { WizardCardHeader } from './wizard-atoms';
import { WizardModal } from './WizardModal';
import { WIZARD_INPUT } from './wizard-field-styles';

/**
 * Tarjeta "Trámites Simultáneos (Opcional)" (A4/B4 · HU #10674 · ADR-0029 + P2/P3 — gramática del
 * repo de diseño: `MatriculaInicial.tsx`, Step 3 «Documentos y requisitos del trámite»).
 *
 * Mismo dato y misma regla de negocio que antes tenía el acordeón "Transformaciones y condiciones
 * del trámite": DECLARAR, durante MI/TR, un cambio de color, combustible o carrocería del vehículo
 * frente al dato del RUNT. Lo que cambia es la gramática — un `<select>` que agrega chips, calcado
 * de la tarjeta que la propuesta pone en el paso de requisitos — en vez de tres casillas de
 * verificación.
 *
 * Con una diferencia deliberada frente a la propuesta: sus chips son decorativos (un trámite
 * simultáneo ahí solo habilita un documento soporte). Aquí el VALOR hace falta —es lo que viaja al
 * FUR—, así que cada subtrámite seleccionado abre debajo su propio campo (el color nuevo, el
 * combustible, la carrocería) y no se pierde nunca.
 *
 * El valor RUNT queda como snapshot inmutable (`*_runt`) y el efectivo es el que viaja al FUR; el
 * flag `cambio_*` marca la declaración — EXACTAMENTE los mismos `field_values` de siempre
 * (`cambio_color`/`vehicle_color`, `cambio_combustible`/`vehicle_fuel`,
 * `cambio_carroceria`/`vehicle_body_type`), con el mismo PATCH atómico de bandera+valor. Marcar un
 * subtrámite equivale a poner su bandera en `true`; quitarlo (con confirmación), a `false` — y
 * restaura el efectivo al snapshot RUNT, igual que hacía el toggle que reemplaza.
 *
 * Dos exclusiones deliberadas del catálogo de la propuesta:
 *  - "Inscribir Prenda" no está: en FLIT tiene tarjeta propia con decisiones de gestión y su gate
 *    (`PrendaForm`, Feature #10585). Ponerla aquí duplicaría la misma decisión en dos sitios.
 *  - "Blindaje" no está: no existe ni bandera, ni documento, ni regla de backend para él en FLIT.
 *
 * WCAG 2.1 AA: el `<select>` de agregar y cada campo de valor tienen su label asociado; los chips
 * viven en una región `aria-live`; quitar un subtrámite pide confirmación (`WizardModal`, con
 * trampa de foco) antes de borrar su bandera y su valor.
 */
export function VehicleTransformationsCard({
  fieldValues,
  readOnly,
  saving,
  onPatch,
}: {
  fieldValues: FieldValue[];
  readOnly: boolean;
  saving: boolean;
  onPatch: (items: { fieldKey: string; valueText: string }[]) => Promise<void>;
}) {
  // Hooks primero, SIEMPRE — el `return null` de abajo (sin datos de vehículo) es condicional y
  // React exige que el orden de hooks no dependa de una rama.
  const headingId = useId();
  const [pendienteQuitar, setPendienteQuitar] = useState<SubtramiteKey | null>(null);

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
          // Al quitar se restaura el efectivo al snapshot RUNT (no queda un cambio implícito).
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

  // El mapeo uno a uno del catálogo de la propuesta: mismas tres etiquetas, mismas tres banderas.
  const subtramites: SubtramiteItem[] = [
    {
      key: 'color',
      optionLabel: 'Cambio de Color',
      valueLabel: 'Nuevo color',
      icon: Palette,
      runtValue: colorRunt,
      effectiveValue: colorEff,
      active: colorActive,
      colorCatalog: true,
      onToggle: (on) => void setColor(on),
      onSelect: (v) => void setColor(true, v),
    },
    {
      key: 'combustible',
      optionLabel: 'Conversiones de Combustible',
      valueLabel: 'Nuevo combustible',
      icon: Fuel,
      runtValue: fuelRunt,
      effectiveValue: fuelEff,
      active: fuelActive,
      options: VEHICLE_FUEL_CATALOG,
      onToggle: (on) => void setFuel(on),
      onSelect: (v) => void setFuel(true, v),
    },
    {
      key: 'carroceria',
      optionLabel: 'Cambio de Carrocería',
      valueLabel: 'Nueva carrocería',
      icon: Layers,
      runtValue: bodyworkRunt,
      effectiveValue: bodyworkEff,
      active: bodyworkActive,
      options: bodyworkOptions,
      emptyMessage: bodyworkEmptyMessage,
      onToggle: (on) => void setBodywork(on),
      onSelect: (v) => void setBodywork(true, v),
    },
  ];

  const seleccionados = subtramites.filter((s) => s.active);
  const disponibles = subtramites.filter((s) => !s.active);
  const itemAQuitar = subtramites.find((s) => s.key === pendienteQuitar) ?? null;

  const handleAgregar = (key: string) => {
    subtramites.find((s) => s.key === key)?.onToggle(true);
  };

  const confirmarQuitar = () => {
    itemAQuitar?.onToggle(false);
    setPendienteQuitar(null);
  };

  // El resumen refleja lo que se imprime en el FUR: solo el valor NUEVO.
  const changes = summarizeDeclaredTransformations(fieldValues);

  return (
    <section
      aria-labelledby={headingId}
      className="space-y-3 rounded-2xl border bg-white p-4 dark:bg-[#162744]"
    >
      <WizardCardHeader
        id={headingId}
        title="Trámites Simultáneos (Opcional)"
        subtitle="Al seleccionar un trámite simultáneo se habilita de inmediato su documento soporte. Se declara frente al RUNT y se registra en el FUR."
      />

      <div>
        <label htmlFor="tramite-simultaneo-agregar" className="mb-1.5 block text-xs font-semibold">
          Agregar trámite simultáneo
        </label>
        <select
          id="tramite-simultaneo-agregar"
          value=""
          onChange={(e) => {
            if (e.target.value) handleAgregar(e.target.value);
          }}
          disabled={disabled || disponibles.length === 0}
          className={`${WIZARD_INPUT} disabled:opacity-60`}
        >
          <option value="">
            {disponibles.length === 0 ? 'No hay más trámites simultáneos disponibles' : 'Selecciona…'}
          </option>
          {disponibles.map((s) => (
            <option key={s.key} value={s.key}>
              {s.optionLabel}
            </option>
          ))}
        </select>
      </div>

      {/* Chips de los seleccionados. El fondo `#EEF5FF`/borde `#DFE5ED` es el mismo par que usa
          `WizardSegmented` para la pista del control: hunde el bloque lo justo sobre la tarjeta
          blanca, sin inventar un gris nuevo. */}
      <div
        className="flex min-h-11 flex-wrap items-center gap-2 rounded-xl border p-2"
        style={{ borderColor: '#DFE5ED', background: '#EEF5FF' }}
        aria-live="polite"
      >
        {seleccionados.length === 0 ? (
          <span className="px-1 text-xs opacity-70">Sin trámites simultáneos seleccionados</span>
        ) : (
          seleccionados.map((s) => (
            <span
              key={s.key}
              className="inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-medium"
              style={{
                background: 'var(--badge-info-bg)',
                color: 'var(--badge-info-fg)',
                borderColor: 'var(--badge-info-border)',
              }}
            >
              {s.optionLabel}
              {!readOnly && (
                <button
                  type="button"
                  onClick={() => setPendienteQuitar(s.key)}
                  disabled={disabled}
                  aria-label={`Quitar ${s.optionLabel}`}
                  className="rounded-full opacity-70 transition hover:opacity-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:cursor-not-allowed"
                >
                  <X className="h-3 w-3" aria-hidden="true" />
                </button>
              )}
            </span>
          ))
        )}
      </div>

      {/* Bajo los chips, un bloque por subtrámite seleccionado con su campo de valor. La propuesta
          no lo tiene —sus chips son decorativos—, pero FLIT necesita capturar el valor efectivo. */}
      {seleccionados.length > 0 && (
        <div className="space-y-2.5">
          {seleccionados.map((s) => (
            <SubtramiteValueBlock key={s.key} item={s} disabled={disabled} />
          ))}
        </div>
      )}

      <p
        aria-live="polite"
        className={cn('rounded-xl px-3 py-2 text-xs', changes.length > 0 ? 'font-medium' : 'opacity-70')}
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

      {itemAQuitar && (
        <WizardModal title="Eliminar trámite simultáneo" onClose={() => setPendienteQuitar(null)}>
          <p className="text-xs leading-relaxed opacity-80">
            ¿Estás seguro de eliminar «{itemAQuitar.optionLabel}»? Se removerán los requisitos
            asociados y se restaurará el dato del RUNT.
          </p>
          <div className="mt-6 flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setPendienteQuitar(null)}
              className="rounded-xl border px-4 py-2 text-xs font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={confirmarQuitar}
              className="rounded-xl px-5 py-2 text-xs font-semibold text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-[#FF4E00] focus-visible:ring-offset-2"
              style={{ background: '#FF4E00' }}
            >
              Sí, eliminar
            </button>
          </div>
        </WizardModal>
      )}
    </section>
  );
}

type SubtramiteKey = 'color' | 'combustible' | 'carroceria';

interface SubtramiteItem {
  key: SubtramiteKey;
  /** Texto del `<option>` del selector y del chip — igual al catálogo de la propuesta. */
  optionLabel: string;
  /** Label del campo de valor bajo el chip (p. ej. "Nuevo color"). */
  valueLabel: string;
  icon: typeof Palette;
  runtValue: string;
  effectiveValue: string;
  active: boolean;
  options?: readonly string[];
  /** Usa el catálogo persistido de colores (API) en lugar de `options` locales. */
  colorCatalog?: boolean;
  /** Mensaje cuando `options` viene vacío (clase de carrocería desconocida o sin clase). */
  emptyMessage?: string;
  onToggle: (on: boolean) => void;
  onSelect: (value: string) => void;
}

/** Bloque de valor de un subtrámite seleccionado: RUNT, selector del nuevo valor y diff si cambió. */
function SubtramiteValueBlock({ item, disabled }: { item: SubtramiteItem; disabled: boolean }) {
  const Icon = item.icon;
  const selectId = `tramite-simultaneo-${item.key}-valor`;
  const hintId = `${selectId}-hint`;
  const changed = isChanged(item.runtValue, item.effectiveValue);
  // El efectivo siempre debe ser una opción válida del selector aunque el catálogo placeholder no
  // lo incluya (p. ej. un color del RUNT fuera de la lista).
  const selectOptions = mergeOption(item.options ?? [], item.effectiveValue || item.runtValue);

  return (
    <div className="rounded-xl border p-3 space-y-1.5">
      <div className="flex items-center gap-1.5 text-xs font-semibold">
        <Icon aria-hidden="true" className="h-3.5 w-3.5 opacity-70" />
        {item.optionLabel}
      </div>
      <span id={hintId} className="block text-xs opacity-70">
        {`RUNT: ${up(item.runtValue) || '—'}`}
      </span>

      {item.colorCatalog ? (
        <VehicleColorSearchSelect
          id={selectId}
          label={item.valueLabel}
          value={item.effectiveValue}
          disabled={disabled}
          placeholder="Buscar color…"
          onChange={item.onSelect}
        />
      ) : (item.options ?? []).length === 0 && item.emptyMessage ? (
        <p className="text-xs opacity-70">{item.emptyMessage}</p>
      ) : (
        <CatalogSearchSelect
          id={selectId}
          label={item.valueLabel}
          value={item.effectiveValue}
          options={selectOptions}
          disabled={disabled}
          placeholder={`Buscar ${item.valueLabel.toLowerCase()}…`}
          onChange={item.onSelect}
        />
      )}

      {changed && (
        <p className="flex items-center gap-1.5 text-xs" style={{ color: '#557EFF' }}>
          <span className="opacity-70">{up(item.runtValue)}</span>
          <ArrowRight aria-hidden="true" className="h-3 w-3" />
          <span className="font-semibold">{up(item.effectiveValue)}</span>
        </p>
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
 *
 * Sin cambios de contrato ni de lógica frente a la versión anterior de este archivo: sigue leyendo
 * exactamente las mismas seis claves (`cambio_*` + `vehicle_*` efectivo/RUNT). La usa
 * `FirmaFurStep` para el resumen del FUR — no depende de la gramática de esta tarjeta.
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
