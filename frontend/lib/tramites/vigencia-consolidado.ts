import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import { formatFechaHora } from '@/lib/format/date';

/**
 * HU #12792 (Épica #12760) — lógica PURA del indicador de vigencia de un consolidado: qué texto,
 * qué color y qué fecha mostrar para una {@link ConsolidadoVigencia}. La consume
 * `components/shared/IndicadorVigenciaConsolidado.tsx` y está pensada para reutilizarse en la
 * consola OT (#12793), el SuperAdmin (#12794) y el aviso de fallo (#12799) sin duplicar copy.
 *
 * Colores (tokens `flit_design_tokens.json`):
 * - `state.success` #70CF3A — vigente. Solo como RELLENO del punto: sobre blanco no llega a 4.5:1
 *   como texto, así que el rótulo va en `--flit-success-ink` (#4F7A12, 5.09:1).
 * - `state.draft` / `text.secondary` #59677D — desactualizado (≈5.7:1 sobre blanco: vale como texto).
 * - `state.muted` #7D8798 — contorno del punto hueco de «inexistente»; el texto sigue en #59677D.
 *
 * El color nunca es el único portador del estado: cada vista trae `etiqueta` + `ariaLabel`.
 */

export const COLOR_VIGENCIA_VIGENTE = '#70CF3A';
export const COLOR_VIGENCIA_DESACTUALIZADO = '#59677D';
export const COLOR_VIGENCIA_INEXISTENTE = '#7D8798';
/** Tinta legible del verde (token CSS del tema, AA en claro y en oscuro). */
export const TINTA_VIGENCIA_VIGENTE = 'var(--flit-success-ink)';
/** Texto secundario FLIT: AA sobre blanco. */
export const TINTA_VIGENCIA_NEUTRA = '#59677D';

export type DocumentoConsolidado = 'wizard' | 'maestro';

export const COPY_VIGENCIA = {
  documento: {
    wizard: 'Consolidado',
    maestro: 'Consolidado maestro',
  },
  etiqueta: {
    vigente: 'Vigente',
    desactualizado: 'Desactualizado',
    inexistente: 'Sin generar',
  },
  leyendaDesactualizado: 'Pendiente de regenerar',
  leyendaInexistente: 'Aún no se ha generado',
  prefijoFecha: 'Generado el',
  prefijoUltimaGeneracion: 'Última generación',
  definitivo: 'Definitivo',
} as const;

export interface OpcionesVigencia {
  documento?: DocumentoConsolidado;
  /** Override de la leyenda del estado `desactualizado` (p. ej. OT: «se reconstruirá al abrirlo»). */
  leyendaDesactualizado?: string;
}

/** Vista ya resuelta: todo lo que el componente necesita pintar, sin lógica adicional. */
export interface VistaVigenciaConsolidado {
  estado: ConsolidadoVigencia['estado'];
  /** «Consolidado» / «Consolidado maestro». */
  documento: string;
  /** Rótulo corto del estado: «Vigente», «Desactualizado», «Sin generar». */
  etiqueta: string;
  /** Leyenda secundaria: «Pendiente de regenerar», «Aún no se ha generado», o `null` en vigente. */
  leyenda: string | null;
  /** Fecha/hora de la última generación en hora Colombia (`DD/MM/YYYY HH:mm`); `null` si no hay. */
  fecha: string | null;
  /** Texto de la fecha con su prefijo («Generado el …» / «Última generación: …»); `null` si no hay. */
  textoFecha: string | null;
  /** Trámite en estado final: el PDF ya no se regenera. Solo aplica a `vigente`. */
  definitivo: boolean;
  /** Origen del PDF tal como lo expone el backend (lo usa #12794 para el caso manual). */
  origen: ConsolidadoVigencia['origen'];
  /** Relleno/contorno del punto indicador. */
  colorPunto: string;
  /** `true` = punto hueco (solo contorno): refuerza «no existe» sin depender del tono. */
  puntoHueco: boolean;
  /** Color del texto del rótulo, con contraste AA. */
  colorTexto: string;
  /** Nombre accesible completo: documento + estado + fecha/leyenda + definitivo. */
  ariaLabel: string;
}

