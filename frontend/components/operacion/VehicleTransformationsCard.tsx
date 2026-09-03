'use client';

import { useEffect, useId, useRef, useState } from 'react';
import { ArrowRight, Paperclip } from 'lucide-react';
import type {
  FieldValue,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
import { cn } from '@/lib/utils';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  VEHICLE_FUEL_CATALOG,
} from '@/lib/catalogs/vehicle-transformations';
import { CatalogSearchSelect } from './CatalogSearchSelect';
import { VehicleColorSearchSelect } from './VehicleColorSearchSelect';
import { VehicleBodyworkSearchSelect } from './VehicleBodyworkSearchSelect';
import { WizardCardHeader, WizardFieldToggle } from './wizard-atoms';
import { DocumentCatalogCaption } from '@/components/shared/DocumentCatalogCaption';
import { catalogDocumentTitle } from '@/lib/tramites/document-labels';
// Misma regla de «hay transformación» que aplica el detalle del OT al revisarla (HU #11931).
import { valorCambiado as isChanged } from '@/lib/tramites/transformaciones-vehiculo';

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

interface DocumentoSugerido {
  /** Nombre legible del documento a adjuntar. NO es el nombre de archivo del cargue. */
  titulo: string;
  descripcion: string;
}

/**
 * Nota informativa de qué documento adjuntar en cada transformación (no había ninguna: el gestor
 * solo veía el DocTipo del catálogo, que dice CÓMO se llama el requisito, no QUÉ pide). Combustible
 * y carrocería tienen un único documento; color depende de la modalidad porque quien lo declara
 * cambia (concesionario en matrícula inicial, propietario en los demás trámites).
 */
const NOTA_COMBUSTIBLE: DocumentoSugerido = {
  titulo: 'Certificado de conversión de combustible',
  descripcion:
    'Certificado de conversión emitido por un taller autorizado por el Ministerio de Minas y Energía, con sello y número de resolución.',
};

const NOTA_CARROCERIA: DocumentoSugerido = {
  titulo: 'Factura y homologación de carrocería',
  descripcion:
    'Factura de compra de la nueva carrocería y ficha técnica de homologación, expedida por el fabricante o importador autorizado.',
};

const NOTA_COLOR_MATRICULA_INICIAL: DocumentoSugerido = {
  titulo: 'Declaración de color del concesionario',
  descripcion:
    'Declaración escrita del color del vehículo, expedida por el concesionario al momento de la matrícula inicial, con firma respectiva.',
};

const NOTA_COLOR_TRASPASO: DocumentoSugerido = {
  titulo: 'Declaración de color del propietario',
  descripcion:
    'Declaración escrita del cambio de color expedida por el propietario, indicando color anterior y nuevo, con firma respectiva.',
};

function notaDoc(key: SubtramiteKey, modalidad: WizardModalidad | undefined): DocumentoSugerido {
  switch (key) {
    case 'combustible':
      return NOTA_COMBUSTIBLE;
    case 'carroceria':
      return NOTA_CARROCERIA;
    case 'color':
      return modalidad === 'matricula_inicial' ? NOTA_COLOR_MATRICULA_INICIAL : NOTA_COLOR_TRASPASO;
  }
}

function soporteDoc(key: SubtramiteKey): { codigo: string; nombre: string; title: string } {
  const codigo = DOC_TIPO_BY_KEY[key];
  const nombre = SOPORTE_HINT[key];
  return { codigo, nombre, title: catalogDocumentTitle(codigo, nombre) };
}

const SUBTITULO_SIMULTANEOS =
  'Declara un cambio de color, combustible o carrocería frente al RUNT.';

/** Copy del modo tipo base: el cambio no se «declara además», es el trámite. */
const subtituloTipoBase = (item: SubtramiteItem) =>
  `${item.valueLabel}: es el trámite que se está radicando. El soporte es opcional.`;

