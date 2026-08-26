'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { ArrowRight, Paperclip, X } from 'lucide-react';
import type { FieldValue, ProcedureAttachment } from '@/lib/api/types/procedure-runtime';
import { cn } from '@/lib/utils';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  VEHICLE_FUEL_CATALOG,
} from '@/lib/catalogs/vehicle-transformations';
import { getBodyworksForVehicleClass, normalizeVehicleClass } from '@/lib/catalogs/bodywork-by-class';
import { CatalogSearchSelect } from './CatalogSearchSelect';
import { VehicleColorSearchSelect } from './VehicleColorSearchSelect';
import { WizardCardHeader } from './wizard-atoms';
import { WizardModal } from './WizardModal';
import { WIZARD_INPUT, WIZARD_LABEL } from './wizard-field-styles';

/** DocTipo de soporte por subtrámite (whitelist AttachmentRules + RF33 factura_carroceria). */
const DOC_TIPO_BY_KEY: Record<SubtramiteKey, string> = {
  color: 'soporte_cambio_color',
  combustible: 'soporte_conversion_combustible',
  carroceria: 'factura_carroceria',
};

const SOPORTE_HINT: Record<SubtramiteKey, string> = {
  color: 'Soporte de cambio de color',
  combustible: 'Soporte de conversión de combustible',
  carroceria: 'Factura de carrocería',
};

const SUBTITULO_SIMULTANEOS =
  'Declara un cambio de color, combustible o carrocería frente al RUNT.';

/** Copy del modo tipo base: el cambio no se «declara además», es el trámite. */
const subtituloTipoBase = (item: SubtramiteItem) =>
  `${item.valueLabel} y su soporte: es el trámite que se está radicando.`;

/**
 * Tarjeta "Trámites Simultáneos — Transformaciones del Vehículo" (prototipo Lovable Traspaso).
 *
 * - Selector «Agregar trámite simultáneo» para activar color / combustible / carrocería.
 * - Por cada uno activo: card DocSlot (adjunto + * Obligatorio + quitar) **y** el select del
 *   valor nuevo (FLIT necesita el valor para el FUR — no son chips decorativos).
 *
 * Con {@link soloSubtramite} la tarjeta deja de ser un acumulador y pasa a ser la captura del
 * atributo que el trámite cambia por definición (familia OTROS): un único subtrámite, siempre
 * activo, sin selector de «agregar» y sin poder quitarlo. Es la misma captura —valor nuevo +
 * soporte— porque el FUR necesita exactamente lo mismo; lo que desaparece es la acumulación.
 */
