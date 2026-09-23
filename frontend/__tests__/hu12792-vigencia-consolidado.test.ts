/**
 * HU #12792 (Épica #12760) — lógica pura del indicador de vigencia del consolidado.
 *
 * Uso de ejemplo:
 *   describirVigenciaConsolidado({ estado: 'vigente', generadoEn: '2026-09-23T15:05:00Z',
 *     origen: 'system', definitivo: false, modo: null })
 *   // → { etiqueta: 'Vigente', fecha: '23/09/2026 10:05', colorPunto: '#70CF3A', … }
 */
import { describe, expect, it } from 'vitest';
import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import {
  COLOR_VIGENCIA_DESACTUALIZADO,
  COLOR_VIGENCIA_VIGENTE,
  COPY_VIGENCIA,
  describirVigenciaConsolidado,
} from '@/lib/tramites/vigencia-consolidado';

// 15:05 UTC = 10:05 en Bogotá (UTC−5).
const GENERADO = '2026-09-23T15:05:00Z';

function vigencia(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: 'vigente',
    generadoEn: GENERADO,
    origen: 'system',
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

describe('HU #12792 AC1 — vigente', () => {
  it('verde #70CF3A con la fecha y hora de la última generación en hora Colombia', () => {
    const v = describirVigenciaConsolidado(vigencia());
    expect(v).not.toBeNull();
    expect(v!.estado).toBe('vigente');
    expect(v!.etiqueta).toBe('Vigente');
    expect(v!.colorPunto).toBe(COLOR_VIGENCIA_VIGENTE);
    expect(v!.colorPunto).toBe('#70CF3A');
    expect(v!.fecha).toBe('23/09/2026 10:05');
    expect(v!.textoFecha).toBe('Generado el 23/09/2026 10:05');
    expect(v!.definitivo).toBe(false);
  });

  it('estado final (definitivo=true) se muestra como vigente con la marca «Definitivo»', () => {
    const v = describirVigenciaConsolidado(
      vigencia({ definitivo: true, modo: 'definitivo_estado_final' }),
    );
    expect(v!.estado).toBe('vigente');
    expect(v!.colorPunto).toBe('#70CF3A');
    expect(v!.definitivo).toBe(true);
    expect(v!.ariaLabel).toContain('definitivo');
  });

  it('vigente sin generadoEn: no inventa fecha', () => {
    const v = describirVigenciaConsolidado(vigencia({ generadoEn: null }));
    expect(v!.fecha).toBeNull();
    expect(v!.textoFecha).toBeNull();
  });

  it('el texto NO usa el verde de marca como tinta (contraste < 4.5:1)', () => {
    const v = describirVigenciaConsolidado(vigencia());
    expect(v!.colorTexto).not.toBe('#70CF3A');
    expect(v!.colorTexto).toBe('var(--flit-success-ink)');
  });
});

describe('HU #12792 AC2 — desactualizado', () => {
  it('gris #59677D con la leyenda de pendiente de regenerar', () => {
    const v = describirVigenciaConsolidado(vigencia({ estado: 'desactualizado' }));
    expect(v!.colorPunto).toBe(COLOR_VIGENCIA_DESACTUALIZADO);
    expect(v!.colorPunto).toBe('#59677D');
    expect(v!.etiqueta).toBe('Desactualizado');
    expect(v!.leyenda).toBe('Pendiente de regenerar');
    expect(v!.textoFecha).toBe('Última generación: 23/09/2026 10:05');
  });

  it('leyendaDesactualizado sobrescribe el texto (caso consola OT, #12793)', () => {
    const v = describirVigenciaConsolidado(vigencia({ estado: 'desactualizado' }), {
      documento: 'maestro',
      leyendaDesactualizado: 'Se reconstruirá al abrirlo',
    });
    expect(v!.leyenda).toBe('Se reconstruirá al abrirlo');
    expect(v!.documento).toBe('Consolidado maestro');
  });

  it('leyenda en blanco cae al texto por defecto', () => {
    const v = describirVigenciaConsolidado(vigencia({ estado: 'desactualizado' }), {
      leyendaDesactualizado: '   ',
    });
    expect(v!.leyenda).toBe(COPY_VIGENCIA.leyendaDesactualizado);
  });

  it('desactualizado nunca es «Definitivo» aunque el backend lo marque', () => {
    const v = describirVigenciaConsolidado(vigencia({ estado: 'desactualizado', definitivo: true }));
    expect(v!.definitivo).toBe(false);
  });
});

describe('HU #12792 AC3 — inexistente', () => {
  it('informa que aún no se ha generado y no muestra fecha', () => {
    const v = describirVigenciaConsolidado(
      vigencia({ estado: 'inexistente', generadoEn: null, origen: null }),
    );
    expect(v!.leyenda).toBe('Aún no se ha generado');
    expect(v!.fecha).toBeNull();
    expect(v!.textoFecha).toBeNull();
    expect(v!.puntoHueco).toBe(true);
  });

  it('aunque llegue un generadoEn espurio, no pinta fecha', () => {
    const v = describirVigenciaConsolidado(vigencia({ estado: 'inexistente' }));
    expect(v!.fecha).toBeNull();
    expect(v!.ariaLabel).not.toMatch(/\d{2}\/\d{2}\/\d{4}/);
  });
});

describe('HU #12792 — contrato: sin dato no hay indicador', () => {
  it.each([undefined, null])('devuelve null con %s (backend anterior al campo)', (valor) => {
    expect(describirVigenciaConsolidado(valor)).toBeNull();
  });

  it('devuelve null con un estado desconocido (no infiere)', () => {
    expect(
      describirVigenciaConsolidado({ ...vigencia(), estado: 'otro' as never }),
    ).toBeNull();
  });

  it('una fecha corrupta no rompe: sin fecha', () => {
    const v = describirVigenciaConsolidado(vigencia({ generadoEn: 'no-es-fecha' }));
    expect(v!.fecha).toBeNull();
  });

  it('la vista expone todas las propiedades del contrato', () => {
    const v = describirVigenciaConsolidado(vigencia())!;
    for (const k of [
      'estado',
      'documento',
      'etiqueta',
      'leyenda',
      'fecha',
      'textoFecha',
      'definitivo',
      'origen',
      'colorPunto',
      'puntoHueco',
      'colorTexto',
      'ariaLabel',
    ]) {
      expect(v).toHaveProperty(k);
    }
  });
});

describe('HU #12792 AC5 — el estado se comunica por texto (aria-label)', () => {
  it.each([
    ['vigente', /consolidado: vigente, generado el 23\/09\/2026 10:05/i],
    ['desactualizado', /consolidado: desactualizado, pendiente de regenerar/i],
    ['inexistente', /consolidado: aún no se ha generado/i],
  ] as const)('%s', (estado, patron) => {
    const v = describirVigenciaConsolidado(vigencia({ estado }))!;
    expect(v.ariaLabel).toMatch(patron);
    expect(v.etiqueta.length).toBeGreaterThan(0);
  });
});
