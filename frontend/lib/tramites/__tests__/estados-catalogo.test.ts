// HU #12600 (Feature #12595, ADR-0059) — catálogo de estados: Preasignación, Asignado, Revocado y el
// distintivo «Rechazado preasignación» en la fuente única del frontend.
import { describe, expect, it } from 'vitest';
import {
  ESTADO_CHIP_STYLES,
  ESTADO_ICONO,
  ESTADO_LABELS,
  ESTADOS_FINALES,
  ESTADOS_RUTA_PLACA,
  ESTADOS_TRAMITE,
  RECHAZADO_PREASIGNACION_LABEL,
  esRechazadoDesdePreasignacion,
  estadoChipStyle,
  estadoLabel,
  estadoLabelConOrigen,
} from '../estados';

describe('catálogo de estados (ADR-0059)', () => {
  it('AC1 — preasignacion, asignado y revocado son estados del trámite con etiqueta, chip e icono propios', () => {
    expect(ESTADOS_TRAMITE).toEqual([
      'borrador',
      'anulado',
      'preparado',
      'preasignacion',
      'asignado',
      'entregado',
      'aprobado',
      'rechazado',
      'revocado',
      'subsanacion',
    ]);

    expect(estadoLabel('preasignacion')).toBe('Preasignación');
    expect(estadoLabel('asignado')).toBe('Asignado');
    expect(estadoLabel('revocado')).toBe('Revocado');

    for (const estado of ['preasignacion', 'asignado', 'revocado'] as const) {
      expect(ESTADO_CHIP_STYLES[estado].accent).toMatch(/^#[0-9A-F]{6}$/i);
      expect(ESTADO_ICONO[estado]).toBe(`/assets/estados/${estado}.svg`);
      // El chip no es el fallback gris: cada estado nuevo tiene identidad propia.
      expect(estadoChipStyle(estado)).toBe(ESTADO_CHIP_STYLES[estado]);
    }
  });

  it('AC1 — cada estado del catálogo tiene label, chip e icono (no hay huecos)', () => {
    for (const estado of ESTADOS_TRAMITE) {
      expect(ESTADO_LABELS[estado]).toBeTruthy();
      expect(ESTADO_CHIP_STYLES[estado]).toBeDefined();
      expect(ESTADO_ICONO[estado]).toMatch(/^\/assets\/estados\/.+\.svg$/);
    }
  });

  it('AC1 — preasignación ámbar (artefacto ADR-0059), asignado índigo; revocado no se confunde con anulado', () => {
    expect(ESTADO_CHIP_STYLES.preasignacion.accent).toBe('#E08A00');
    expect(ESTADO_CHIP_STYLES.asignado.accent).toBe('#6366F1');
    expect(ESTADO_CHIP_STYLES.revocado.accent).not.toBe(ESTADO_CHIP_STYLES.anulado.accent);
    expect(ESTADOS_RUTA_PLACA).toEqual(['preasignacion', 'asignado']);
    expect(ESTADOS_FINALES).toContain('revocado');
  });

  it('AC2 — rechazado con rejectedFrom=preasignacion dice «Rechazado preasignación» con el color de Rechazado', () => {
    expect(estadoLabelConOrigen('rechazado', 'preasignacion')).toBe(RECHAZADO_PREASIGNACION_LABEL);
    expect(RECHAZADO_PREASIGNACION_LABEL).toBe('Rechazado preasignación');
    expect(esRechazadoDesdePreasignacion('rechazado', 'preasignacion')).toBe(true);
    // El distintivo no cambia el color: sigue siendo un rechazo.
    expect(estadoChipStyle('rechazado')).toBe(ESTADO_CHIP_STYLES.rechazado);
  });

  it('AC2 — sin marca (o con otro origen) el chip dice «Rechazado»; la marca no aplica a otros estados', () => {
    expect(estadoLabelConOrigen('rechazado', null)).toBe('Rechazado');
    expect(estadoLabelConOrigen('rechazado', undefined)).toBe('Rechazado');
    expect(estadoLabelConOrigen('rechazado', 'entregado')).toBe('Rechazado');
    expect(estadoLabelConOrigen('preasignacion', 'preasignacion')).toBe('Preasignación');
    expect(esRechazadoDesdePreasignacion('entregado', 'preasignacion')).toBe(false);
    expect(esRechazadoDesdePreasignacion(null, 'preasignacion')).toBe(false);
  });

  it('AC3 — un valor desconocido cae al fallback titlecase y al chip neutro, sin romper', () => {
    expect(estadoLabel('preasignado')).toBe('Preasignado');
    expect(estadoLabel('terminado')).toBe('Terminado');
    expect(estadoLabel('en_revision')).toBe('En revision');
    expect(estadoLabelConOrigen('terminado', null)).toBe('Terminado');
    expect(estadoChipStyle('preasignado').accent).toBe('#64748B');
    expect(estadoLabel(null)).toBe('—');
  });
});
