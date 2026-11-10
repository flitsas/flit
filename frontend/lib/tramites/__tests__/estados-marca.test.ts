import { describe, expect, it } from 'vitest';
import { ESTADOS_TRAMITE, ESTADO_MARCA, estadoChipClass } from '../estados';

// HU #14482 — lenguaje de estados de la propuesta: sin verde ni rojo, la marca dice el estado.
describe('marca del chip de estado (HU #14482)', () => {
  it('todo estado del catálogo tiene marca', () => {
    for (const e of ESTADOS_TRAMITE) expect(ESTADO_MARCA[e]).toBeTruthy();
  });

  it('aprobado lleva check, subsanación punto y los cierres sin aprobar equis', () => {
    expect(estadoChipClass('aprobado')).toBe('di-estado di-estado--check');
    expect(estadoChipClass('subsanacion')).toBe('di-estado di-estado--punto');
    for (const e of ['rechazado', 'anulado', 'revocado']) {
      expect(estadoChipClass(e)).toBe('di-estado di-estado--equis');
    }
    for (const e of ['preparado', 'preasignacion', 'asignado', 'entregado']) {
      expect(estadoChipClass(e)).toBe('di-estado di-estado--avance');
    }
  });

  it('borrador y valores desconocidos o vacíos quedan neutros', () => {
    expect(estadoChipClass('borrador')).toBe('di-estado di-estado--neutro');
    expect(estadoChipClass('submitted')).toBe('di-estado di-estado--neutro');
    expect(estadoChipClass(null)).toBe('di-estado di-estado--neutro');
  });
});
