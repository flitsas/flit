import type {
  ConsolidadoVigencia,
  GenerarConsolidadoResult,
} from '@/lib/api/types/procedure-runtime';
import { formatFechaHora } from '@/lib/format/date';

/**
 * HU #12799 (Épica #12760) — lógica PURA del aviso «la regeneración del consolidado falló».
 *
 * Contrato del backend (#12798): cuando regenerar el consolidado falla y existe un PDF anterior, la
 * ruta de entrega (`GET …/consolidado/entrega`, gestor y OT) y el POST de generación del wizard
 * responden con ÉXITO trayendo ese PDF anterior, `regenerado: false` y un aviso
 * `"<documento>: <causa>"` en `avisosCascada` (`"consolidado: adjunto_no_disponible"`,
 * `"consolidado_maestro: excepcion"`). El POST del maestro OT (`…/consolidado-maestro`) NO tiene ese
 * respaldo: si la reconstrucción falla responde con error, no con el PDF anterior. No hay un campo de
 * «último fallo»: el fallo se reconoce SOLO en la respuesta de la apertura o de la generación.
 *
 * `avisosCascada` ya existía (HU #11050) para los documentos de la cascada (`"impronta: …"`,
 * `"fur: …"`): esos NO son fallo del consolidado. Solo cuentan los prefijos `consolidado:` y
 * `consolidado_maestro:`.
 */

export type DocumentoFalloConsolidado = 'consolidado' | 'consolidado_maestro';

const DOCUMENTOS_FALLO: ReadonlySet<string> = new Set(['consolidado', 'consolidado_maestro']);

/**
 * Copy amigable por código de causa (códigos del generador, `ConsolidadoCommand` /
 * `ConsolidadoMaestroCommand` + `ConsolidadoFalloBitacora.CausaExcepcion`). Un código fuera del mapa
 * cae en {@link CAUSA_FALLO_GENERICA}: nunca se muestra el código crudo.
 */
export const CAUSAS_FALLO_CONSOLIDADO: Readonly<Record<string, string>> = {
  adjunto_no_disponible: 'un documento del expediente no está disponible en el almacenamiento',
  storage_unavailable: 'el almacenamiento de documentos no respondió',
  sin_adjuntos: 'el expediente no tiene documentos para consolidar',
  mimetype_no_soportado: 'uno de los documentos tiene un formato que no se puede incluir',
  excepcion: 'ocurrió un error inesperado al generar el documento',
};

export const CAUSA_FALLO_GENERICA = 'ocurrió un error al generar el documento';

export const COPY_FALLO_REGENERACION = {
  titulo: {
    consolidado: 'No se pudo regenerar el consolidado',
    consolidado_maestro: 'No se pudo regenerar el consolidado maestro',
  },
  reintentar: 'Reintentar',
  ariaReintentar: {
    consolidado: 'Reintentar la regeneración del consolidado',
    consolidado_maestro: 'Reintentar la regeneración del consolidado maestro',
  },
} as const;

export interface FalloRegeneracionConsolidado {
  /** Qué consolidado falló (prefijo del aviso). */
  documento: DocumentoFalloConsolidado;
  /** Código de causa tal cual llegó (solo para lógica/tests; NO se pinta). */
  causa: string;
  /** Causa traducida a texto amigable. */
  causaTexto: string;
}

/** Documento del aviso: lo que va antes del primer `:` (`"fur: x"` → `"fur"`). */
function documentoDeAviso(aviso: string): string {
  return (aviso.split(':')[0] ?? '').trim();
}

/** `true` si el aviso de cascada es el fallo del propio consolidado (wizard o maestro). */
export function esAvisoFalloConsolidado(aviso: string | null | undefined): boolean {
  return typeof aviso === 'string' && DOCUMENTOS_FALLO.has(documentoDeAviso(aviso));
}

/** Avisos de cascada SIN los del fallo del consolidado (los de otros documentos siguen igual). */
export function avisosSinFalloConsolidado(avisos: readonly string[] | null | undefined): string[] {
  return (avisos ?? []).filter((a) => !esAvisoFalloConsolidado(a));
}

/** Texto amigable de un código de causa; genérico si no está en el mapa. */
export function textoCausaFallo(causa: string | null | undefined): string {
  const codigo = (causa ?? '').trim();
  return (codigo && CAUSAS_FALLO_CONSOLIDADO[codigo]) || CAUSA_FALLO_GENERICA;
}

