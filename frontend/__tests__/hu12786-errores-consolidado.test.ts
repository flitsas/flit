/**
 * Épica #12760 — security B2: los errores del backend del consolidado se traducen a copy amigable;
 * el código o el `detail` crudo nunca se pintan. Incluye el `modo` nuevo `radicado_fijo` de la
 * entrega OT (versión radicada: sin aviso de fallo y sin refrescar la vigencia con la hora local).
 *
 * Uso de ejemplo:
 *   mensajeErrorConsolidadoAmigable(new Error('fur_requerido'))
 *   // → 'El trámite aún no tiene el FUR generado: genéralo antes de consolidar el expediente.'
 *   esEntregaRadicadaFija({ modo: 'radicado_fijo' }) // → true
 */
import { describe, expect, it } from 'vitest';
import { ApiError } from '@/lib/api/types';
import { TramitesApiError } from '@/lib/api/tramites-client';
import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import {
  ERROR_CONSOLIDADO_GENERICO,
  ERROR_CONSOLIDADO_SERVICIO,
  ERROR_CONSOLIDADO_SIN_PERMISO,
  ERRORES_CONSOLIDADO,
  codigoErrorConsolidado,
  mensajeErrorConsolidadoAmigable,
} from '@/lib/tramites/errores-consolidado';
import { mensajeErrorConsolidado } from '@/lib/tramites/useAperturaConsolidado';
import { esEntregaRadicadaFija } from '@/lib/tramites/consolidado-entrega-ot';
import {
  detectarFalloRegeneracion,
  vigenciaTrasApertura,
} from '@/lib/tramites/fallo-regeneracion-consolidado';

const AHORA = new Date('2026-09-23T16:00:00Z');

