/**
 * HU #12793 (Épica #12760) — lógica pura del indicador del consolidado MAESTRO en la consola OT.
 *
 * Uso de ejemplo:
 *   describirVigenciaConsolidado(maestro, { documento: 'maestro', radicadoEn: row.quipuxRadicadoEn })
 *   // → { estado: 'radicado', etiqueta: 'Versión radicada', textoFecha: 'Radicado el 20/09/2026 09:30' }
 *   vigenciaMaestroTrasApertura(maestro, { regenerado: true, modo: null }, new Date())
 *   // → { estado: 'vigente', generadoEn: <ahora>, … }
 */
import { describe, expect, it } from 'vitest';
import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import {
  COLOR_VIGENCIA_DESACTUALIZADO,
  COLOR_VIGENCIA_VIGENTE,
  COPY_VIGENCIA,
  TINTA_VIGENCIA_VIGENTE,
  describirVigenciaConsolidado,
} from '@/lib/tramites/vigencia-consolidado';
import { vigenciaMaestroTrasApertura } from '@/lib/tramites/consolidado-entrega-ot';

// 15:05 UTC = 10:05 Bogotá; 14:30 UTC = 09:30 Bogotá.
const GENERADO = '2026-09-23T15:05:00Z';
const RADICADO = '2026-09-20T14:30:00Z';
const LEYENDA_OT = 'Se reconstruirá al abrirlo';

function maestro(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: 'vigente',
    generadoEn: GENERADO,
    origen: 'system',
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

describe('HU #12793 AC1 — maestro vigente', () => {
  it('verde con la fecha y hora de generación del maestro', () => {
    const v = describirVigenciaConsolidado(maestro(), { documento: 'maestro' });
    expect(v!.estado).toBe('vigente');
    expect(v!.documento).toBe('Consolidado maestro');
    expect(v!.colorPunto).toBe(COLOR_VIGENCIA_VIGENTE);
    expect(v!.textoFecha).toBe('Generado el 23/09/2026 10:05');
  });

  it('radicadoEn vacío, solo espacios o null no altera el comportamiento de #12792', () => {
    const base = describirVigenciaConsolidado(maestro(), { documento: 'maestro' });
    expect(
      describirVigenciaConsolidado(maestro(), { documento: 'maestro', radicadoEn: '  ' }),
    ).toEqual(base);
    expect(
      describirVigenciaConsolidado(maestro(), { documento: 'maestro', radicadoEn: null }),
    ).toEqual(base);
  });
});

describe('HU #12793 AC2 — maestro desactualizado', () => {
  it('gris con la leyenda de la consola OT («se reconstruirá al abrirlo»)', () => {
    const v = describirVigenciaConsolidado(maestro({ estado: 'desactualizado' }), {
      documento: 'maestro',
      leyendaDesactualizado: LEYENDA_OT,
    });
    expect(v!.estado).toBe('desactualizado');
    expect(v!.colorPunto).toBe(COLOR_VIGENCIA_DESACTUALIZADO);
    expect(v!.leyenda).toBe(LEYENDA_OT);
    expect(v!.ariaLabel).toContain('se reconstruirá al abrirlo');
  });
});

describe('HU #12793 AC3 — versión radicada (read-only)', () => {
  it('manda sobre un maestro desactualizado: versión radicada, fecha de radicación, sin regenerar', () => {
    const v = describirVigenciaConsolidado(maestro({ estado: 'desactualizado' }), {
      documento: 'maestro',
      leyendaDesactualizado: LEYENDA_OT,
      radicadoEn: RADICADO,
    });
    expect(v!.estado).toBe('radicado');
    expect(v!.etiqueta).toBe(COPY_VIGENCIA.etiqueta.radicado);
    expect(v!.fecha).toBe('20/09/2026 09:30');
    expect(v!.textoFecha).toBe('Radicado el 20/09/2026 09:30');
    expect(v!.leyenda).toBeNull();
    expect(v!.definitivo).toBe(false);
    expect(v!.colorPunto).toBe(COLOR_VIGENCIA_VIGENTE);
    expect(v!.colorTexto).toBe(TINTA_VIGENCIA_VIGENTE);
    const todo = [v!.etiqueta, v!.leyenda, v!.textoFecha, v!.ariaLabel].join(' ');
    expect(todo).not.toMatch(/regener|reconstru/i);
  });

  it('se pinta aunque el backend no exponga la vigencia (null/undefined)', () => {
    const opts = { documento: 'maestro' as const, radicadoEn: RADICADO };
    expect(describirVigenciaConsolidado(null, opts)!.estado).toBe('radicado');
    expect(describirVigenciaConsolidado(undefined, opts)!.estado).toBe('radicado');
  });

  it('fecha de radicación ilegible ⇒ versión radicada sin fecha, sin lanzar', () => {
    const v = describirVigenciaConsolidado(maestro(), {
      documento: 'maestro',
      radicadoEn: 'no-es-fecha',
    });
    expect(v!.estado).toBe('radicado');
    expect(v!.fecha).toBeNull();
    expect(v!.textoFecha).toBeNull();
  });

  it('contrato de la vista radicada: mismas claves que la vista de #12792', () => {
    const radicado = describirVigenciaConsolidado(maestro(), {
      documento: 'maestro',
      radicadoEn: RADICADO,
    });
    const vigente = describirVigenciaConsolidado(maestro(), { documento: 'maestro' });
    expect(Object.keys(radicado!).sort()).toEqual(Object.keys(vigente!).sort());
  });
});

describe('HU #12793 — refresco tras abrir/reconstruir (vigenciaMaestroTrasApertura)', () => {
  const AHORA = new Date('2026-09-23T20:00:00Z');

  it('POST que reconstruye ⇒ vigente con la fecha de ahora', () => {
    const r = vigenciaMaestroTrasApertura(
      maestro({ estado: 'desactualizado' }),
      { regenerado: true },
      AHORA,
    );
    expect(r).toMatchObject({ estado: 'vigente', generadoEn: AHORA.toISOString(), origen: 'system' });
  });

  it('entrega que reutiliza (modo vigente) ⇒ vigente con la fecha previa', () => {
    const r = vigenciaMaestroTrasApertura(maestro(), { regenerado: false, modo: 'vigente' }, AHORA);
    expect(r).toMatchObject({ estado: 'vigente', generadoEn: GENERADO });
  });

  it('sin vigencia previa (backend sin el campo) ⇒ null: la UI no infiere', () => {
    expect(vigenciaMaestroTrasApertura(null, { regenerado: true }, AHORA)).toBeNull();
    expect(vigenciaMaestroTrasApertura(undefined, { regenerado: true }, AHORA)).toBeNull();
  });

  it.each([
    'solo_lectura',
    'definitivo_estado_final',
    'migrado_solo_lectura',
    'cargado_por_usuario',
  ] as const)('entrega en modo %s ⇒ null (no se sabe si refleja el expediente)', (modo) => {
    expect(
      vigenciaMaestroTrasApertura(maestro({ estado: 'desactualizado' }), { modo }, AHORA),
    ).toBeNull();
  });
});