export function VehicleTransformationsCard({
  fieldValues,
  readOnly,
  saving,
  onPatch,
  hideHeader = false,
  instanceId = null,
  onDocumentsChanged,
  onCompletenessChange,
  soloSubtramite = null,
}: {
  fieldValues: FieldValue[];
  readOnly: boolean;
  saving: boolean;
  onPatch: (items: { fieldKey: string; valueText: string }[]) => Promise<void>;
  /** Omite WizardCardHeader interno — útil cuando la tarjeta vive dentro de un WizardAccordion. */
  hideHeader?: boolean;
  /** Con instancia: permite subir/borrar el documento soporte de cada subtrámite. */
  instanceId?: string | null;
  onDocumentsChanged?: () => void;
  /** Notifica si todos los subtrámites activos tienen valor nuevo + adjunto (gate Continuar). */
  onCompletenessChange?: (complete: boolean) => void;
  /**
   * Modo tipo base (familia OTROS): captura SOLO este atributo, siempre activo y no removible.
   * `null` (default) = acumulador de simultáneos, el comportamiento de matrícula y traspaso.
   */
  soloSubtramite?: SubtramiteKey | null;
}) {
  const headingId = useId();
  const [pendienteQuitar, setPendienteQuitar] = useState<SubtramiteKey | null>(null);
  const [attachments, setAttachments] = useState<ProcedureAttachment[]>([]);
  const [uploadingKey, setUploadingKey] = useState<SubtramiteKey | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);

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
  const bodyworkActive = byKey('cambio_carroceria') === 'true' || isChanged(bodyworkRunt, bodyworkEff);
  const vehicleClass = normalizeVehicleClass(byKey('vehicle_class'));
  const bodyworkOptions = getBodyworksForVehicleClass(vehicleClass).map((o) => o.name);
  const bodyworkEmptyMessage = vehicleClass
    ? `No hay carrocerías disponibles para la clase ${vehicleClass}`
    : 'Consulta el RUNT para obtener la clase';

  const hasVehicle = [
    byKey('plate'),
    byKey('vin'),
    byKey('vehicle_brand'),
    colorRunt,
    fuelRunt,
  ].some((v) => v !== '');

  useEffect(() => {
    if (!instanceId || !hasVehicle) {
      // Sin vehículo no se limpia el estado: ya arranca vacío. El reset síncrono disparaba
      // react-hooks/set-state-in-effect y el card no se pinta de todos modos (`return null`).
      return;
    }
    let active = true;
    void tramitesClient
      .getAttachments(instanceId)
      .then((list) => {
        if (active) setAttachments(list);
      })
      .catch(() => {
        if (active) setAttachments([]);
      });
    return () => {
      active = false;
    };
  }, [instanceId, hasVehicle]);

  useEffect(() => {
    onCompletenessChange?.(
      areSimultaneousTramitesComplete(fieldValues, attachments, soloSubtramite),
    );
  }, [fieldValues, attachments, onCompletenessChange, soloSubtramite]);

  if (!hasVehicle) return null;

  const disabled = readOnly || saving;

  const setColor = (on: boolean, value?: string) =>
    on
      ? onPatch([
          { fieldKey: 'cambio_color', valueText: 'true' },
          // Vacío al activar: el gestor debe escoger el nuevo valor (no prellenar con RUNT).
          { fieldKey: 'vehicle_color', valueText: value ?? '' },
        ])
      : onPatch([
          { fieldKey: 'cambio_color', valueText: 'false' },
          { fieldKey: 'vehicle_color', valueText: colorRunt },
        ]);

  const setFuel = (on: boolean, value?: string) =>
    on
      ? onPatch([
          { fieldKey: 'cambio_combustible', valueText: 'true' },
          { fieldKey: 'vehicle_fuel', valueText: value ?? '' },
        ])
      : onPatch([
          { fieldKey: 'cambio_combustible', valueText: 'false' },
          { fieldKey: 'vehicle_fuel', valueText: fuelRunt },
        ]);

  const setBodywork = (on: boolean, value?: string) =>
    on
      ? onPatch([
          { fieldKey: 'cambio_carroceria', valueText: 'true' },
          { fieldKey: 'vehicle_body_type', valueText: value ?? '' },
        ])
      : onPatch([
          { fieldKey: 'cambio_carroceria', valueText: 'false' },
          { fieldKey: 'vehicle_body_type', valueText: bodyworkRunt },
        ]);

  const pickColor = (v: string) => {
    if (!isChanged(colorRunt, v)) return;
    void setColor(true, v);
  };
  const pickFuel = (v: string) => {
    if (!isChanged(fuelRunt, v)) return;
    void setFuel(true, v);
  };
  const pickBodywork = (v: string) => {
    if (!isChanged(bodyworkRunt, v)) return;
    void setBodywork(true, v);
  };

  const subtramites: SubtramiteItem[] = [
    {
      key: 'color',
      optionLabel: 'Cambio de Color',
      valueLabel: 'Nuevo color',
      runtValue: colorRunt,
      effectiveValue: colorEff,
      active: colorActive,
      colorCatalog: true,
      onToggle: (on) => void setColor(on),
      onSelect: pickColor,
    },
    {
      key: 'combustible',
      optionLabel: 'Conversiones de Combustible',
      valueLabel: 'Nuevo combustible',
      runtValue: fuelRunt,
      effectiveValue: fuelEff,
      active: fuelActive,
      options: VEHICLE_FUEL_CATALOG,
      onToggle: (on) => void setFuel(on),
      onSelect: pickFuel,
    },
    {
      key: 'carroceria',
      optionLabel: 'Cambio de Carrocería',
      valueLabel: 'Nueva carrocería',
      runtValue: bodyworkRunt,
      effectiveValue: bodyworkEff,
      active: bodyworkActive,
      options: bodyworkOptions,
      emptyMessage: bodyworkEmptyMessage,
      onToggle: (on) => void setBodywork(on),
      onSelect: pickBodywork,
    },
  ];

  // Modo tipo base: el subtrámite del tipo se pinta SIEMPRE, esté o no marcado en field_values —el
  // gestor no lo activó, lo trajo el trámite— y ningún otro se ofrece.
  const soloItem = soloSubtramite
    ? (subtramites.find((s) => s.key === soloSubtramite) ?? null)
    : null;
  const seleccionados = soloSubtramite
    ? (soloItem ? [soloItem] : [])
    : subtramites.filter((s) => s.active);
  const disponibles = soloSubtramite ? [] : subtramites.filter((s) => !s.active);
  const itemAQuitar = subtramites.find((s) => s.key === pendienteQuitar) ?? null;

  const attachmentFor = (key: SubtramiteKey) =>
    attachments.find((a) => a.tipo.toLowerCase() === DOC_TIPO_BY_KEY[key].toLowerCase());

  const refreshAttachments = async () => {
    if (!instanceId) return;
    const list = await tramitesClient.getAttachments(instanceId).catch(() => null);
    if (list) setAttachments(list);
    onDocumentsChanged?.();
  };

  const handleUpload = async (key: SubtramiteKey, file: File) => {
    if (!instanceId || readOnly) return;
    setUploadError(null);
    setUploadingKey(key);
    try {
      await tramitesClient.uploadAttachment(instanceId, DOC_TIPO_BY_KEY[key], file);
      await refreshAttachments();
    } catch {
      setUploadError('No se pudo adjuntar el archivo. Reintenta.');
    } finally {
      setUploadingKey(null);
    }
  };

  const handleRemoveAttachment = async (attachmentId: string) => {
    if (!instanceId || readOnly) return;
    setUploadError(null);
    setDeletingId(attachmentId);
    try {
      await tramitesClient.deleteAttachment(instanceId, attachmentId);
      await refreshAttachments();
    } catch {
      setUploadError('No se pudo eliminar el archivo. Reintenta.');
    } finally {
      setDeletingId(null);
    }
  };

  const handleAgregar = (key: string) => {
    subtramites.find((s) => s.key === key)?.onToggle(true);
  };

  const confirmarQuitar = () => {
    itemAQuitar?.onToggle(false);
    setPendienteQuitar(null);
  };

  const changes = summarizeDeclaredTransformations(fieldValues);

  return (
    <section
      aria-labelledby={headingId}
      className={hideHeader ? 'space-y-4' : 'space-y-4 rounded-2xl border bg-white p-4 dark:bg-[#162744]'}
      style={hideHeader ? undefined : { borderColor: '#E2E8F0' }}
    >
      {!hideHeader && (
        <WizardCardHeader
          id={headingId}
          title={soloItem ? soloItem.optionLabel : 'Trámites Simultáneos — Transformaciones del Vehículo'}
          subtitle={soloItem ? subtituloTipoBase(soloItem) : SUBTITULO_SIMULTANEOS}
        />
      )}
      {hideHeader && (
        <p id={headingId} className="text-[13px] opacity-70">
          {soloItem ? subtituloTipoBase(soloItem) : SUBTITULO_SIMULTANEOS}
        </p>
      )}

      {/* El selector de «agregar» es el acumulador del art. 5.1.8, y en el modo tipo base no hay
          nada que acumular: el único cambio del trámite ya está abajo, activo y no removible. */}
      {!soloSubtramite && (
        <div>
          <label htmlFor="tramite-simultaneo-agregar" className={`${WIZARD_LABEL} mb-1.5`}>
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
              {disponibles.length === 0
                ? 'No hay más trámites simultáneos disponibles'
                : 'Selecciona uno o varios trámites para agregar...'}
            </option>
            {disponibles.map((s) => (
              <option key={s.key} value={s.key}>
                {s.optionLabel}
              </option>
            ))}
          </select>
        </div>
      )}

      {uploadError && (
        <p className="text-xs font-medium" style={{ color: '#FF4E00' }} role="alert">
          {uploadError}
        </p>
      )}

      {seleccionados.length > 0 && (
        <div
          className={cn(
            'grid gap-4',
            seleccionados.length === 1 && 'grid-cols-1 sm:max-w-sm',
            seleccionados.length === 2 && 'grid-cols-1 sm:grid-cols-2',
            seleccionados.length >= 3 && 'grid-cols-1 sm:grid-cols-2 xl:grid-cols-3',
          )}
        >
          {seleccionados.map((s) => (
            <SubtramiteDocCard
              key={s.key}
              item={s}
              disabled={disabled}
              // El subtrámite ES el trámite: no se puede quitar —quitarlo sería quedarse sin
              // trámite; se cambia eligiendo otro tipo— y no repite su nombre, que ya lo pone el
              // contenedor (la cabecera de la tarjeta o el acordeón del paso).
              esTipoBase={!!soloSubtramite}
              attachment={attachmentFor(s.key)}
              canUpload={!!instanceId && !readOnly}
              uploading={uploadingKey === s.key}
              deleting={!!attachmentFor(s.key) && deletingId === attachmentFor(s.key)?.id}
              onRequestRemove={() => setPendienteQuitar(s.key)}
              onUpload={(file) => void handleUpload(s.key, file)}
              onRemoveAttachment={(id) => void handleRemoveAttachment(id)}
            />
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
          : soloItem
            ? `Escoge el ${soloItem.valueLabel.toLowerCase()} y adjunta el soporte: es lo que el FUR declara.`
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
  optionLabel: string;
  valueLabel: string;
  runtValue: string;
  effectiveValue: string;
  active: boolean;
  options?: readonly string[];
  colorCatalog?: boolean;
  emptyMessage?: string;
  onToggle: (on: boolean) => void;
  onSelect: (value: string) => void;
}

/**
 * Card estilo DocSlot del prototipo + select de valor FLIT (nuevo color/combustible/carrocería).
 */
function SubtramiteDocCard({
  item,
  disabled,
  esTipoBase = false,
  attachment,
  canUpload,
  uploading,
  deleting,
  onRequestRemove,
  onUpload,
  onRemoveAttachment,
}: {
  item: SubtramiteItem;
  disabled: boolean;
  /**
   * El subtrámite ES el trámite (familia OTROS): no se puede quitar, y no repite su nombre —lo
   * pone el contenedor—. Con `false` es un simultáneo acumulado: nombre propio y botón de quitar.
   */
  esTipoBase?: boolean;
  attachment: ProcedureAttachment | undefined;
  canUpload: boolean;
  uploading: boolean;
  deleting: boolean;
  onRequestRemove: () => void;
  onUpload: (file: File) => void;
  onRemoveAttachment: (id: string) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const selectId = `tramite-simultaneo-${item.key}-valor`;
  // Solo se muestra valor si ya es un cambio real frente al RUNT (activo + vacío = pendiente).
  const changed = isChanged(item.runtValue, item.effectiveValue);
  const selectValue = changed ? item.effectiveValue : '';
  const valueMissing = !changed;
  const fileMissing = !attachment;
  const selectOptions = excludeRunt(
    mergeOption(item.options ?? [], selectValue),
    item.runtValue,
  );
  const done = changed && !!attachment;
  const busy = uploading || deleting;

  return (
    <div
      className={cn(
        'relative flex h-full flex-col rounded-xl bg-white p-4 shadow-sm transition hover:shadow-md dark:bg-[#162744]',
        done ? 'border' : 'border-2 border-dashed hover:border-[#557EFF] hover:bg-[#F0F5FF]',
      )}
      style={{ borderColor: '#E2E8F0' }}
    >
      <div className="absolute right-3 top-3 flex items-center gap-1.5">
        {done ? (
          <span
            className="whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-semibold text-white"
            style={{ background: '#8CC63F' }}
          >
            Validado
          </span>
        ) : (
          <span className="whitespace-nowrap rounded-full bg-red-50 px-2.5 py-0.5 text-xs font-medium text-red-600">
            * Obligatorio
          </span>
        )}
        {!disabled && !esTipoBase && (
          <button
            type="button"
            onClick={onRequestRemove}
            aria-label={`Quitar ${item.optionLabel}`}
            className="grid h-6 w-6 place-items-center rounded-full border bg-white transition hover:bg-[#FEF2F2]"
            style={{ borderColor: '#FECACA', color: '#B91C1C' }}
          >
            <X className="h-3.5 w-3.5" aria-hidden="true" />
          </button>
        )}
      </div>

      {/* Con el subtrámite como tipo base, el contenedor ya lo nombró: repetirlo dejaba dos rótulos
          seguidos diciendo lo mismo. Lo que sí falta ahí es qué soporte se pide, así que sube. */}
      <p className="pr-28 text-[13px] font-semibold leading-tight" style={{ color: '#162744' }}>
        {esTipoBase ? SOPORTE_HINT[item.key] : item.optionLabel}
      </p>
      {!esTipoBase && <p className="mt-0.5 text-[11px] opacity-70">{SOPORTE_HINT[item.key]}</p>}
      <p className="mt-1 text-[11px] opacity-70">PDF, JPG hasta 5MB</p>
      {attachment && (
        <p className="mt-1 truncate text-[11px] opacity-60">{attachment.filename}</p>
      )}

      {(done || uploading) && (
        <div
          className="mt-3 h-1.5 w-full overflow-hidden rounded-full"
          style={{ background: '#F1F5F9' }}
          aria-hidden="true"
        >
          <div
            className={`h-full rounded-full ${uploading ? 'animate-pulse' : ''}`}
            style={{
              width: uploading ? '60%' : '100%',
              background: uploading ? '#557EFF' : '#8CC63F',
            }}
          />
        </div>
      )}

      {/* Select de valor (FLIT): vacío al agregar; obligatorio y distinto del RUNT. */}
      <div className="mt-3 space-y-1.5">
        <span className="block text-[11px] opacity-70">{`RUNT: ${up(item.runtValue) || '—'}`}</span>
        {item.colorCatalog ? (
          <VehicleColorSearchSelect
            id={selectId}
            label={`${item.valueLabel} *`}
            value={selectValue}
            disabled={disabled}
            invalid={valueMissing}
            placeholder="Selecciona…"
            onChange={item.onSelect}
          />
        ) : (item.options ?? []).length === 0 && item.emptyMessage ? (
          <p className="text-xs opacity-70">{item.emptyMessage}</p>
        ) : (
          <CatalogSearchSelect
            id={selectId}
            label={`${item.valueLabel} *`}
            value={selectValue}
            options={selectOptions}
            disabled={disabled}
            invalid={valueMissing}
            placeholder={`Selecciona ${item.valueLabel.toLowerCase()}…`}
            onChange={item.onSelect}
          />
        )}
        {valueMissing && (
          <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }}>
            Escoge un valor distinto al del RUNT.
          </p>
        )}
        {fileMissing && (
          <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }}>
            Adjunta el soporte obligatorio.
          </p>
        )}
        {changed && (
          <p className="flex items-center gap-1.5 text-xs" style={{ color: '#557EFF' }}>
            <span className="opacity-70">{up(item.runtValue)}</span>
            <ArrowRight aria-hidden="true" className="h-3 w-3" />
            <span className="font-semibold">{up(item.effectiveValue)}</span>
          </p>
        )}
      </div>

      <div className="mt-auto flex flex-wrap items-center gap-2 pt-4">
        {canUpload ? (
          <>
            <input
              ref={inputRef}
              type="file"
              accept="application/pdf,image/jpeg,image/png,image/webp"
              className="hidden"
              aria-label={`Adjuntar ${SOPORTE_HINT[item.key]}`}
              onChange={(e) => {
                const file = e.target.files?.[0];
                e.target.value = '';
                if (file) onUpload(file);
              }}
            />
            <button
              type="button"
              onClick={() => inputRef.current?.click()}
              disabled={busy || disabled}
              className="inline-flex h-9 items-center gap-1.5 rounded-lg border bg-white px-4 text-[12px] font-semibold transition hover:bg-[#EFF6FF] disabled:cursor-not-allowed disabled:opacity-50 dark:bg-transparent"
              style={{
                borderColor: fileMissing ? '#FF4E00' : '#557EFF',
                color: fileMissing ? '#FF4E00' : '#557EFF',
                borderWidth: fileMissing ? 2 : 1,
              }}
            >
              <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
              {uploading
                ? 'Subiendo…'
                : attachment
                  ? 'Reemplazar archivo'
                  : 'Adjuntar archivo'}
            </button>
            {attachment && (
              <button
                type="button"
                onClick={() => onRemoveAttachment(attachment.id)}
                disabled={busy || disabled}
                className="text-xs font-semibold disabled:opacity-50"
                style={{ color: '#FF4E00' }}
                aria-label={`Borrar ${SOPORTE_HINT[item.key]}`}
              >
                {deleting ? 'Borrando…' : 'Borrar'}
              </button>
            )}
          </>
        ) : (
          <p className="text-[11px] opacity-60">
            {attachment ? 'Documento adjunto' : 'Guarda el trámite para poder adjuntar el soporte.'}
          </p>
        )}
      </div>
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

function mergeOption(options: readonly string[], current: string): string[] {
  const cur = current.trim();
  if (cur === '') return [...options];
  const exists = options.some((o) => o.toUpperCase() === cur.toUpperCase());
  return exists ? [...options] : [cur.toUpperCase(), ...options];
}

/** Quita el valor RUNT del catálogo: no sirve como “nuevo” valor. */
function excludeRunt(options: readonly string[], runt: string): string[] {
  const r = runt.trim().toUpperCase();
  if (!r) return [...options];
  return options.filter((o) => o.trim().toUpperCase() !== r);
}

/**
 * Completo = sin subtrámites activos, o cada activo tiene valor distinto al RUNT + adjunto soporte.
 */
export function areSimultaneousTramitesComplete(
  fieldValues: FieldValue[],
  attachments: ProcedureAttachment[],
  soloSubtramite: SubtramiteKey | null = null,
): boolean {
  return getSimultaneousIncompleteMessages(fieldValues, attachments, soloSubtramite).length === 0;
}

/**
 * @param soloSubtramite Modo tipo base: ese subtrámite cuenta como ACTIVO aunque no tenga bandera ni
 * diff todavía — el trámite lo trae por definición, así que dejarlo sin valor o sin soporte es un
 * expediente incompleto, no «ninguna transformación declarada».
 */
export function getSimultaneousIncompleteMessages(
  fieldValues: FieldValue[],
  attachments: ProcedureAttachment[],
  soloSubtramite: SubtramiteKey | null = null,
): string[] {
  const byKey = (key: string) =>
    fieldValues.find((f) => f.fieldKey === key)?.valueText?.trim() ?? '';

  const specs: {
    key: SubtramiteKey;
    flag: string;
    runtKey: string;
    runtFallback: string;
    effKey: string;
    label: string;
  }[] = [
    {
      key: 'color',
      flag: 'cambio_color',
      runtKey: 'vehicle_color_runt',
      runtFallback: 'vehicle_color',
      effKey: 'vehicle_color',
      label: 'Cambio de Color',
    },
    {
      key: 'combustible',
      flag: 'cambio_combustible',
      runtKey: 'vehicle_fuel_runt',
      runtFallback: 'vehicle_fuel',
      effKey: 'vehicle_fuel',
      label: 'Conversiones de Combustible',
    },
    {
      key: 'carroceria',
      flag: 'cambio_carroceria',
      runtKey: 'vehicle_body_type_runt',
      runtFallback: 'vehicle_body_type',
      effKey: 'vehicle_body_type',
      label: 'Cambio de Carrocería',
    },
  ];

  const messages: string[] = [];
  for (const s of specs) {
    // En modo tipo base solo se evalúa el subtrámite del tipo: los otros dos no pueden estar
    // activos (el PATCH los rechaza) y mirarlos solo podría bloquear el paso por un residuo.
    if (soloSubtramite && s.key !== soloSubtramite) continue;
    const runt = byKey(s.runtKey) || byKey(s.runtFallback);
    const eff = byKey(s.effKey);
    const active = s.key === soloSubtramite || byKey(s.flag) === 'true' || isChanged(runt, eff);
    if (!active) continue;
    if (!isChanged(runt, eff)) {
      messages.push(`${s.label}: escoge el nuevo valor.`);
    }
    const hasDoc = attachments.some(
      (a) => a.tipo.toLowerCase() === DOC_TIPO_BY_KEY[s.key].toLowerCase(),
    );
    if (!hasDoc) {
      messages.push(`${s.label}: adjunta el soporte obligatorio.`);
    }
  }
  return messages;
}