/**
 * Tarjeta "Trámites Simultáneos — Transformaciones del Vehículo" (prototipo Lovable Traspaso).
 *
 * - Tres checks independientes (color / combustible / carrocería).
 * - Por cada uno activo: valor nuevo (obligatorio para el FUR) y adjunto de soporte opcional.
 *
 * Con {@link soloSubtramite} la tarjeta deja de ser un acumulador y pasa a ser la captura del
 * atributo que el trámite cambia por definición (familia OTROS): un único subtrámite, siempre
 * activo, sin checks de las otras dos transformaciones. El valor nuevo sigue siendo obligatorio;
 * el soporte es opcional.
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
  modalidad,
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
  /** Notifica si todos los subtrámites activos tienen valor nuevo (gate Continuar). El adjunto es opcional. */
  onCompletenessChange?: (complete: boolean) => void;
  /**
   * Modo tipo base (familia OTROS): captura SOLO este atributo, siempre activo y no removible.
   * `null` (default) = acumulador de simultáneos, el comportamiento de matrícula y traspaso.
   */
  soloSubtramite?: SubtramiteKey | null;
  /**
   * Gobierna la nota informativa de «cambio de color»: quién declara el color no es el mismo actor
   * en matrícula inicial (concesionario) que en el resto de trámites (propietario). Sin dato, se usa
   * el texto de traspaso — es el que ya existía antes de que esta nota se pudiera distinguir por
   * modalidad.
   */
  modalidad?: WizardModalidad;
}) {
  const headingId = useId();
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
  const vehicleClassRaw = byKey('vehicle_class') || byKey('vehicle_class_runt');

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
      nota: notaDoc('color', modalidad),
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
      nota: notaDoc('combustible', modalidad),
      onToggle: (on) => void setFuel(on),
      onSelect: pickFuel,
    },
    {
      key: 'carroceria',
      optionLabel: 'Cambio de Carrocería',
      valueLabel: 'Nueva carrocería',
      runtValue: bodyworkRunt,
      effectiveValue: bodyworkEff,
      nota: notaDoc('carroceria', modalidad),
      active: bodyworkActive,
      bodyworkCatalog: true,
      vehicleClass: vehicleClassRaw,
      onToggle: (on) => void setBodywork(on),
      onSelect: pickBodywork,
    },
  ];

  // Modo tipo base: el subtrámite del tipo se pinta SIEMPRE, esté o no marcado en field_values —el
  // gestor no lo activó, lo trajo el trámite— y ningún otro se ofrece.
  const soloItem = soloSubtramite
    ? (subtramites.find((s) => s.key === soloSubtramite) ?? null)
    : null;
  const cardsVisibles = soloSubtramite
    ? (soloItem ? [soloItem] : [])
    : subtramites.filter((s) => s.active);

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

  const changes = summarizeDeclaredTransformations(fieldValues);

  const renderCard = (s: SubtramiteItem) => (
    <SubtramiteDocCard
      key={s.key}
      item={s}
      disabled={disabled}
      esTipoBase={!!soloSubtramite}
      attachment={attachmentFor(s.key)}
      canUpload={!!instanceId && !readOnly}
      uploading={uploadingKey === s.key}
      deleting={!!attachmentFor(s.key) && deletingId === attachmentFor(s.key)?.id}
      onUpload={(file) => void handleUpload(s.key, file)}
      onRemoveAttachment={(id) => void handleRemoveAttachment(id)}
    />
  );

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

      {/* Checks independientes: cada transformación se activa o se apaga sin el selector de agregar. */}
      {!soloSubtramite && (
        <div className="space-y-4">
          {subtramites.map((s) => (
            <div key={s.key} className="space-y-3">
              <WizardFieldToggle
                id={`tramite-simultaneo-${s.key}`}
                label={s.optionLabel}
                checked={s.active}
                onChange={(on) => s.onToggle(on)}
                disabled={disabled}
              />
              {s.active && renderCard(s)}
            </div>
          ))}
        </div>
      )}

      {soloSubtramite && cardsVisibles.length > 0 && (
        <div className="grid grid-cols-1 gap-4 sm:max-w-sm">{cardsVisibles.map(renderCard)}</div>
      )}

      {uploadError && (
        <p className="text-xs font-medium" style={{ color: '#FF4E00' }} role="alert">
          {uploadError}
        </p>
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
            ? `Escoge el ${soloItem.valueLabel.toLowerCase()} para declararlo en el FUR.`
            : 'Sin transformaciones declaradas: se registrará el dato del RUNT.'}
      </p>
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
  bodyworkCatalog?: boolean;
  vehicleClass?: string;
  emptyMessage?: string;
  /** Qué documento adjuntar y por qué (HU nota informativa de transformaciones). */
  nota: DocumentoSugerido;
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
  onUpload,
  onRemoveAttachment,
}: {
  item: SubtramiteItem;
  disabled: boolean;
  /**
   * El subtrámite ES el trámite (familia OTROS): no se puede quitar, y no repite su nombre —lo
   * pone el contenedor—. Con `false` es un simultáneo: el check de arriba es el encendido/apagado.
   */
  esTipoBase?: boolean;
  attachment: ProcedureAttachment | undefined;
  canUpload: boolean;
  uploading: boolean;
  deleting: boolean;
  onUpload: (file: File) => void;
  onRemoveAttachment: (id: string) => void;
}) {
  const soporte = soporteDoc(item.key);
  const inputRef = useRef<HTMLInputElement>(null);
  const selectId = `tramite-simultaneo-${item.key}-valor`;
  const changed = isChanged(item.runtValue, item.effectiveValue);
  const selectValue = changed ? item.effectiveValue : '';
  const valueMissing = !changed;
  const selectOptions = excludeRunt(
    mergeOption(item.options ?? [], selectValue),
    item.runtValue,
  );
  const done = changed;
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
      </div>

      {/* Con el subtrámite como tipo base, el contenedor ya lo nombró: repetirlo dejaba dos rótulos
          seguidos diciendo lo mismo. Lo que sí falta ahí es qué soporte se pide, así que sube. */}
      <p className="pr-28 text-[13px] font-semibold leading-tight" style={{ color: '#162744' }}>
        {esTipoBase ? (
          <DocumentCatalogCaption nombre={soporte.nombre} codigo={soporte.codigo} />
        ) : (
          item.optionLabel
        )}
      </p>
      <p className="mt-1 text-[11px] opacity-70">PDF, JPG hasta 5MB · Opcional</p>

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
        ) : item.bodyworkCatalog ? (
          <VehicleBodyworkSearchSelect
            id={selectId}
            label={`${item.valueLabel} *`}
            value={selectValue}
            vehicleClass={item.vehicleClass ?? ''}
            excludeName={item.runtValue}
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
        {changed && (
          <p className="flex items-center gap-1.5 text-xs" style={{ color: '#557EFF' }}>
            <span className="opacity-70">{up(item.runtValue)}</span>
            <ArrowRight aria-hidden="true" className="h-3 w-3" />
            <span className="font-semibold">{up(item.effectiveValue)}</span>
          </p>
        )}
      </div>

      {/* Nota informativa: qué documento sirve como soporte, con nombre sugerido y qué debe
          contener. Pegada al botón que activa —es la que responde «qué adjunto aquí»—, y sin
          repetir el nombre técnico del DocTipo, que ya dice el heading de la tarjeta. El nombre
          sugerido es el título con el que se identifica, no el nombre del archivo que suba el
          gestor. */}
      <div
        className="mt-3 rounded-lg px-2.5 py-2 text-[11px] leading-snug"
        style={{ background: '#F8FAFC' }}
      >
        <p className="font-semibold" style={{ color: '#162744' }}>
          <span className="font-normal opacity-70">Debes adjuntar: </span>
          {item.nota.titulo}
        </p>
        <p className="mt-0.5 opacity-70">{item.nota.descripcion}</p>
      </div>

      <div className="mt-auto flex flex-wrap items-center gap-2 pt-4">
        {canUpload ? (
          <>
            <input
              ref={inputRef}
              type="file"
              accept="application/pdf,image/jpeg,image/png,image/webp"
              className="hidden"
              aria-label={`Adjuntar ${soporte.title}`}
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
                borderColor: '#557EFF',
                color: '#557EFF',
                borderWidth: 1,
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
                aria-label={`Borrar ${soporte.title}`}
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
 * Completo = sin subtrámites activos, o cada activo tiene valor distinto al RUNT.
 * El certificado de soporte es opcional y no bloquea Continuar.
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
 * diff todavía — el trámite lo trae por definición, así que dejarlo sin valor es un expediente
 * incompleto, no «ninguna transformación declarada».
 */
export function getSimultaneousIncompleteMessages(
  fieldValues: FieldValue[],
  _attachments: ProcedureAttachment[],
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
  }
  return messages;
}