const ESTADOS_VALIDOS: ReadonlySet<string> = new Set(['vigente', 'desactualizado', 'inexistente']);

/**
 * Resuelve la vista del indicador. Devuelve `null` cuando no hay dato fiable (backend anterior al
 * campo: `undefined`/`null`, o un `estado` desconocido): la UI NO infiere la vigencia.
 *
 * Uso de ejemplo:
 *   describirVigenciaConsolidado({ estado: 'vigente', generadoEn: '2026-09-23T15:05:00Z', … })
 *   // → { etiqueta: 'Vigente', fecha: '23/09/2026 10:05', colorPunto: '#70CF3A', … }
 */
export function describirVigenciaConsolidado(
  vigencia: ConsolidadoVigencia | null | undefined,
  opciones: OpcionesVigencia = {},
): VistaVigenciaConsolidado | null {
  if (!vigencia || !ESTADOS_VALIDOS.has(vigencia.estado)) return null;

  const documento = COPY_VIGENCIA.documento[opciones.documento ?? 'wizard'];
  const estado = vigencia.estado;
  // «Inexistente» no muestra fecha aunque el backend mande una (AC3).
  const fechaFormateada =
    estado === 'inexistente' ? '' : formatFechaHora(vigencia.generadoEn, '');
  const fecha = fechaFormateada === '' ? null : fechaFormateada;
  const definitivo = estado === 'vigente' && vigencia.definitivo === true;

  if (estado === 'vigente') {
    const textoFecha = fecha ? `${COPY_VIGENCIA.prefijoFecha} ${fecha}` : null;
    return {
      estado,
      documento,
      etiqueta: COPY_VIGENCIA.etiqueta.vigente,
      leyenda: null,
      fecha,
      textoFecha,
      definitivo,
      origen: vigencia.origen ?? null,
      colorPunto: COLOR_VIGENCIA_VIGENTE,
      puntoHueco: false,
      colorTexto: TINTA_VIGENCIA_VIGENTE,
      ariaLabel: unir([
        `${documento}: ${COPY_VIGENCIA.etiqueta.vigente.toLowerCase()}`,
        textoFecha ? textoFecha.charAt(0).toLowerCase() + textoFecha.slice(1) : null,
        definitivo ? COPY_VIGENCIA.definitivo.toLowerCase() : null,
      ]),
    };
  }

  if (estado === 'desactualizado') {
    const leyenda = opciones.leyendaDesactualizado?.trim() || COPY_VIGENCIA.leyendaDesactualizado;
    const textoFecha = fecha ? `${COPY_VIGENCIA.prefijoUltimaGeneracion}: ${fecha}` : null;
    return {
      estado,
      documento,
      etiqueta: COPY_VIGENCIA.etiqueta.desactualizado,
      leyenda,
      fecha,
      textoFecha,
      definitivo: false,
      origen: vigencia.origen ?? null,
      colorPunto: COLOR_VIGENCIA_DESACTUALIZADO,
      puntoHueco: false,
      colorTexto: TINTA_VIGENCIA_NEUTRA,
      ariaLabel: unir([
        `${documento}: ${COPY_VIGENCIA.etiqueta.desactualizado.toLowerCase()}`,
        leyenda.toLowerCase(),
        textoFecha ? textoFecha.charAt(0).toLowerCase() + textoFecha.slice(1) : null,
      ]),
    };
  }

  return {
    estado,
    documento,
    etiqueta: COPY_VIGENCIA.etiqueta.inexistente,
    leyenda: COPY_VIGENCIA.leyendaInexistente,
    fecha: null,
    textoFecha: null,
    definitivo: false,
    origen: vigencia.origen ?? null,
    colorPunto: COLOR_VIGENCIA_INEXISTENTE,
    puntoHueco: true,
    colorTexto: TINTA_VIGENCIA_NEUTRA,
    ariaLabel: `${documento}: ${COPY_VIGENCIA.leyendaInexistente.toLowerCase()}`,
  };
}

function unir(partes: Array<string | null>): string {
  return partes.filter((p): p is string => !!p).join(', ');
}