/**
 * Detecta el fallo de regeneración en la respuesta de la apertura o la generación: `regenerado`
 * EXACTAMENTE `false` y un aviso con prefijo `consolidado:` / `consolidado_maestro:`. Cualquier otra
 * combinación (regenerado, reutilizado sin aviso, avisos solo de otros documentos) → `null`. La
 * entrega `modo: "radicado_fijo"` (maestro radicado en Quipux) nunca intenta regenerar: nunca es fallo.
 *
 * Uso de ejemplo:
 *   detectarFalloRegeneracion({ regenerado: false, avisosCascada: ['consolidado: excepcion'] })
 *   // → { documento: 'consolidado', causa: 'excepcion', causaTexto: 'ocurrió un error inesperado…' }
 */
export function detectarFalloRegeneracion(
  res:
    | (Pick<GenerarConsolidadoResult, 'regenerado' | 'avisosCascada'> &
        Partial<Pick<GenerarConsolidadoResult, 'modo'>>)
    | null
    | undefined,
): FalloRegeneracionConsolidado | null {
  if (!res || res.regenerado !== false || res.modo === 'radicado_fijo') return null;
  const aviso = (res.avisosCascada ?? []).find(esAvisoFalloConsolidado);
  if (!aviso) return null;
  const causa = aviso.split(':').slice(1).join(':').trim();
  return {
    documento: documentoDeAviso(aviso) as DocumentoFalloConsolidado,
    causa,
    causaTexto: textoCausaFallo(causa),
  };
}

/**
 * Detalle del aviso: causa + que el PDF mostrado NO es el más reciente + fecha del PDF conservado en
 * hora Colombia. Sin fecha fiable (backend sin vigencia) se omite la fecha, no se inventa.
 */
export function textoAvisoFalloRegeneracion(
  fallo: Pick<FalloRegeneracionConsolidado, 'causaTexto'>,
  generadoEn: string | null | undefined,
): string {
  const fecha = formatFechaHora(generadoEn, '');
  const causa = fallo.causaTexto.charAt(0).toUpperCase() + fallo.causaTexto.slice(1);
  const conservado = fecha
    ? `El PDF que se muestra no es el más reciente: es la versión generada el ${fecha} (hora Colombia).`
    : 'El PDF que se muestra no es el más reciente: es la última versión disponible.';
  return `${causa}. ${conservado}`;
}

/** Modos de la entrega que garantizan que el PDF servido refleja el expediente actual. */
const MODOS_ENTREGA_VIGENTE: ReadonlySet<string> = new Set(['vigente', 'regenerado']);

/**
 * HU #12793 / #12799 — vigencia local de un consolidado (wizard o maestro) tras abrirlo o
 * regenerarlo, sin pedir de nuevo el detalle.
 *
 * - Fallo de regeneración (#12799): el PDF conservado NO refleja el expediente ⇒ `desactualizado`
 *   (gris) con la `generadoEn` previa, que es la fecha del PDF conservado. Nunca pasa a vigente.
 * - Generación (POST, `modo` omitido) o entrega `vigente`/`regenerado`: vigente; si se reconstruyó la
 *   fecha es `ahora` (aproximación cliente del sello del backend), si se reutilizó, la previa.
 * - `null` (no tocar el indicador) sin vigencia previa o cuando la entrega sirvió el adjunto tal cual
 *   (`solo_lectura`, `radicado_fijo`, definitivo, migrado, cargado por usuario). En `radicado_fijo`
 *   el maestro radicado NO pasa a vigente con la hora local: su fecha es la de la radicación.
 *
 * Uso de ejemplo:
 *   const nueva = vigenciaTrasApertura(vigencia, res, new Date());
 *   if (nueva) setVigenciaLocal(nueva);
 */
export function vigenciaTrasApertura(
  previa: ConsolidadoVigencia | null | undefined,
  res: Pick<GenerarConsolidadoResult, 'regenerado' | 'modo' | 'avisosCascada'>,
  ahora: Date,
): ConsolidadoVigencia | null {
  if (!previa) return null;
  if (detectarFalloRegeneracion(res)) {
    return { ...previa, estado: 'desactualizado', definitivo: false };
  }
  // `modo` null/omitido = ruta POST de generación: deja el consolidado vigente.
  if (res.modo != null && !MODOS_ENTREGA_VIGENTE.has(res.modo)) return null;
  const regenerado = res.regenerado === true || res.modo === 'regenerado';
  const generadoEn = regenerado ? ahora.toISOString() : previa.generadoEn;
  return {
    ...previa,
    estado: 'vigente',
    generadoEn,
    origen: regenerado ? 'system' : previa.origen,
  };
}
