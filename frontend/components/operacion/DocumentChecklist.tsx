'use client';

import { useRef, useState } from 'react';
import { useWizardFocusTrap } from './use-wizard-focus-trap';
import { Eye, Info } from 'lucide-react';
import {
  ocrResultForTipo,
  resumirVins,
  useProcedureDocuments,
  type OcrUiResult,
} from '@/hooks/useProcedureDocuments';
import { useProcedureBatchUpload } from '@/hooks/useProcedureBatchUpload';
import { BatchDropzone } from './BatchDropzone';
import { BatchReviewPanel } from './BatchReviewPanel';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import { WizardCardHeader } from './wizard-atoms';
import { INLINE_ALERT_TONES } from '@/components/atom/InlineAlert';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { CarLoaderModal } from '@/components/atom/CarLoader';
import { isPrendaManagedChecklistTipo } from './prenda-document-tipos';
import { tramitesClient } from '@/lib/api/tramites-client';
import { DocumentPreviewModal } from '@/components/shared/DocumentPreviewModal';
import { DocumentCatalogCaption } from '@/components/shared/DocumentCatalogCaption';
import { catalogDocumentTitle } from '@/lib/tramites/document-labels';
import type {
  ChecklistItemView,
  ProcedureAttachment,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

export type DocumentUploadMode = 'individual' | 'batch';

/** Mensaje del POST generate-impronta del trámite (ProblemDetails.detail). */
export function describeGenerarImprontaEnTramite(error: unknown): string {
  if (error instanceof Error && error.message.trim()) return error.message;
  return 'No se pudo generar la impronta. Verifica placa u organismo e intenta de nuevo.';
}

interface Props {
  instanceId: string | null;
  /**
   * Notifica al contenedor (wizard) que los documentos cambiaron, para que
   * re-consulte su estado de gating. Sin esto, el wizard no se entera de que
   * el checklist quedó completo y "Continuar" no se habilita.
   */
  onChanged?: () => void;
  /**
   * Oculta el título "Documentos requeridos" y su descripción cuando el contenedor
   * ya pinta el título del paso (el wizard lo hace con su h2 + subtítulo).
   */
  hideHeader?: boolean;
  /** Modalidad del trámite: decide qué tipos pasan por OCR (matrícula: 4; traspaso: impronta+soat). */
  modalidad?: WizardModalidad;
  /**
   * Modo controlado (prototipo: toggle en cabecera del acordeón). Sin props, el checklist
   * gestiona el modo por su cuenta y pinta el toggle en el cuerpo.
   */
  uploadMode?: DocumentUploadMode;
  onUploadModeChange?: (mode: DocumentUploadMode) => void;
  /** Si true, no pinta el toggle interno (ya va en el badge del WizardAccordion). */
  hideModeToggle?: boolean;
  /**
   * Si el tipo admite generar la impronta (Kyverum / FUR). Independiente de si el ítem es
   * obligatorio. Por defecto sí: solo `improntaSource === 'MANUAL'` la apaga.
   */
  permiteGenerarImprontaAutomatica?: boolean;
}

/**
 * Toggle Carga Individual / Carga Masiva (prototipo Lovable — pista segmentada con borde).
 * Exportado para vivir en la cabecera del acordeón «Gestión de documentos».
 */
export function DocumentUploadModeToggle({
  value,
  onChange,
  disabled = false,
}: {
  value: DocumentUploadMode;
  onChange: (mode: DocumentUploadMode) => void;
  disabled?: boolean;
}) {
  const options: { value: DocumentUploadMode; label: string }[] = [
    { value: 'individual', label: 'Carga Individual' },
    { value: 'batch', label: 'Carga Masiva' },
  ];
  return (
    <div
      className="inline-flex rounded-xl border bg-white p-1 dark:bg-[#162744]"
      style={{ borderColor: '#E2E8F0' }}
      role="group"
      aria-label="Modo de cargue de documentos"
    >
      {options.map((opt) => {
        const on = value === opt.value;
        return (
          <button
            key={opt.value}
            type="button"
            disabled={disabled}
            aria-pressed={on}
            onClick={() => onChange(opt.value)}
            className="rounded-lg px-4 py-2 text-[13px] font-semibold transition disabled:opacity-50"
            style={
              on
                ? {
                    background: '#EFF6FF',
                    color: '#557EFF',
                    boxShadow: '0 4px 20px -2px rgba(15,23,42,0.05)',
                  }
                : { color: '#64748B' }
            }
          >
            {opt.label}
          </button>
        );
      })}
    </div>
  );
}

/**
 * Tipos de documento que el sistema genera automáticamente (mandato, trámite virtual).
 * El operador no puede ni debe adjuntarlos; el slot muestra "Autogenerado por el sistema".
 */
const AUTO_DOC_TIPOS = new Set(['mandato', 'tramite_virtual']);

/** MIME permitidos por el contrato. */
export const ALLOWED_MIME = [
  'application/pdf',
  'image/jpeg',
  'image/png',
  'image/webp',
] as const;

/** Tamaño máximo: 20 MB. */
export const MAX_SIZE_BYTES = 20 * 1024 * 1024;

const ALLOWED_LABEL = 'PDF, JPG, PNG o WEBP';

/**
 * Límites de carga por tipo de documento (HU #10524, RF08/09/10). Cuando se omiten (o vienen
 * vacíos) se aplican los límites globales actuales — misma validación que hoy, sin regresión.
 * El backend (HU #10520) es la fuente de verdad: valida por tipo en la carga; este check cliente
 * es un pre-filtro de UX.
 */
export interface FileTypeLimits {
  allowedMimes?: readonly string[];
  maxSizeBytes?: number;
}

/**
 * Valida mime y tamaño antes de subir. Pura y testeable de forma aislada. Aplica los límites
 * por tipo si se proveen (<paramref name="limits"/>), con respaldo a los globales.
 * Devuelve un mensaje de error claro, o null si el archivo es aceptable.
 */
export function validateFile(file: File, limits?: FileTypeLimits): string | null {
  const allowed = (limits?.allowedMimes && limits.allowedMimes.length > 0
    ? limits.allowedMimes
    : ALLOWED_MIME) as readonly string[];
  const maxSize = limits?.maxSizeBytes && limits.maxSizeBytes > 0 ? limits.maxSizeBytes : MAX_SIZE_BYTES;

  if (!allowed.includes(file.type)) {
    return `Tipo de archivo no permitido. Usa ${ALLOWED_LABEL}.`;
  }
  if (file.size > maxSize) {
    return maxSize === MAX_SIZE_BYTES
      ? 'El archivo supera el máximo de 20 MB.'
      : `El archivo supera el máximo de ${formatSize(maxSize)}.`;
  }
  return null;
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** Formatea un monto como moneda COP: 119990000 → "$119.990.000". '' si no es un número &gt; 0. */
function formatCOP(value: unknown): string {
  const n = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(n) || n <= 0) return '';
  return new Intl.NumberFormat('es-CO', {
    style: 'currency',
    currency: 'COP',
    maximumFractionDigits: 0,
  }).format(n);
}

/** Valor string no vacío de un campo del JSON del OCR (trim); '' si es null/undefined. */
function pickStr(data: Record<string, unknown>, key: string): string {
  const v = data[key];
  return v === null || v === undefined ? '' : String(v).trim();
}

/** Une varios campos no vacíos con un separador (p. ej. marca + línea + modelo). */
function joinFields(data: Record<string, unknown>, keys: string[], sep = ' '): string {
  return keys.map((k) => pickStr(data, k)).filter(Boolean).join(sep);
}

/**
 * HU #11998 — estado de deuda del paz y salvo, en palabras. `no_determinado` no es un fallo del
 * análisis: hay documentos —una declaración suelta, por ejemplo— que sencillamente no permiten
 * saber el saldo, y decirlo es más honesto que suponer que está al día.
 */
const ESTADO_DEUDA: Record<string, string> = {
  al_dia: 'Al día',
  adeuda: 'Adeuda vigencias',
  no_determinado: 'No se puede determinar',
};

/**
 * HU #12030 — estado de la sociedad en el certificado de cámara de comercio. Una sociedad disuelta o
 * en liquidación no invalida el documento: el certificado sigue siendo el correcto y lo que cambia es
 * lo que el gestor debe saber antes de radicar. Por eso se informa aquí y no en el veredicto del OCR.
 */
const ESTADO_SOCIEDAD: Record<string, string> = {
  activa: 'Activa',
  disuelta: 'Disuelta',
  en_liquidacion: 'En liquidación',
  cancelada: 'Matrícula cancelada',
  no_determinado: 'No se puede determinar',
};

/**
 * HU #12038 — aviso cuando el modelo declara que no pudo leer bien el documento. Solo hay texto para
 * los casos que piden cautela: con «buena» no se muestra nada, que es el 95 % de las veces.
 */
const LEGIBILIDAD_AVISO: Record<string, string> = {
  parcial: 'El documento se leyó con dificultad: comprueba los datos antes de darlos por buenos.',
  mala: 'No se pudo leer el documento con fiabilidad. Los campos que falten es porque no se distinguían.',
};

/** Descriptor de un campo del resumen OCR. */
interface OcrField {
  label: string;
  /** Valor a mostrar (ya formateado); '' → la fila se omite. */
  value: (d: Record<string, unknown>) => string;
  /** 'state' → se pinta como chip de color (coincide/no_coincide/…). */
  kind?: 'state';
  /** Campo VIN: se resalta en rojo si el rechazo fue por cruce de VIN. */
  vin?: boolean;
  /** Valor en negrita (p. ej. Total). */
  strong?: boolean;
}

/** Campos del resumen por tipo de documento (set ampliado). */
const OCR_RESUMEN_FIELDS: Record<string, ReadonlyArray<OcrField>> = {
  factura: [
    { label: 'N.º factura', value: (d) => pickStr(d, 'numero_factura') },
    { label: 'Fecha', value: (d) => pickStr(d, 'fecha') },
    { label: 'Emisor', value: (d) => pickStr(d, 'emisor_nombre') },
    { label: 'NIT', value: (d) => pickStr(d, 'emisor_nit') },
    { label: 'Comprador', value: (d) => pickStr(d, 'comprador_nombre') },
    { label: 'Vehículo', value: (d) => joinFields(d, ['vehiculo_marca', 'vehiculo_linea', 'vehiculo_modelo']) },
    { label: 'Color', value: (d) => pickStr(d, 'vehiculo_color') },
    { label: 'IVA', value: (d) => formatCOP(d.iva) },
    { label: 'Total', value: (d) => formatCOP(d.total), strong: true },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin'), vin: true },
  ],
  aduana: [
    { label: 'N.º documento', value: (d) => pickStr(d, 'numero_documento') },
    { label: 'Fecha', value: (d) => pickStr(d, 'fecha') },
    { label: 'Aduana', value: (d) => pickStr(d, 'aduana') },
    { label: 'Importador', value: (d) => pickStr(d, 'importador_nombre') },
    { label: 'Subpartida', value: (d) => pickStr(d, 'subpartida_arancelaria') },
    { label: 'País origen', value: (d) => pickStr(d, 'pais_origen') },
    { label: 'Valor CIF', value: (d) => formatCOP(d.valor_cif_cop), strong: true },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin') || pickStr(d, 'vehiculo_chasis'), vin: true },
  ],
  impronta: [
    { label: 'N.º certificado', value: (d) => pickStr(d, 'numero_certificado') },
    { label: 'Entidad', value: (d) => pickStr(d, 'entidad_emisora') },
    { label: 'Estado motor', value: (d) => pickStr(d, 'estado_motor'), kind: 'state' },
    { label: 'Estado VIN', value: (d) => pickStr(d, 'estado_vin'), kind: 'state' },
    { label: 'Estado chasis', value: (d) => pickStr(d, 'estado_chasis'), kind: 'state' },
    { label: 'Alertas', value: (d) => (Array.isArray(d.alertas) ? d.alertas.join(', ') : '') },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin') || pickStr(d, 'vehiculo_chasis'), vin: true },
  ],
  soat: [
    { label: 'N.º póliza', value: (d) => pickStr(d, 'numero_poliza') },
    { label: 'Aseguradora', value: (d) => pickStr(d, 'aseguradora') },
    { label: 'Vigencia', value: (d) => joinFields(d, ['fecha_inicio', 'fecha_vencimiento'], ' – ') },
    { label: 'Estado', value: (d) => pickStr(d, 'estado_poliza') },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin'), vin: true },
  ],
  // El prompt de `rtm` llegó en HU #10977 pero su resumen nunca se añadió aquí, así que el panel salía
  // con el encabezado y la grilla vacía. Los campos son los que pide el certificado de vigencia.
  // HU #11998 — paz y salvo de impuestos. El resumen antepone la DEUDA: es el dato por el que se
  // pide el documento. El emisor va justo después porque es lo que distingue un paz y salvo legítimo
  // de un recibo de caja de la secretaría de tránsito, que se le parece mucho.
  paz_salvo: [
    { label: 'Estado', value: (d) => ESTADO_DEUDA[pickStr(d, 'estado_deuda')] ?? pickStr(d, 'estado_deuda') },
    { label: 'Vigencias adeudadas', value: (d) => pickStr(d, 'vigencias_adeudadas') },
    { label: 'Emisor', value: (d) => pickStr(d, 'emisor') },
    { label: 'N.º certificado', value: (d) => pickStr(d, 'numero_certificado') },
    { label: 'Placa', value: (d) => pickStr(d, 'vehiculo_placa') },
    { label: 'Vehículo', value: (d) => joinFields(d, ['vehiculo_marca', 'vehiculo_linea', 'vehiculo_modelo']) },
    { label: 'Propietario', value: (d) => pickStr(d, 'propietario_nombre') },
    { label: 'Ubicación', value: (d) => joinFields(d, ['municipio', 'departamento'], ', ') },
    { label: 'Periodo certificado', value: (d) => pickStr(d, 'vigencia_certificada') },
    { label: 'Expedición', value: (d) => pickStr(d, 'fecha_expedicion') },
  ],
  // HU #12037 — Certificado CEPD. El resumen antepone el combustible y las emisiones, que es por lo
  // que se pide el documento, y luego el vehículo homologado para cotejarlo con el trámite.
  // NO se muestra la cilindrada a propósito: se midió un 16 % de error real sobre un dato que el
  // trámite ya tiene, y mostrarla mal invitaría a «corregir» el trámite con un valor peor.
  certificado_ambiental: [
    { label: 'Combustible', value: (d) => pickStr(d, 'combustible') },
    { label: 'Emisiones', value: (d) => (d.tiene_seccion_emisiones ? 'Sección presente' : 'Sin sección de emisiones') },
    { label: 'Prueba dinámica', value: (d) => joinFields(d, ['emisiones_co_dinamica', 'emisiones_hc_dinamica', 'emisiones_nox_dinamica'], ' · ') },
    { label: 'Opacidad (diésel)', value: (d) => pickStr(d, 'opacidad_diesel') },
    { label: 'N.º de ficha', value: (d) => pickStr(d, 'numero_ficha') },
    { label: 'Homologación', value: (d) => pickStr(d, 'tipo_homologacion') },
    { label: 'Vehículo', value: (d) => joinFields(d, ['vehiculo_marca', 'vehiculo_referencia', 'vehiculo_modelo']) },
    { label: 'Clase', value: (d) => joinFields(d, ['clase_vehiculo', 'tipo_carroceria'], ' — ') },
    { label: 'Certifica', value: (d) => pickStr(d, 'certificado_por') },
  ],
  // HU #12030 — certificado de cámara de comercio. El resumen antepone NIT y razón social, que es lo
  // que el gestor coteja contra la empresa que figura como parte, y después el representante legal,
  // que es quien puede firmar por ella. La vigencia se informa, nunca bloquea.
  camara_comercio: [
    { label: 'NIT', value: (d) => pickStr(d, 'nit') },
    { label: 'Razón social', value: (d) => pickStr(d, 'razon_social') },
    { label: 'Representante legal', value: (d) => joinFields(d, ['representante_legal_nombre', 'representante_legal_cargo'], ' — ') },
    { label: 'Estado', value: (d) => ESTADO_SOCIEDAD[pickStr(d, 'estado_sociedad')] ?? pickStr(d, 'estado_sociedad') },
    { label: 'Expedición', value: (d) => pickStr(d, 'fecha_expedicion') },
    { label: 'Último año renovado', value: (d) => pickStr(d, 'ultimo_ano_renovado') },
    { label: 'Matrícula mercantil', value: (d) => pickStr(d, 'matricula_mercantil') },
    { label: 'Cámara', value: (d) => pickStr(d, 'camara_emisora') },
    { label: 'Domicilio', value: (d) => pickStr(d, 'domicilio') },
  ],
  // HU #12001 — contrato de leasing. El resumen antepone las dos partes, que es lo que define el
  // trámite: el vehículo quedará a nombre del arrendador y el locatario se registra aparte.
  // NO se muestra el NIT del arrendador a propósito: la carátula no lo trae —trae la cédula del
  // representante— y en la medición el modelo lo inventaba. Un dato que nadie ve no induce a error.
  contrato_leasing: [
    { label: 'Arrendador', value: (d) => pickStr(d, 'arrendador_nombre') },
    { label: 'Locatario', value: (d) => pickStr(d, 'locatario_nombre') },
    { label: 'N.º de contrato', value: (d) => pickStr(d, 'numero_contrato') },
    { label: 'Fecha', value: (d) => pickStr(d, 'fecha_contrato') },
    { label: 'Bien', value: (d) => pickStr(d, 'vehiculo_descripcion') },
    { label: 'Vehículo', value: (d) => joinFields(d, ['vehiculo_marca', 'vehiculo_linea', 'vehiculo_modelo']) },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin') },
    { label: 'Proveedor', value: (d) => pickStr(d, 'proveedor_nombre') },
  ],
  // HU #12000 — comprobante de pago. El resumen antepone si YA ESTÁ PAGADO y el valor: es lo que el
  // gestor necesita saber de un vistazo. Una liquidación sin pagar es válida, así que el estado se
  // informa aparte del veredicto del OCR.
  comprobante_derechos: [
    { label: 'Estado', value: (d) => (d.hay_constancia_pago ? 'Pagado' : 'Liquidado, sin constancia de pago') },
    { label: 'Valor', value: (d) => pickStr(d, 'valor_total') },
    { label: 'Entidad', value: (d) => pickStr(d, 'entidad_recaudadora') },
    { label: 'Conceptos', value: (d) => pickStr(d, 'conceptos') },
    { label: 'Referencia', value: (d) => pickStr(d, 'numero_referencia') },
    { label: 'Fecha', value: (d) => pickStr(d, 'fecha_pago') },
    { label: 'Placa', value: (d) => pickStr(d, 'vehiculo_placa') },
    { label: 'Propietario', value: (d) => pickStr(d, 'propietario_nombre') },
    { label: 'Ubicación', value: (d) => joinFields(d, ['municipio', 'departamento'], ', ') },
  ],
  // HU #11999 — inscripción de prenda. El resumen antepone el ACREEDOR porque es el dato por el que
  // se pide el documento: el gestor lo coteja contra el acreedor registrado en el trámite. La placa va
  // después y a menudo viene vacía, porque muchos contratos identifican el vehículo solo por chasis.
  inscripcion_prenda: [
    { label: 'Acreedor', value: (d) => pickStr(d, 'acreedor_nombre') },
    { label: 'NIT del acreedor', value: (d) => pickStr(d, 'acreedor_documento') },
    { label: 'Garante', value: (d) => pickStr(d, 'garante_nombre') },
    { label: 'N.º de registro', value: (d) => pickStr(d, 'numero_registro') },
    { label: 'Fecha de registro', value: (d) => pickStr(d, 'fecha_registro') },
    { label: 'Placa', value: (d) => pickStr(d, 'vehiculo_placa') },
    { label: 'Chasis', value: (d) => pickStr(d, 'vehiculo_chasis') },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin') },
    { label: 'Vehículo', value: (d) => joinFields(d, ['vehiculo_marca', 'vehiculo_linea', 'vehiculo_modelo']) },
  ],
  // HU #11996 — licencia de tránsito. El resumen prioriza lo que el gestor coteja de un vistazo
  // contra el trámite: placa y VIN primero, y el organismo que la expidió, que es lo que distingue
  // una licencia legítima de un recibo de la misma secretaría.
  tarjeta_propiedad: [
    { label: 'Placa', value: (d) => pickStr(d, 'vehiculo_placa') },
    { label: 'N.º licencia', value: (d) => pickStr(d, 'numero_licencia') },
    { label: 'Vehículo', value: (d) => joinFields(d, ['vehiculo_marca', 'vehiculo_linea', 'vehiculo_modelo']) },
    { label: 'Color', value: (d) => pickStr(d, 'vehiculo_color') },
    { label: 'Servicio', value: (d) => pickStr(d, 'vehiculo_servicio') },
    { label: 'Motor', value: (d) => pickStr(d, 'vehiculo_motor') },
    { label: 'Propietario', value: (d) => pickStr(d, 'propietario_nombre') },
    { label: 'Identificación', value: (d) => joinFields(d, ['propietario_tipo_documento', 'propietario_documento'], ' ') },
    { label: 'Organismo', value: (d) => pickStr(d, 'organismo_transito') },
    { label: 'Expedición', value: (d) => pickStr(d, 'fecha_expedicion') },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin'), vin: true },
  ],
  rtm: [
    { label: 'N.º certificado', value: (d) => pickStr(d, 'numero_certificado') },
    { label: 'CDA', value: (d) => pickStr(d, 'cda_expide') },
    { label: 'Expedición', value: (d) => pickStr(d, 'fecha_expedicion') },
    { label: 'Vencimiento', value: (d) => pickStr(d, 'fecha_vencimiento') },
    { label: 'Estado', value: (d) => pickStr(d, 'estado') },
    { label: 'Resultado', value: (d) => pickStr(d, 'resultado') },
    { label: 'VIN', value: (d) => pickStr(d, 'vehiculo_vin'), vin: true },
  ],
};

/** Nombre corto del tipo para el encabezado de la tarjeta. */
/**
 * HU #12047 — códigos que son el MISMO documento y comparten etiqueta y resumen. Espejo de
 * `AliasReconocer` en el backend (HU #12045): `prenda_registro` es el DocTipo del adjunto que exige
 * la decisión «registrar» y `inscripcion_prenda` la casilla del catálogo.
 *
 * Sin esto el panel salía con el código crudo por título y la rejilla VACÍA —el mismo fallo que ya
 * tuvo `rtm`, ver el comentario en OCR_RESUMEN_FIELDS—: el análisis corría y se pagaba, pero al
 * gestor no se le enseñaba ni el acreedor ni el NIT, que son el dato por el que se pide el documento.
 */
const TIPO_ALIAS: Record<string, string> = {
  prenda_registro: 'inscripcion_prenda',
};

/** El código del que cuelgan la etiqueta y el resumen de este tipo. */
export function tipoCanonico(tipo: string): string {
  return TIPO_ALIAS[tipo] ?? tipo;
}

const TIPO_LABEL: Record<string, string> = {
  factura: 'Factura',
  aduana: 'Aduana',
  impronta: 'Impronta',
  soat: 'SOAT',
  rtm: 'RTM',
  tarjeta_propiedad: 'Licencia de Tránsito',
  paz_salvo: 'Paz y Salvo de Impuestos',
  inscripcion_prenda: 'Inscripción de Prenda',
  comprobante_derechos: 'Comprobante de pago',
  contrato_leasing: 'Contrato de Leasing',
  camara_comercio: 'Certificado de Cámara de Comercio',
  certificado_ambiental: 'Certificado CEPD',
};

/** Nombre legible de un tipo de documento OCR; el propio código si no está en el mapa. */
export function tipoLabel(tipo: string): string {
  return TIPO_LABEL[tipoCanonico(tipo)] ?? tipo;
}

/**
 * Tono semántico del chip de estado (coincide / no_coincide / no_aplica / no_verificado).
 *
 * B4 (guardián de diseño) — antes pintaba con hex propios fuera de la paleta autorizada
 * (`#3B8A00` 3.8:1, `#B77900` 3.2:1, `#F9AC00` fuera de token). Ahora resuelve un tono de
 * `StatusBadge`, la única fuente de color de estado del sistema.
 */
function ocrFieldTone(value: string): StatusTone {
  const v = value.toLowerCase();
  if (v === 'coincide') return 'success';
  if (v === 'no_coincide') return 'danger';
  if (v === 'no_aplica') return 'neutral';
  return 'warning'; // no_verificado / otros
}

/** Chip "PDF recortado (X/Y págs)" cuando el OCR recortó un subconjunto de páginas. */
function recorteLabel(data: Record<string, unknown> | null): string | null {
  if (!data || data._paginas_extraidas !== true) return null;
  const extraidas = Array.isArray(data.paginas_documento) ? data.paginas_documento.length : null;
  const originales = typeof data._paginas_originales === 'number' ? data._paginas_originales : null;
  return extraidas && originales ? `PDF recortado (${extraidas}/${originales} págs)` : 'PDF recortado';
}

/**
 * Indicador OCR compacto: icono con color por estado + tooltip al pasar el mouse.
 * El detalle (campos, motivo) vive en tooltip/modal — no agranda el contenedor de upload.
 * Exportada para cargue individual y revisión masiva.
 */
export function OcrStatusPanel({ tipo, ocr }: { tipo: string; ocr: OcrUiResult }) {
  const [detailOpen, setDetailOpen] = useState(false);
  const [tipOpen, setTipOpen] = useState(false);
  // B5 (guardián de diseño) — trampa de foco + retorno de foco + Escape del modal de detalle OCR.
  const ocrDialogRef = useRef<HTMLDivElement>(null);
  useWizardFocusTrap(ocrDialogRef, {
    active: detailOpen,
    onEscape: () => setDetailOpen(false),
  });
  // B4 (guardián de diseño) — antes traía hex propios fuera de la paleta autorizada (`#3B8A00`
  // 3.8:1, `#B77900` 3.2:1, `#F9AC00` fuera de token). El tono sale de `--badge-*`
  // (`globals.css`), la misma fuente que usa `StatusBadge`, y queda theme-aware de paso.
  const ocrTone: StatusTone =
    ocr.status === 'verified' ? 'success' : ocr.status === 'rejected' ? 'danger' : 'warning';
  const palette = {
    color: `var(--badge-${ocrTone}-fg)`,
    border: `var(--badge-${ocrTone}-border)`,
    bg: `var(--badge-${ocrTone}-bg)`,
    label:
      ocr.status === 'verified'
        ? 'Verificado'
        : ocr.status === 'rejected'
          ? 'Rechazado'
          : 'No analizado',
  };

  const data = ocr.data;
  const nombre = tipoLabel(tipo);
  const tipoDocumento = data ? pickStr(data, 'tipo_documento') : '';
  const recorte = recorteLabel(data);
  const rechazoPorVin = ocr.status === 'rejected' && !!ocr.motivo && /VIN/i.test(ocr.motivo);

  const fields = data
    ? (OCR_RESUMEN_FIELDS[tipoCanonico(tipo)] ?? [])
        .map((field) => {
          const value = field.value(data);
          return { field, value: field.vin ? resumirVins(value) : value };
        })
        .filter((x) => x.value !== '')
    : [];

  // HU #12038 — el modelo declara si pudo LEER el documento o si estaba completando. Medido: sobre
  // las 7 fichas giradas en las que inventaba, las 7 dijeron «parcial» y ninguna «buena»; de las 56
  // derechas, 54 dijeron «buena». No señala qué campo concreto falla, así que el aviso es sobre el
  // conjunto: los valores de abajo pueden no ser fiables.
  const avisoLegibilidad = data ? (LEGIBILIDAD_AVISO[pickStr(data, 'legibilidad')] ?? null) : null;

  const tipId = `ocr-tip-${tipo}`;

  return (
    <div className="relative shrink-0">
      <button
        type="button"
        className="inline-flex h-7 w-7 items-center justify-center rounded-full"
        style={{ background: palette.color, color: '#FFFFFF', boxShadow: '0 1px 2px rgba(0,0,0,0.18)' }}
        aria-label={`OCR ${nombre}: ${palette.label}. Ver detalle`}
        aria-describedby={tipOpen ? tipId : undefined}
        aria-expanded={detailOpen}
        onMouseEnter={() => setTipOpen(true)}
        onMouseLeave={() => setTipOpen(false)}
        onFocus={() => setTipOpen(true)}
        onBlur={() => setTipOpen(false)}
        onClick={() => setDetailOpen(true)}
      >
        <Info className="h-4 w-4" strokeWidth={2.5} aria-hidden />
      </button>

      {tipOpen && !detailOpen && (
        <div
          id={tipId}
          role="tooltip"
          className="absolute right-0 z-40 mt-1.5 w-56 rounded-xl border bg-white p-2.5 text-left shadow-lg dark:bg-[#162744]"
          style={{ borderColor: palette.border }}
        >
          <p className="text-xs font-bold" style={{ color: palette.color }}>
            OCR — {palette.label}
          </p>
          {ocr.motivo && (
            <p className="mt-1 text-xs leading-snug opacity-80">{ocr.motivo}</p>
          )}
          {(tipoDocumento || recorte) && (
            <p className="mt-1 text-xs opacity-70">
              {[tipoDocumento, recorte].filter(Boolean).join(' · ')}
            </p>
          )}
          {fields.length > 0 && (
            <p className="mt-1.5 text-xs font-medium" style={{ color: '#557EFF' }}>
              Clic para ver {fields.length} campo{fields.length === 1 ? '' : 's'}
            </p>
          )}
        </div>
      )}

      {detailOpen && (
        <div
          // B6 (guardián de diseño) — overlay único del sistema: `rgba(22,39,68,0.45)` +
          // `backdrop-blur-[6px]` (antes `bg-black/40 backdrop-blur-sm`, uno de los cuatro
          // overlays distintos del asistente).
          className="fixed inset-0 z-50 grid place-items-center bg-[rgba(22,39,68,0.45)] px-4 backdrop-blur-[6px]"
          role="dialog"
          aria-modal="true"
          aria-labelledby={`ocr-detail-title-${tipo}`}
          onClick={() => setDetailOpen(false)}
        >
          <div
            ref={ocrDialogRef}
            tabIndex={-1}
            className="max-h-[80vh] w-full max-w-lg overflow-y-auto rounded-2xl border bg-white p-4 shadow-xl outline-none focus:ring-0 dark:bg-[#162744]"
            style={{ borderColor: palette.border }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="mb-3 flex items-start justify-between gap-2">
              <div className="flex items-center gap-2">
                <span
                  className="inline-flex h-7 w-7 items-center justify-center rounded-full"
                  style={{ background: palette.color, color: '#FFFFFF' }}
                  aria-hidden
                >
                  <Info className="h-4 w-4" strokeWidth={2.5} />
                </span>
                <h3
                  id={`ocr-detail-title-${tipo}`}
                  className="text-sm font-semibold"
                  style={{ color: '#162744' }}
                >
                  OCR — {nombre}
                  <span className="mt-0.5 block text-xs font-bold" style={{ color: palette.color }}>
                    {palette.label}
                  </span>
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setDetailOpen(false)}
                className="rounded-lg px-2 py-1 text-xs font-medium opacity-70 hover:opacity-100"
              >
                Cerrar
              </button>
            </div>
            {ocr.motivo && (
              <p className="mb-2 text-xs font-medium" style={{ color: palette.color }}>
                {ocr.motivo}
              </p>
            )}
            {(tipoDocumento || recorte) && (
              <p className="mb-2 text-xs opacity-70">
                {[tipoDocumento, recorte].filter(Boolean).join(' · ')}
              </p>
            )}
            {avisoLegibilidad && (
              <p className="mb-2 text-xs font-medium" style={{ color: '#B77900' }}>
                {avisoLegibilidad}
              </p>
            )}
            {fields.length === 0 ? (
              <p className="text-xs opacity-70">Sin campos adicionales detectados.</p>
            ) : (
              <dl className="grid grid-cols-1 gap-x-4 gap-y-2 sm:grid-cols-2">
                {fields.map(({ field, value }) => (
                  <div key={field.label} className="flex items-baseline gap-1.5 text-xs">
                    <dt className="shrink-0 opacity-70">{field.label}:</dt>
                    {field.kind === 'state' ? (
                      <dd>
                        <StatusBadge tone={ocrFieldTone(value)} label={value.replace(/_/g, ' ')} />
                      </dd>
                    ) : (
                      <dd
                        className={`min-w-0 wrap-break-word ${field.strong ? 'font-bold' : ''}`}
                        style={
                          field.vin && rechazoPorVin ? { color: '#FF4E00', fontWeight: 700 } : undefined
                        }
                      >
                        {value}
                      </dd>
                    )}
                  </div>
                ))}
              </dl>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * Caja de subida por ítem del checklist. Presentacional + un input de archivo
 * oculto; valida cliente-side antes de delegar la subida al hook.
 * Exportada para reutilizar el mismo diseño en la sección Prenda.
 */
export function DocumentSlot({
  item,
  attachment,
  uploading,
  analyzing,
  deleting,
  ocr,
  onUpload,
  onRemove,
  onGenerateImpronta,
  onPreview,
  permiteGenerarImprontaAutomatica = true,
}: {
  item: ChecklistItemView;
  attachment: ProcedureAttachment | undefined;
  uploading: boolean;
  analyzing: boolean;
  deleting: boolean;
  ocr: OcrUiResult | undefined;
  onUpload: (file: File) => void;
  onRemove: (attachmentId: string) => void;
  /** Genera la impronta (Kyverum) y la adjunta al trámite. */
  onGenerateImpronta?: () => Promise<void>;
  /** Abre el modal de previsualización para este adjunto. */
  onPreview?: (attachment: ProcedureAttachment) => void;
  /** Si false, no se ofrece generar (tipo parametrizado en MANUAL). */
  permiteGenerarImprontaAutomatica?: boolean;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [generating, setGenerating] = useState(false);
  const readOnly = useWizardReadOnly();

  const tipo = item.docTipo ?? item.key;
  const caption = catalogDocumentTitle(tipo, item.label);
  const done = item.satisfied || !!attachment;
  const busy = uploading || analyzing || deleting || generating;
  const isAuto = AUTO_DOC_TIPOS.has(tipo);
  const isImpronta = tipo.toLowerCase() === 'impronta';
  const ocrRejected = ocr?.status === 'rejected';
  const showValidado = done && !ocrRejected;
  const canGenerate =
    isImpronta &&
    permiteGenerarImprontaAutomatica &&
    !!onGenerateImpronta &&
    !attachment &&
    !readOnly;

  const handleGenerate = async () => {
    if (!onGenerateImpronta) return;
    setLocalError(null);
    setGenerating(true);
    try {
      await onGenerateImpronta();
    } catch (err) {
      setLocalError(describeGenerarImprontaEnTramite(err));
    } finally {
      setGenerating(false);
    }
  };

  const handlePick = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // permite re-seleccionar el mismo archivo
    if (!file) return;
    // Pre-validación cliente con los límites del tipo (RF08/09): así el error sale inline y con el
    // límite real (no el global de 20 MB) y no llega al backend / cuadro global.
    const err = validateFile(file, {
      allowedMimes: item.mimeTypesAllowed,
      maxSizeBytes: item.maxSizeBytes,
    });
    setLocalError(err);
    if (err) return;
    onUpload(file);
  };

  return (
    <li
      className={
        // Filas del checklist (`DocumentChecklist`): `basis` fija el mismo tope de columnas que
        // antes (1 / 2 / 4 / 6 según viewport) y `grow` es lo que falta para que una fila
        // incompleta —p. ej. 3 documentos en un checklist con hueco para 6— reparta el espacio
        // sobrante entre las tarjetas reales en vez de dejarlo en blanco. En los contenedores que
        // reutilizan `DocumentSlot` dentro de un `<ul className="grid grid-cols-1">` (escritura del
        // representante, prenda) estas clases de flex no aplican: no hay flex container que las lea.
        //
        // SIN `h-full`: con el `<ul>` en `flex-wrap` (no `grid`), su alto es automático —lo fija el
        // contenido— y un hijo con `height:100%` no tiene contra qué resolverse, así que el navegador
        // termina colapsándolo a su propio contenido. Es lo que igualaba mal la altura: la tarjeta
        // «Certificado de Aduana» (sin nombre de archivo ni barra de progreso) quedaba más baja que
        // sus vecinas. Quitar la altura explícita deja actuar el `align-items: stretch` por defecto
        // del flex container, que sí iguala cada tarjeta a la más alta de su misma fila.
        'relative flex grow shrink-0 basis-full flex-col rounded-xl bg-white p-4 shadow-sm transition hover:shadow-md dark:bg-[#162744] md:basis-[calc(50%-0.5rem)] xl:basis-[calc(25%-0.75rem)] 2xl:basis-[calc(16.6667%-0.8333rem)] ' +
        (isAuto || (done && !ocrRejected)
          ? 'border'
          : 'border-2 border-dashed hover:border-[#557EFF] hover:bg-[#F0F5FF]')
      }
      style={{ borderColor: '#E2E8F0' }}
    >
      {/* Badges esquina superior derecha (prototipo DocSlot). */}
      <div className="absolute right-3 top-3 flex items-center gap-1.5">
        {showValidado ? (
          <span
            className="whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-semibold text-white"
            style={{ background: '#8CC63F' }}
          >
            Cargado
          </span>
        ) : ocrRejected ? (
          <StatusBadge tone="danger" label="No coincide" />
        ) : item.obligatorio ? (
          <span className="whitespace-nowrap rounded-full bg-red-50 px-2.5 py-0.5 text-xs font-medium text-red-600">
            Por cargar
          </span>
        ) : (
          <span className="text-xs font-medium opacity-60">Opcional</span>
        )}
      </div>

      <p
        className="pr-28 text-[13px] font-semibold leading-tight"
        style={{ color: '#162744' }}
      >
        <DocumentCatalogCaption nombre={item.label} codigo={tipo} />
      </p>
      {isAuto && (
        <p className="mt-0.5 text-[11px] opacity-70">Autogenerado por el sistema</p>
      )}
      <p className="mt-1 text-[11px] opacity-70">PDF, JPG hasta 5MB</p>
      {attachment && !isAuto && (
        <p className="mt-1 truncate text-[11px] opacity-60">
          {attachment.filename} · {formatSize(attachment.sizeBytes)}
        </p>
      )}

            {(done || analyzing || generating) && (
        <div
          className="mt-3 h-1.5 w-full overflow-hidden rounded-full"
          style={{ background: '#F1F5F9' }}
          aria-hidden="true"
        >
          <div
              className={`h-full rounded-full ${analyzing || generating ? 'animate-pulse' : ''}`}
              style={{
                width: analyzing || generating ? '60%' : '100%',
                background: analyzing || generating ? '#557EFF' : ocrRejected ? 'var(--badge-danger-fg)' : '#8CC63F',
              }}
          />
        </div>
      )}

      {ocr && !analyzing ? (
        <div className="mt-2">
          <OcrStatusPanel tipo={tipo} ocr={ocr} />
        </div>
      ) : null}

      <div className="mt-auto flex flex-wrap items-center gap-2 pt-4">
        {isAuto ? (
          <p className="text-[11px] italic opacity-60">
            Autogenerado por el sistema al radicar.
          </p>
        ) : readOnly ? (
          attachment && onPreview ? (
            <button
              type="button"
              onClick={() => onPreview(attachment)}
              className="text-xs font-semibold"
              style={{ color: '#557EFF' }}
              aria-label={`Previsualizar ${caption}`}
            >
              Ver
            </button>
          ) : null
        ) : (
          <>
            {attachment && onPreview && (
              <button
                type="button"
                onClick={() => onPreview(attachment)}
                className="rounded-lg border p-1 disabled:opacity-60"
                style={{ color: '#557EFF' }}
                aria-label={`Previsualizar ${caption}`}
              >
                <Eye className="h-3.5 w-3.5" aria-hidden="true" />
              </button>
            )}
            <input
              ref={inputRef}
              type="file"
              accept={ALLOWED_MIME.join(',')}
              onChange={handlePick}
              className="hidden"
              aria-label={`Subir ${caption}`}
            />
            <button
              type="button"
              onClick={() => inputRef.current?.click()}
              disabled={busy}
              className="h-9 rounded-lg border bg-white px-4 text-[12px] font-semibold transition hover:bg-[#EFF6FF] disabled:cursor-not-allowed disabled:opacity-50 dark:bg-transparent"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
            >
              {analyzing
                ? 'Analizando documento...'
                : uploading
                  ? 'Subiendo…'
                  : attachment
                    ? 'Reemplazar archivo'
                    : 'Adjuntar archivo'}
            </button>
            {canGenerate && (
              <button
                type="button"
                onClick={() => void handleGenerate()}
                disabled={busy}
                className="h-9 rounded-lg px-4 text-[12px] font-semibold text-white transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
                style={{ background: '#557EFF' }}
              >
                {generating ? 'Generando impronta…' : 'Generar impronta'}
              </button>
            )}
            {attachment && (
              <button
                type="button"
                onClick={() => onRemove(attachment.id)}
                disabled={busy}
                className="text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-60"
                style={{ color: '#FF4E00' }}
                aria-label={`Borrar ${caption}`}
              >
                {deleting ? 'Borrando…' : 'Borrar'}
              </button>
            )}
          </>
        )}
      </div>

      {localError && (
        <p
          className="mt-1.5 text-xs"
          style={{ color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {localError}
        </p>
      )}
    </li>
  );
}

/**
 * Grid de subida de documentos guiado por el checklist del trámite.
 * Reutilizable: dado un `instanceId`, carga el checklist (qué docTipos exige
 * la tipología) y los adjuntos, marca ✓ los satisfechos, valida mime/tamaño
 * antes de subir y resume "faltan N obligatorios / completo".
 */
export function DocumentChecklist({
  instanceId,
  onChanged,
  hideHeader = false,
  modalidad,
  uploadMode: uploadModeProp,
  onUploadModeChange,
  hideModeToggle = false,
  permiteGenerarImprontaAutomatica = true,
}: Props) {
  const { state, refresh, upload, remove, clearError } = useProcedureDocuments(instanceId);
  const { checklist, attachments, uploadingTipos, analyzingTipos, deletingId, ocrResults } =
    state;

  // Feature #11211 — modos mutuamente excluyentes: individual (checklist) vs batch (masivo).
  const [uploadModeState, setUploadModeState] = useState<DocumentUploadMode>('individual');
  const uploadMode = uploadModeProp ?? uploadModeState;
  const setUploadMode = (mode: DocumentUploadMode) => {
    if (uploadModeProp === undefined) setUploadModeState(mode);
    onUploadModeChange?.(mode);
  };
  const batch = useProcedureBatchUpload(instanceId, { modalidad });
  const readOnly = useWizardReadOnly();

  // Se indexa en minúsculas porque los dos extremos no guardan el código igual: el `docTipo` del
  // checklist conserva el casing con que se creó el tipo en el módulo Documental, y el `tipo` del
  // adjunto lo persiste el backend en minúsculas. Emparejar tal cual dejaba la casilla vacía —y al
  // gestor reintentando— con un documento que en realidad ya estaba cargado.
  // HU #12046 — cuando hay más de uno del mismo tipo gana el MÁS RECIENTE. Antes ganaba el primero de
  // la lista, así que tras «Reemplazar archivo» la casilla seguía enseñando el documento viejo. El
  // backend ya no acumula, pero los expedientes creados antes del arreglo sí traen duplicados, y las
  // bolsas (`otro`, anexos) siguen admitiendo varios legítimamente.
  const attachmentByTipo = new Map<string, ProcedureAttachment>();
  for (const a of attachments) {
    const key = a.tipo.toLowerCase();
    const previo = attachmentByTipo.get(key);
    if (!previo || a.uploadedAt >= previo.uploadedAt) attachmentByTipo.set(key, a);
  }

  // Prenda se carga en PrendaForm (`prenda_*` / `inscripcion_prenda`); no duplicar aquí.
  const items = (checklist?.items ?? []).filter((item) => {
    const tipo = item.docTipo ?? item.key;
    return !isPrendaManagedChecklistTipo(tipo);
  });
  const showModeToggle =
    !hideModeToggle && !readOnly && !!instanceId && items.length > 0;

  // Preview modal state (HU #10703)
  const [previewAttachment, setPreviewAttachment] = useState<ProcedureAttachment | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  const handlePreview = async (attachment: ProcedureAttachment) => {
    setPreviewAttachment(attachment);
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewError(null);
    setPreviewLoading(true);
    try {
      const result = await tramitesClient.fetchAttachmentPreviewUrl(
        instanceId ?? '',
        attachment.id,
      );
      // El file-manager sirve el objeto como binary/octet-stream sin Content-Disposition, por lo que
      // un <iframe> con la URL directa fuerza descarga. Re-empaquetamos los bytes como Blob con el
      // mimetype real para forzar el render inline en el navegador (S3 permite CORS GET).
      const blob = await fetch(result.url).then((r) => {
        if (!r.ok) throw new Error(String(r.status));
        return r.blob();
      });
      const typed = attachment.mimetype ? new Blob([blob], { type: attachment.mimetype }) : blob;
      setPreviewUrl(URL.createObjectURL(typed));
    } catch {
      setPreviewError('No se pudo obtener la URL de previsualización. Descarga el archivo en su lugar.');
    } finally {
      setPreviewLoading(false);
    }
  };

  const closePreview = () => {
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setPreviewAttachment(null);
    setPreviewError(null);
  };

  const handleDownloadFromPreview = async () => {
    if (!instanceId || !previewAttachment) return;
    try {
      const { blob, filename, mimetype } = await tramitesClient.downloadAttachment(
        instanceId,
        previewAttachment.id,
        undefined,
        previewAttachment.filename,
      );
      const objectUrl = URL.createObjectURL(new Blob([blob], { type: mimetype }));
      const a = document.createElement('a');
      a.href = objectUrl;
      a.download = filename;
      a.click();
      URL.revokeObjectURL(objectUrl);
    } catch {
      // silencioso — el usuario puede reintentar desde el listado
    }
  };

  // El OCR de la carga masiva analiza el lote entero y puede tardar bastante; mientras corre no hay
  // nada útil que hacer en la pantalla, así que la propuesta la cubre con la escena de espera. El
  // análisis de un documento suelto NO la levanta: su tarjeta ya muestra "Analizando…" con su barra,
  // y tapar la pantalla entera por un archivo impediría seguir adjuntando los demás.
  const analizandoLote = batch.state.phase === 'analyzing';

  return (
    <>
    {analizandoLote && (
      <CarLoaderModal
        mode="ocr"
        label={
          // El lote va archivo por archivo, así que la espera puede decir por dónde va. Con un solo
          // archivo el contador sobra y se deja el mensaje de siempre.
          batch.state.progreso && batch.state.progreso.total > 1
            ? `Analizando expediente ${Math.min(batch.state.progreso.hechos + 1, batch.state.progreso.total)} de ${batch.state.progreso.total}…`
            : undefined
        }
      />
    )}
    <DocumentPreviewModal
      open={!!previewAttachment}
      onClose={closePreview}
      title={previewAttachment?.filename ?? 'Previsualización'}
      mimetype={previewAttachment?.mimetype ?? null}
      url={previewUrl}
      loading={previewLoading}
      error={previewError}
      onDownload={previewAttachment ? () => void handleDownloadFromPreview() : undefined}
    />
    <section
      className={
        hideHeader
          ? ''
          : 'rounded-2xl border bg-white p-4 dark:bg-[#162744]'
      }
      aria-label="Documentos del trámite"
    >
      {!hideHeader && (
        <WizardCardHeader
          title="Gestión de documentos"
          action={
            showModeToggle ? (
              <div className="flex flex-wrap items-center gap-3">
                {uploadMode === 'batch' && (
                  <StatusBadge label="Clasificación automática" tone="info" />
                )}
                <DocumentUploadModeToggle value={uploadMode} onChange={setUploadMode} />
              </div>
            ) : undefined
          }
        />
      )}

      {checklist &&
        (() => {
          const tono = INLINE_ALERT_TONES[checklist.completo ? 'success' : 'warning'];
          const Icono = tono.Icon;
          return (
            <div
              className="mb-3 flex items-center gap-2.5 rounded-xl px-4 py-3"
              style={{ background: tono.background, border: `1px solid ${tono.border}` }}
              role="status"
              aria-live="polite"
            >
              <Icono className="h-4 w-4 shrink-0" style={{ color: tono.color }} aria-hidden="true" />
              <span className="text-xs font-bold" style={{ color: tono.color }}>
                {checklist.completo
                  ? 'Documentos completos'
                  : `Faltan ${checklist.faltanObligatorios} obligatorio${
                      checklist.faltanObligatorios === 1 ? '' : 's'
                    }`}
              </span>
            </div>
          );
        })()}

      {state.error && (
        <div
          className="mb-3 flex items-center justify-between gap-3 rounded-xl border p-3 text-xs"
          style={{
            borderColor: '#FF4E00',
            background: 'rgba(255,78,0,0.06)',
            color: '#FF4E00',
          }}
          role="alert"
          aria-live="polite"
        >
          <span>{state.error}</span>
          <button
            type="button"
            onClick={clearError}
            className="font-bold"
            aria-label="Descartar error"
          >
            ×
          </button>
        </div>
      )}

      {/* Toggle interno solo si no vive en la cabecera del acordeón. */}
      {showModeToggle && (
        <div className="mb-3 flex flex-wrap items-center justify-end gap-3">
          {uploadMode === 'batch' && (
            <StatusBadge label="Clasificación automática" tone="info" />
          )}
          <DocumentUploadModeToggle value={uploadMode} onChange={setUploadMode} />
        </div>
      )}

      {uploadMode === 'batch' && !readOnly && !!instanceId && items.length > 0 && (
        <div className="mb-4">
          {/* Sin «IA» ni «slot»: lo primero es jerga de cómo está hecho el sistema y lo segundo es
              inglés en una frase en español —la propia interfaz lo llama «casilla» en el resto de
              textos—. La última frase es la que faltaba: sin ella, el panel de revisión que aparece
              a continuación sorprende, y el gestor cree que ya se adjuntó algo. */}
          <p className="mb-3 text-[12px] opacity-70">
            Carga todos los documentos juntos y el sistema los reparte en la casilla que le
            corresponde a cada uno, incluidos los de trámites simultáneos. Revisas el reparto antes
            de que se adjunte nada.
          </p>
          {batch.state.phase === 'reviewing' || batch.state.phase === 'uploading' ? (
            <BatchReviewPanel
              state={batch.state}
              aceptadas={batch.aceptadas}
              onToggle={batch.setDecision}
              onCancel={batch.reset}
              onConfirm={() =>
                void batch.confirm().then((ok) => {
                  void refresh();
                  onChanged?.();
                  return ok;
                })
              }
            />
          ) : (
            <BatchDropzone
              busy={batch.state.phase === 'analyzing'}
              onFiles={(files) => void batch.analyze(files, items, attachments)}
            />
          )}

          {batch.state.error && (
            <div
              className="mt-2 flex items-center justify-between gap-3 rounded-xl border p-3 text-xs"
              style={{
                borderColor: '#FF4E00',
                background: 'rgba(255,78,0,0.06)',
                color: '#FF4E00',
              }}
              role="alert"
              aria-live="polite"
            >
              <span>{batch.state.error}</span>
              <button
                type="button"
                onClick={batch.clearError}
                className="font-bold"
                aria-label="Descartar error de la carga masiva"
              >
                ×
              </button>
            </div>
          )}
        </div>
      )}

      {items.length === 0 ? (
        <p className="text-xs opacity-70">
          {state.loading
            ? 'Cargando documentos requeridos…'
            : 'Este trámite no requiere documentos.'}
        </p>
      ) : (
        <ul
          className="mt-1 flex flex-wrap gap-4"
          aria-label="Checklist de documentos"
        >
          {[...items]
            .sort((a, b) => {
              if (a.obligatorio !== b.obligatorio) return a.obligatorio ? -1 : 1;
              return a.label.localeCompare(b.label, 'es', { sensitivity: 'base' });
            })
            .map((item) => {
            const tipo = item.docTipo ?? item.key;
            const attachment = attachmentByTipo.get(tipo.toLowerCase());
            return (
              <DocumentSlot
                key={item.key}
                item={item}
                attachment={attachment}
                uploading={uploadingTipos.has(tipo)}
                analyzing={analyzingTipos.has(tipo)}
                deleting={!!attachment && deletingId === attachment.id}
                ocr={ocrResultForTipo(ocrResults, tipo)}
                onUpload={(file) =>
                  void upload(tipo, file).then((ok) => {
                    if (ok) onChanged?.();
                  })
                }
                onRemove={(id) =>
                  void remove(id).then((ok) => {
                    if (ok) onChanged?.();
                  })
                }
                onGenerateImpronta={
                  instanceId && permiteGenerarImprontaAutomatica
                    ? async () => {
                        await tramitesClient.generarImpronta(instanceId);
                        await refresh();
                        onChanged?.();
                      }
                    : undefined
                }
                permiteGenerarImprontaAutomatica={permiteGenerarImprontaAutomatica}
                onPreview={instanceId ? (att) => void handlePreview(att) : undefined}
              />
            );
          })}
        </ul>
      )}
    </section>
    </>
  );
}
