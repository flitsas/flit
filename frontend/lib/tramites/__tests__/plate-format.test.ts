// HU 2 (registro diferido 2026-11-09) — formato de placa de carro en el modal «Asignar placa al
// trámite» del OT. Solo carros: la clase normalizada (como `FurClassificationNormalizer` del
// backend) debe ser IGUAL a una de las nueve clases de carro; nunca prefijo ni contains.
//
// Uso de ejemplo:
//   esClaseCarro('  Camión ') → true
//   esClaseCarro('CAMIÓN GRÚA') → false (maquinaria)
//   normalizarPlacaCarro('abc-12 3') → 'ABC123'
//   PLACA_CARRO_PATTERN.test('ABC123') → true
//   estadoPlacaOt('ABC12', 'CARRO') → { invalida: true, lista: false }
import { describe, expect, it } from 'vitest';
import {
  PLACA_CARRO_PATTERN,
  esClaseCarro,
  estadoPlacaOt,
  grupoPlacaDesdeClase,
  grupoPlacaDesdeFormatoFur,
  grupoPlacaPideDigito,
  normalizarPlacaCarro,
  normalizarPlacaOt,
  resolverGrupoPlaca,
} from '../plate-format';

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
    expect(estadoPlacaOt('ABC12', 'CARRO')).toEqual({ invalida: true, lista: false });
    expect(estadoPlacaOt('ABC12D', 'CARRO')).toEqual({ invalida: true, lista: false });
  });
  it('carro válido → lista y sin aviso', () => {
    expect(estadoPlacaOt('ABC123', 'CARRO')).toEqual({ invalida: false, lista: true });
  });
  it('vacío no es error pero tampoco está listo', () => {
    expect(estadoPlacaOt('', 'CARRO')).toEqual({ invalida: false, lista: false });
    expect(estadoPlacaOt('   ', null)).toEqual({ invalida: false, lista: false });
  });
  it('sin grupo → solo exige texto, cualquier formato', () => {
    expect(estadoPlacaOt('ABC12D', null)).toEqual({ invalida: false, lista: true });
    expect(estadoPlacaOt('R12345', null)).toEqual({ invalida: false, lista: true });
  });
});

// HU5–HU7 (registro diferido 2026-11-09, plan preasignacion.pdf) — grupo de placa por clase.
// Uso de ejemplo:
//   grupoPlacaDesdeClase('Motocicleta') → 'MOTO'
//   resolverGrupoPlaca('MAQUINARIA', 'CAMION GRUA') → 'MAQUINARIA'
//   estadoPlacaOt('R1234', 'REMOLQUE') → { invalida: true, lista: false }
describe('grupoPlacaDesdeClase (fallback por listas exactas)', () => {
  it.each(['MOTOCICLETA', 'CUATRIMOTO', 'CUADRICICLO', 'CICLOMOTOR'])(
    '%s → MOTO',
    (c) => expect(grupoPlacaDesdeClase(c)).toBe('MOTO'),
  );
  it.each(['REMOLQUE', 'SEMIREMOLQUE', 'SEMIRREMOLQUE', 'REMOLQUE Y SEMIRREMOLQUE', ' remolque  y semirremolque '])(
    '%s → REMOLQUE',
    (c) => expect(grupoPlacaDesdeClase(c)).toBe('REMOLQUE'),
  );
  it('carros → CARRO, normalizando tildes', () => {
    expect(grupoPlacaDesdeClase('Automóvil')).toBe('CARRO');
    expect(grupoPlacaDesdeClase('CAMION')).toBe('CARRO');
  });
  it('maquinaria, SIN CLASE, vacía y nula → sin grupo (nunca contains)', () => {
    for (const c of ['CAMIÓN GRÚA', 'MOTONIVELADORA', 'MOTO', 'SIN CLASE', '', null, undefined]) {
      expect(grupoPlacaDesdeClase(c)).toBeNull();
    }
  });
});

describe('resolverGrupoPlaca (grupoPlaca del backend primero)', () => {
  it('usa grupoPlaca si viene, aunque la clase diga otra cosa', () => {
    expect(resolverGrupoPlaca('MAQUINARIA', 'CAMION GRUA')).toBe('MAQUINARIA');
    expect(resolverGrupoPlaca('REMOLQUE', 'AUTOMOVIL')).toBe('REMOLQUE');
    expect(resolverGrupoPlaca('moto', '')).toBe('MOTO');
  });
  it('sin grupoPlaca (null/undefined/desconocido) cae a las listas por clase', () => {
    expect(resolverGrupoPlaca(null, 'MOTOCICLETA')).toBe('MOTO');
    expect(resolverGrupoPlaca(undefined, 'SEMIRREMOLQUE')).toBe('REMOLQUE');
    expect(resolverGrupoPlaca('OTRO', 'AUTOMOVIL')).toBe('CARRO');
    expect(resolverGrupoPlaca(undefined, 'CAMION GRUA')).toBeNull();
  });
});

