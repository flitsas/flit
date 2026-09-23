/**
 * HU #12794 (Épica #12760) — lógica pura del ORIGEN manual del consolidado (AC2).
 *
 * Uso de ejemplo:
 *   esConsolidadoManual({ estado: 'vigente', generadoEn: '…', origen: 'user',
 *     definitivo: false, modo: 'cargado_por_usuario' }) // → true
 */
import { describe, expect, it } from 'vitest';
import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import {
  COPY_ORIGEN_CONSOLIDADO,
  esConsolidadoManual,
} from '@/lib/tramites/vigencia-consolidado';

function vigencia(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: 'vigente',
    generadoEn: '2026-09-23T15:05:00Z',
    origen: 'system',
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

describe('HU #12794 AC2 — esConsolidadoManual', () => {
  it('happy path: origen user ⇒ manual', () => {
    expect(esConsolidadoManual(vigencia({ origen: 'user' }))).toBe(true);
  });

  it('happy path: modo cargado_por_usuario ⇒ manual aunque el origen no venga', () => {
    expect(esConsolidadoManual(vigencia({ origen: null, modo: 'cargado_por_usuario' }))).toBe(true);
  });

  it('manual también cuando está desactualizado (el PDF cargado sigue protegido)', () => {
    expect(
      esConsolidadoManual(vigencia({ estado: 'desactualizado', origen: 'user', modo: 'cargado_por_usuario' })),
    ).toBe(true);
  });

  it('edge: origen system ⇒ no manual', () => {
    expect(esConsolidadoManual(vigencia())).toBe(false);
  });

  it('edge: inexistente nunca es manual aunque el backend arrastre origen', () => {
    expect(esConsolidadoManual(vigencia({ estado: 'inexistente', generadoEn: null, origen: 'user' }))).toBe(false);
  });

  it('edge: null / undefined / estado desconocido ⇒ false sin lanzar', () => {
    expect(esConsolidadoManual(null)).toBe(false);
    expect(esConsolidadoManual(undefined)).toBe(false);
    expect(
      esConsolidadoManual(vigencia({ estado: 'otro' as ConsolidadoVigencia['estado'], origen: 'user' })),
    ).toBe(false);
  });

  it('contrato: copy de la marca y de la advertencia', () => {
    expect(COPY_ORIGEN_CONSOLIDADO.manual).toBe('Cargado manualmente');
    expect(COPY_ORIGEN_CONSOLIDADO.advertenciaManual).toMatch(/no lo sobrescribirá/);
  });
});
