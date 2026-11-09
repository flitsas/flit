// HU 2 (registro diferido 2026-11-09) — formato de placa de carro en el modal «Asignar placa al
// trámite» del OT. Solo carros: la clase normalizada (como `FurClassificationNormalizer` del
// backend) debe ser IGUAL a una de las nueve clases de carro; nunca prefijo ni contains.
//
// Uso de ejemplo:
//   esClaseCarro('  Camión ') → true
//   esClaseCarro('CAMIÓN GRÚA') → false (maquinaria)
//   normalizarPlacaCarro('abc-12 3') → 'ABC123'
//   PLACA_CARRO_PATTERN.test('ABC123') → true
//   estadoPlacaOt('ABC12', true) → { invalida: true, lista: false }
import { describe, expect, it } from 'vitest';
import { PLACA_CARRO_PATTERN, esClaseCarro, estadoPlacaOt, normalizarPlacaCarro } from '../plate-format';

describe('esClaseCarro', () => {
  it.each([
    'AUTOMOVIL',
    'CAMIONETA',
    'CAMPERO',
    'BUS',
    'BUSETA',
    'MICROBUS',
    'CAMION',
    'TRACTOCAMION',
    'VOLQUETA',
  ])('reconoce %s como carro', (clase) => {
    expect(esClaseCarro(clase)).toBe(true);
  });

  it('normaliza tildes, mayúsculas, espacios externos e internos', () => {
    expect(esClaseCarro('automóvil')).toBe(true);
    expect(esClaseCarro('  Camión ')).toBe(true);
    expect(esClaseCarro('Micro   bus')).toBe(false); // colapsa, no elimina espacios
    expect(esClaseCarro('TRACTOCAMIÓN')).toBe(true);
    expect(esClaseCarro('Camioneta')).toBe(true);
  });

  it('nunca compara por prefijo ni contains (CAMIÓN GRÚA es maquinaria)', () => {
    expect(esClaseCarro('CAMIÓN GRÚA')).toBe(false);
    expect(esClaseCarro('CAMION GRUA')).toBe(false);
    expect(esClaseCarro('CAMION  GRUA')).toBe(false);
    expect(esClaseCarro('BUSES')).toBe(false);
  });

  it('motos, remolques, SIN CLASE, vacía y nula no son carro', () => {
    for (const c of ['MOTOCICLETA', 'MOTOCARRO', 'REMOLQUE', 'SEMIRREMOLQUE', 'SIN CLASE', '', '   ']) {
      expect(esClaseCarro(c)).toBe(false);
    }
    expect(esClaseCarro(null)).toBe(false);
    expect(esClaseCarro(undefined)).toBe(false);
  });
});

describe('normalizarPlacaCarro', () => {
  it('AC normalización — "abc-12 3" queda "ABC123"', () => {
    expect(normalizarPlacaCarro('abc-12 3')).toBe('ABC123');
  });

  it('corta a 6 caracteres y descarta todo lo que no sea A-Z / 0-9', () => {
    expect(normalizarPlacaCarro('abc1234')).toBe('ABC123');
    expect(normalizarPlacaCarro('a.b_c/1*2#3')).toBe('ABC123');
    expect(normalizarPlacaCarro('ñbc123')).toBe('BC123');
  });

  it('vacío no lanza y devuelve vacío', () => {
    expect(normalizarPlacaCarro('')).toBe('');
  });
});

describe('PLACA_CARRO_PATTERN', () => {
  it('acepta exactamente 3 letras y 3 números', () => {
    expect(PLACA_CARRO_PATTERN.test('ABC123')).toBe(true);
  });

  it.each(['ABC12', 'ABC12D', 'AB1234', 'abc123', 'ABC1234', 'R12345', ''])('rechaza %s', (p) => {
    expect(PLACA_CARRO_PATTERN.test(p)).toBe(false);
  });
});

// Compartido por los modales «Asignar placa» y «Corregir placa» del OT (2026-11-09).
describe('estadoPlacaOt', () => {
  it('carro inválido → invalida y no lista', () => {
    expect(estadoPlacaOt('ABC12', true)).toEqual({ invalida: true, lista: false });
    expect(estadoPlacaOt('ABC12D', true)).toEqual({ invalida: true, lista: false });
  });
  it('carro válido → lista y sin aviso', () => {
    expect(estadoPlacaOt('ABC123', true)).toEqual({ invalida: false, lista: true });
  });
  it('vacío no es error pero tampoco está listo', () => {
    expect(estadoPlacaOt('', true)).toEqual({ invalida: false, lista: false });
    expect(estadoPlacaOt('   ', false)).toEqual({ invalida: false, lista: false });
  });
  it('no carro → solo exige texto, cualquier formato', () => {
    expect(estadoPlacaOt('ABC12D', false)).toEqual({ invalida: false, lista: true });
    expect(estadoPlacaOt('R12345', false)).toEqual({ invalida: false, lista: true });
  });
});