describe('grupoPlacaDesdeFormatoFur (lado gestor, FUR)', () => {
  it('MAQUINARIA y REMOLQUES del catálogo del backend mandan', () => {
    expect(grupoPlacaDesdeFormatoFur('MAQUINARIA', 'CAMION GRUA')).toBe('MAQUINARIA');
    expect(grupoPlacaDesdeFormatoFur('REMOLQUES', 'CAMA BAJA')).toBe('REMOLQUE');
  });
  it('AUTOMOTOR o sin formato → listas por clase', () => {
    expect(grupoPlacaDesdeFormatoFur('AUTOMOTOR', 'MOTOCICLETA')).toBe('MOTO');
    expect(grupoPlacaDesdeFormatoFur(null, 'REMOLQUE')).toBe('REMOLQUE');
    expect(grupoPlacaDesdeFormatoFur(undefined, '')).toBeNull();
  });
});

describe('grupoPlacaPideDigito', () => {
  it('REMOLQUE y MAQUINARIA no piden dígito; CARRO, MOTO y sin grupo sí', () => {
    expect(grupoPlacaPideDigito('REMOLQUE')).toBe(false);
    expect(grupoPlacaPideDigito('MAQUINARIA')).toBe(false);
    expect(grupoPlacaPideDigito('CARRO')).toBe(true);
    expect(grupoPlacaPideDigito('MOTO')).toBe(true);
    expect(grupoPlacaPideDigito(null)).toBe(true);
  });
});

describe('normalizarPlacaOt — máscara por grupo', () => {
  it('maxLength por grupo: CARRO 6, MOTO 6, REMOLQUE 7, MAQUINARIA 8', () => {
    expect(normalizarPlacaOt('abc-1234', 'CARRO')).toBe('ABC123');
    expect(normalizarPlacaOt('abc 12de', 'MOTO')).toBe('ABC12D');
    expect(normalizarPlacaOt('r-1234567', 'REMOLQUE')).toBe('R123456');
    expect(normalizarPlacaOt('mc 0295549', 'MAQUINARIA')).toBe('MC029554');
  });
  it('sin grupo deja el texto tal cual', () => {
    expect(normalizarPlacaOt('abc-12 d', null)).toBe('abc-12 d');
  });
});

describe('estadoPlacaOt por grupo', () => {
  it.each([
    ['MOTO', 'ABC12D', true],
    ['MOTO', 'ABC12', true],
    ['MOTO', 'ABC123', false],
    ['MOTO', 'AB12D', false],
    ['REMOLQUE', 'R12345', true],
    ['REMOLQUE', 'S543210', true],
    ['REMOLQUE', 'R1234', false],
    ['REMOLQUE', 'T12345', false],
    ['MAQUINARIA', 'MC029554', true],
    ['MAQUINARIA', 'MC02955', false],
    ['MAQUINARIA', 'M0295541', false],
  ] as const)('%s %s → lista=%s', (grupo, placa, lista) => {
    expect(estadoPlacaOt(placa, grupo)).toEqual({ invalida: !lista, lista });
  });
});

// HU10 (corrección del usuario 2026-11-09, D5) — grupo MOTOCARRO: 123ABC, dígito = primer número.
// Uso de ejemplo: grupoPlacaDesdeClase('Mototriciclo') → 'MOTOCARRO'; estadoPlacaOt('123ABC', 'MOTOCARRO').lista → true
describe('grupo MOTOCARRO (HU10)', () => {
  it.each(['MOTOCARRO', 'MOTOTRICICLO', 'TRICIMOTO', ' tricimoto '])('%s → MOTOCARRO (lista exacta)', (c) => {
    expect(grupoPlacaDesdeClase(c)).toBe('MOTOCARRO');
  });
  it('grupoPlaca "MOTOCARRO" del backend es válido', () => {
    expect(resolverGrupoPlaca('MOTOCARRO', 'X')).toBe('MOTOCARRO');
    expect(grupoPlacaDesdeFormatoFur('AUTOMOTOR', 'MOTOCARRO')).toBe('MOTOCARRO');
  });
  it('pide dígito', () => {
    expect(grupoPlacaPideDigito('MOTOCARRO')).toBe(true);
  });
  it('máscara: mayúsculas, alfanumérico y maxLength 6', () => {
    expect(normalizarPlacaOt('123-abcd', 'MOTOCARRO')).toBe('123ABC');
  });
  it.each([
    ['123ABC', true],
    ['ABC12D', false],
    ['ABC123', false],
    ['12ABC', false],
    ['1234AB', false],
  ] as const)('estadoPlacaOt %s → lista=%s', (placa, lista) => {
    expect(estadoPlacaOt(placa, 'MOTOCARRO')).toEqual({ invalida: !lista, lista });
  });
});
