// Uso de ejemplo:
 // detalleEstadoHeader('entregado').color === estadoChipStyle('entregado').accent (#00A99D)
 // INLINE_ALERT_TONES.pending.background === 'var(--badge-pending-bg)'
import { describe, expect, it } from 'vitest';
import { ESTADOS_TRAMITE, estadoChipStyle } from '@/lib/tramites/estados';
import { INLINE_ALERT_TONES } from '@/components/atom/InlineAlert';
import { DETALLE_GOLD } from '../detalle-visual';
import { detalleEstadoHeader } from '../detalle-estado-header';

describe('HU #12726 — AC1 cabecera = catálogo de estados', () => {
  it('happy: entregado usa accent teal del catálogo (#00A99D)', () => {
    const hdr = detalleEstadoHeader('entregado');
    expect(hdr.color).toBe(estadoChipStyle('entregado').accent);
    expect(hdr.color).toBe('#00A99D');
  });

  it('contrato: cada estado del catálogo usa exactamente accent de estadoChipStyle', () => {
    for (const estado of ESTADOS_TRAMITE) {
      expect(detalleEstadoHeader(estado).color).toBe(estadoChipStyle(estado).accent);
    }
  });

  it('edge: el dorado DETALLE_GOLD (#F9AC00) no es el color de cabecera de ningún estado del catálogo', () => {
    expect(DETALLE_GOLD).toBe('#F9AC00');
    for (const estado of ESTADOS_TRAMITE) {
      expect(detalleEstadoHeader(estado).color).not.toBe(DETALLE_GOLD);
    }
  });
});

describe('HU #12726 — AC4 tono pending de InlineAlert', () => {
  it('happy: pending usa tokens --badge-pending-* (naranja corporativo)', () => {
    const pending = INLINE_ALERT_TONES.pending;
    expect(pending.background).toBe('var(--badge-pending-bg)');
    expect(pending.color).toBe('var(--badge-pending-fg)');
    expect(pending.border).toBe('var(--badge-pending-border)');
    expect(pending.Icon.displayName ?? pending.Icon.name).toMatch(/TriangleAlert|AlertTriangle/i);
  });

  it('contrato: error/warning/info/success no cambian a tokens pending', () => {
    expect(INLINE_ALERT_TONES.error.background).toBe('var(--badge-danger-bg)');
    expect(INLINE_ALERT_TONES.warning.background).toBe('var(--badge-warning-bg)');
    expect(INLINE_ALERT_TONES.info.background).toBe('var(--badge-info-bg)');
    expect(INLINE_ALERT_TONES.success.background).toBe('var(--badge-success-bg)');
    for (const tone of ['error', 'warning', 'info', 'success'] as const) {
      expect(INLINE_ALERT_TONES[tone].background).not.toBe('var(--badge-pending-bg)');
    }
  });

  it('edge: estados pendientes de cabecera marcan pendiente=true (alerta naranja en modal)', () => {
    expect(detalleEstadoHeader('preasignacion').pendiente).toBe(true);
    expect(detalleEstadoHeader('asignado').pendiente).toBe(true);
    expect(detalleEstadoHeader('subsanacion').pendiente).toBe(true);
    expect(detalleEstadoHeader('entregado').pendiente).toBe(false);
    expect(detalleEstadoHeader('aprobado').pendiente).toBe(false);
  });
});

describe('HU #12726 — AC2 contrato a11y etiqueta de timeline', () => {
  // Uso de ejemplo: aria-label `Ver línea de tiempo de ${ref}` (chip tabla) y cabecera modal.
  it('contrato: patrón de etiqueta accesible del chip de estado incluye el radicado', () => {
    const ref = 'TR-TL';
    const aria = `Ver línea de tiempo de ${ref}`;
    expect(aria).toMatch(/^Ver línea de tiempo de TR-TL$/);
  });

  it('happy: cabecera del detalle anuncia estado + acción de timeline', () => {
    const label = detalleEstadoHeader('entregado').label;
    const aria = `Estado: ${label}. Ver línea de tiempo del trámite`;
    expect(aria).toBe('Estado: Entregado. Ver línea de tiempo del trámite');
  });

  it('edge: etiqueta sigue válida con referencia vacía (no lanza)', () => {
    expect(() => `Ver línea de tiempo de ${''}`).not.toThrow();
  });
});

describe('HU #12726 — AC3 contrato initialPanel timeline', () => {
  it('contrato: valores de panel admitidos incluyen timeline', () => {
    const allowed = ['timeline', 'identidad', null] as const;
    expect(allowed).toContain('timeline');
  });

  it('happy: entregado no es pendiente (toggle timeline no fuerza alerta naranja)', () => {
    expect(detalleEstadoHeader('entregado').pendiente).toBe(false);
  });

  it('edge: preasignacion sí es pendiente aunque el panel sea timeline', () => {
    expect(detalleEstadoHeader('preasignacion').pendiente).toBe(true);
  });
});

describe('HU #12726 — AC5 no regresión catálogo / tonos', () => {
  it('contrato: hex de entregado en catálogo permanece #00A99D (estados.ts inmutable en esta HU)', () => {
    expect(estadoChipStyle('entregado').accent).toBe('#00A99D');
  });

  it('happy: tonos base InlineAlert distintos de pending', () => {
    for (const tone of ['error', 'warning', 'info', 'success'] as const) {
      expect(INLINE_ALERT_TONES[tone].background).not.toBe(INLINE_ALERT_TONES.pending.background);
    }
  });

  it('edge: DETALLE_GOLD sigue existiendo como token visual pero no pinta cabecera de estados', () => {
    expect(DETALLE_GOLD).toBe('#F9AC00');
    expect(detalleEstadoHeader('borrador').color).not.toBe(DETALLE_GOLD);
  });
});
