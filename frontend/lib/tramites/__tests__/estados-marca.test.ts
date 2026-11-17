import { describe, expect, it } from 'vitest';
import { ESTADOS_TRAMITE, estadoChipClass, estadoChipStyle } from '../estados';

// HU #14482 (ajuste de Samuel) — el chip de estado va solo con texto, sin marca, y con el color
// propio de cada estado (tokens --estado-<x>-bg/-fg/-border, valores de develop).
describe('chip de estado del trámite (HU #14482)', () => {
  it('ningún estado lleva modificador de marca', () => {
    for (const e of [...ESTADOS_TRAMITE, 'submitted', null]) {
      expect(estadoChipClass(e)).toBe('di-estado');
    }
  });

  it('cada estado toma su propio token de color', () => {
    for (const e of ESTADOS_TRAMITE) {
      expect(estadoChipStyle(e)).toMatchObject({
        bg: `var(--estado-${e}-bg)`,
        color: `var(--estado-${e}-fg)`,
        border: `var(--estado-${e}-border)`,
      });
    }
  });
});