function vigencia(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: 'desactualizado',
    generadoEn: '2026-09-20T15:05:00Z',
    origen: 'system',
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

describe('mensajeErrorConsolidadoAmigable — códigos conocidos', () => {
  it.each([
    ['fur_requerido', /FUR generado/],
    ['documentos_incompletos', /Faltan documentos obligatorios/],
    ['modalidad_no_soportada', /no admite expediente consolidado/],
    ['consolidado_no_generado', /aún no tiene consolidado generado/],
    ['generacion_bloqueada_estado_final', /documentación es definitiva/],
    ['force_no_permitido_en_get', /No se pudo abrir el consolidado/],
    ['storage_unavailable', /almacenamiento de documentos no respondió/],
  ])('%s → copy amigable sin el código crudo', (codigo, patron) => {
    const texto = mensajeErrorConsolidadoAmigable(new Error(codigo));
    expect(texto).toMatch(patron);
    expect(texto).not.toContain(codigo);
  });

  it('reconoce el código dentro del mensaje («409 Conflict: fur_requerido») y en el ProblemDetails', () => {
    expect(codigoErrorConsolidado(new Error('409 Conflict: fur_requerido'))).toBe('fur_requerido');
    const problem = new TramitesApiError(400, 'Solicitud inválida', {
      title: 'force_no_permitido_en_get',
    });
    expect(codigoErrorConsolidado(problem)).toBe('force_no_permitido_en_get');
    const apiError = new ApiError(409, 'x', { code: 'documentos_incompletos' });
    expect(codigoErrorConsolidado(apiError)).toBe('documentos_incompletos');
  });

  it('compara palabra completa: un código parecido no se confunde', () => {
    expect(codigoErrorConsolidado(new Error('fur_requerido_extra'))).toBeNull();
  });
});

describe('mensajeErrorConsolidadoAmigable — respaldo por estado y genérico', () => {
  it('403 sin código conocido (gestor pide tipo=consolidado_maestro) → sin permiso', () => {
    const err = new TramitesApiError(403, 'Forbidden: tipo consolidado_maestro', null);
    expect(mensajeErrorConsolidadoAmigable(err)).toBe(ERROR_CONSOLIDADO_SIN_PERMISO);
  });

  it('400 con código desconocido → respaldo del llamador, nunca el texto crudo', () => {
    const err = new TramitesApiError(400, 'tipo_consolidado_maestro_no_admitido', null);
    const texto = mensajeErrorConsolidadoAmigable(err, 'Respaldo del llamador.');
    expect(texto).toBe('Respaldo del llamador.');
  });

  it('404 → sin consolidado generado; 503 y 0 → servicio no disponible', () => {
    expect(mensajeErrorConsolidadoAmigable(new ApiError(404, 'Not Found'))).toBe(
      ERRORES_CONSOLIDADO.consolidado_no_generado,
    );
    expect(mensajeErrorConsolidadoAmigable(new ApiError(503, 'upstream'))).toBe(ERROR_CONSOLIDADO_SERVICIO);
    expect(mensajeErrorConsolidadoAmigable(new ApiError(0, 'net'))).toBe(ERROR_CONSOLIDADO_SERVICIO);
  });

  it('edge: null, string y objetos raros no lanzan y dan el genérico', () => {
    expect(() => mensajeErrorConsolidadoAmigable(null)).not.toThrow();
    expect(mensajeErrorConsolidadoAmigable(null)).toBe(ERROR_CONSOLIDADO_GENERICO);
    expect(mensajeErrorConsolidadoAmigable('fur_requerido')).toMatch(/FUR generado/);
    expect(mensajeErrorConsolidadoAmigable({ status: 'x' })).toBe(ERROR_CONSOLIDADO_GENERICO);
  });

  it('el mensaje de la apertura/«Re-generar» del visor ya no devuelve el texto crudo', () => {
    const texto = mensajeErrorConsolidado(new Error('System.NullReferenceException at Foo'));
    expect(texto).not.toMatch(/NullReference/);
    expect(texto).toMatch(/No se pudo generar el consolidado/);
  });
});

describe('modo radicado_fijo (entrega OT) — versión radicada', () => {
  it('esEntregaRadicadaFija reconoce solo ese modo', () => {
    expect(esEntregaRadicadaFija({ modo: 'radicado_fijo' })).toBe(true);
    expect(esEntregaRadicadaFija({ modo: 'solo_lectura' })).toBe(false);
    expect(esEntregaRadicadaFija({ modo: null })).toBe(false);
    expect(esEntregaRadicadaFija(null)).toBe(false);
  });

  it('nunca es fallo de regeneración, aunque traiga regenerado=false y un aviso del maestro', () => {
    expect(
      detectarFalloRegeneracion({
        regenerado: false,
        modo: 'radicado_fijo',
        avisosCascada: ['consolidado_maestro: excepcion'],
      }),
    ).toBeNull();
  });

  it('no refresca la vigencia a vigente con la hora local (devuelve null: no se toca el indicador)', () => {
    expect(
      vigenciaTrasApertura(vigencia(), { regenerado: false, modo: 'radicado_fijo' }, AHORA),
    ).toBeNull();
    expect(
      vigenciaTrasApertura(
        vigencia(),
        { regenerado: false, modo: 'radicado_fijo', avisosCascada: ['consolidado_maestro: excepcion'] },
        AHORA,
      ),
    ).toBeNull();
  });
});

// Uso de ejemplo: mensajeErrorConsolidadoAmigable(new TramitesApiError(409, 'Conflict', { error: 'adjunto_protegido' }))
describe('re-review #12760 (M-N1) — adjunto_protegido del DELETE de adjuntos', () => {
  it('traduce el 409 adjunto_protegido del ProblemDetails a copy amigable', () => {
    const err = new TramitesApiError(409, 'Conflict', {
      title: 'Conflict',
      status: 409,
      detail: 'Este documento lo genera el sistema y no se puede eliminar.',
      error: 'adjunto_protegido',
    });
    expect(codigoErrorConsolidado(err)).toBe('adjunto_protegido');
    expect(mensajeErrorConsolidadoAmigable(err)).toBe(
      'Este documento lo genera el sistema y no se puede eliminar.',
    );
  });

  it('el código viene en el mapa y nunca se pinta crudo', () => {
    expect(ERRORES_CONSOLIDADO.adjunto_protegido).toBe(
      'Este documento lo genera el sistema y no se puede eliminar.',
    );
    expect(mensajeErrorConsolidadoAmigable(new Error('409: adjunto_protegido'))).not.toContain(
      'adjunto_protegido',
    );
  });
});
