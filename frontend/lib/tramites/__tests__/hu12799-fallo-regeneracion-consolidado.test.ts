/**
 * HU #12799 (Épica #12760) — regla PURA del fallo de regeneración del consolidado.
 *
 * Uso de ejemplo:
 *   detectarFalloRegeneracion({ regenerado: false, avisosCascada: ['consolidado: excepcion'] })
 *   // → { documento: 'consolidado', causa: 'excepcion', causaTexto: 'ocurrió un error inesperado…' }
 *   vigenciaTrasApertura(previa, res, new Date()) // fallo ⇒ desactualizado con la fecha conservada
 */
import { describe, expect, it } from 'vitest';
import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import {
  CAUSA_FALLO_GENERICA,
  avisosSinFalloConsolidado,
  detectarFalloRegeneracion,
  esAvisoFalloConsolidado,
  textoAvisoFalloRegeneracion,
  textoCausaFallo,
  vigenciaTrasApertura,
} from '@/lib/tramites/fallo-regeneracion-consolidado';

// 15:05 UTC = 10:05 Bogotá. Datos ficticios.
const GENERADO = '2026-09-20T15:05:00Z';
const AHORA = new Date('2026-09-23T20:00:00Z');

function vigencia(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: 'desactualizado',
    generadoEn: GENERADO,
    origen: 'system',
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

describe('hu12799 AC1/AC3 — detectarFalloRegeneracion', () => {
  it('AC1 — wizard: regenerado=false + «consolidado: …» es fallo, con causa traducida', () => {
    const fallo = detectarFalloRegeneracion({
      regenerado: false,
      avisosCascada: ['consolidado: adjunto_no_disponible'],
    });
    expect(fallo).toEqual({
      documento: 'consolidado',
      causa: 'adjunto_no_disponible',
      causaTexto: 'un documento del expediente no está disponible en el almacenamiento',
    });
  });

  it('AC3 — maestro OT: «consolidado_maestro: excepcion» es fallo del maestro', () => {
    const fallo = detectarFalloRegeneracion({
      regenerado: false,
      avisosCascada: ['impronta: provider_unavailable', 'consolidado_maestro: excepcion'],
    });
    expect(fallo?.documento).toBe('consolidado_maestro');
    expect(fallo?.causaTexto).toBe('ocurrió un error inesperado al generar el documento');
  });

  it('contrato — devuelve documento, causa y causaTexto', () => {
    const fallo = detectarFalloRegeneracion({
      regenerado: false,
      avisosCascada: ['consolidado: storage_unavailable'],
    });
    expect(fallo).toHaveProperty('documento');
    expect(fallo).toHaveProperty('causa');
    expect(fallo).toHaveProperty('causaTexto');
  });
});

describe('hu12799 AC4 — sin fallo no hay aviso', () => {
  it.each([
    ['regenerado true con aviso de consolidado', { regenerado: true, avisosCascada: ['consolidado: x'] }],
    ['regenerado omitido', { avisosCascada: ['consolidado: excepcion'] }],
    ['reutilizado vigente sin avisos', { regenerado: false, avisosCascada: [] }],
    ['avisos de otros documentos', { regenerado: false, avisosCascada: ['fur: provider_unavailable'] }],
    ['avisosCascada null', { regenerado: false, avisosCascada: null }],
  ])('%s → null', (_caso, res) => {
    expect(detectarFalloRegeneracion(res)).toBeNull();
  });

  it.each([null, undefined])('respuesta %s → null sin lanzar', (res) => {
    expect(() => detectarFalloRegeneracion(res)).not.toThrow();
    expect(detectarFalloRegeneracion(res)).toBeNull();
  });

  it('prefijo parecido («consolidado_wizard_extra: …», «documentos_del_expediente: …») no es fallo', () => {
    expect(esAvisoFalloConsolidado('consolidado_wizard_extra: x')).toBe(false);
    expect(esAvisoFalloConsolidado('documentos_del_expediente: x')).toBe(false);
    expect(esAvisoFalloConsolidado('consolidado:excepcion')).toBe(true);
    expect(esAvisoFalloConsolidado('')).toBe(false);
  });

  it('avisosSinFalloConsolidado conserva los avisos de otros documentos', () => {
    expect(
      avisosSinFalloConsolidado([
        'consolidado: excepcion',
        'impronta: provider_unavailable',
        'consolidado_maestro: storage_unavailable',
      ]),
    ).toEqual(['impronta: provider_unavailable']);
    expect(avisosSinFalloConsolidado(null)).toEqual([]);
  });
});

describe('hu12799 — copy del aviso (sin códigos crudos)', () => {
  it('causa desconocida o vacía → texto genérico', () => {
    expect(textoCausaFallo('codigo_inventado_xyz')).toBe(CAUSA_FALLO_GENERICA);
    expect(textoCausaFallo('')).toBe(CAUSA_FALLO_GENERICA);
    expect(textoCausaFallo(null)).toBe(CAUSA_FALLO_GENERICA);
  });

  it('AC1 — dice que el PDF no es el más reciente e incluye la fecha en hora Colombia', () => {
    const texto = textoAvisoFalloRegeneracion({ causaTexto: 'el almacenamiento no respondió' }, GENERADO);
    expect(texto).toBe(
      'El almacenamiento no respondió. El PDF que se muestra no es el más reciente: es la versión generada el 20/09/2026 10:05 (hora Colombia).',
    );
    expect(texto).not.toMatch(/_/);
  });

  it('sin fecha fiable no inventa una', () => {
    const texto = textoAvisoFalloRegeneracion({ causaTexto: CAUSA_FALLO_GENERICA }, null);
    expect(texto).toMatch(/no es el más reciente: es la última versión disponible/);
    expect(texto).not.toMatch(/\d{2}\/\d{2}\/\d{4}/);
  });
});

describe('hu12799 AC1/AC2/AC3 — vigencia local tras abrir o regenerar', () => {
  const FALLO_WIZARD = { regenerado: false, avisosCascada: ['consolidado: excepcion'] };

  it('AC1 — fallo del POST: sigue gris con la fecha del PDF conservado', () => {
    const nueva = vigenciaTrasApertura(vigencia({ estado: 'vigente' }), FALLO_WIZARD, AHORA);
    expect(nueva).toMatchObject({ estado: 'desactualizado', generadoEn: GENERADO });
  });

  it('AC2 — reintento con éxito: pasa a vigente con la fecha de ahora', () => {
    const nueva = vigenciaTrasApertura(vigencia(), { regenerado: true, avisosCascada: [] }, AHORA);
    expect(nueva).toMatchObject({ estado: 'vigente', generadoEn: AHORA.toISOString(), origen: 'system' });
  });

  it('AC3 — entrega OT con fallo (modo null) NO pasa a vigente (antes sí)', () => {
    const nueva = vigenciaTrasApertura(
      vigencia(),
      { regenerado: false, modo: null, avisosCascada: ['consolidado_maestro: adjunto_no_disponible'] },
      AHORA,
    );
    expect(nueva).toMatchObject({ estado: 'desactualizado', generadoEn: GENERADO });
  });

  it('sin vigencia previa no toca el indicador', () => {
    expect(vigenciaTrasApertura(null, FALLO_WIZARD, AHORA)).toBeNull();
  });

  it('no regresión #12793 — entrega vigente conserva la fecha; solo_lectura no toca', () => {
    expect(
      vigenciaTrasApertura(vigencia({ estado: 'vigente' }), { regenerado: false, modo: 'vigente' }, AHORA),
    ).toMatchObject({ estado: 'vigente', generadoEn: GENERADO });
    expect(
      vigenciaTrasApertura(vigencia(), { regenerado: false, modo: 'solo_lectura' }, AHORA),
    ).toBeNull();
  });
});
