import { describe, it, expect } from 'vitest';
import {
  sanitizeVin,
  validateVin,
  sanitizePlate,
  validatePlate,
  sanitizeDocNumber,
  validateDocNumber,
} from '../fieldRules';

describe('VIN (ISO 3779)', () => {
  it('acepta 17 caracteres válidos', () => {
    expect(validateVin('1HGCM82633A004352')).toBeNull();
  });

  it('rechaza longitud distinta de 17', () => {
    expect(validateVin('1HGCM82633A00435')).not.toBeNull();
  });

  it('rechaza las letras prohibidas I, O, Q', () => {
    expect(validateVin('IHGCM82633A004352')).not.toBeNull();
    expect(validateVin('OHGCM82633A004352')).not.toBeNull();
    expect(validateVin('QHGCM82633A004352')).not.toBeNull();
  });

  it('sanea a mayúsculas, quita inválidos (I/O/Q, símbolos) y topa en 17', () => {
    expect(sanitizeVin('io q1')).toBe('1'); // I, O, Q y espacios fuera
    expect(sanitizeVin('1111111111111111122')).toBe('11111111111111111'); // 19 dígitos → tope 17
  });
});

describe('Placa Colombia', () => {
  it('acepta carro, moto (actual/antigua), remolque y maquinaria', () => {
    expect(validatePlate('ABC123')).toBeNull(); // carro
    expect(validatePlate('ABC12D')).toBeNull(); // moto actual
    expect(validatePlate('ABC12')).toBeNull(); // moto antigua
    expect(validatePlate('R12345')).toBeNull(); // remolque
    expect(validatePlate('S12345')).toBeNull(); // semirremolque
    expect(validatePlate('MC029554')).toBeNull(); // maquinaria (2 letras + 6 dígitos)
  });

  it('rechaza formatos inválidos', () => {
    expect(validatePlate('AB123')).not.toBeNull();
    expect(validatePlate('ABCD12')).not.toBeNull();
    expect(validatePlate('123ABC')).not.toBeNull();
    expect(validatePlate('MC02955')).not.toBeNull(); // maquinaria incompleta (5 dígitos)
    expect(validatePlate('MC0295544')).not.toBeNull(); // maquinaria con dígito de más
  });

  it('sanea a mayúsculas y quita separadores', () => {
    expect(sanitizePlate('abc-123')).toBe('ABC123');
    expect(sanitizePlate('mc-029554')).toBe('MC029554'); // maquinaria: no se trunca a 6
  });
});

describe('Número de documento por tipo', () => {
  it('CC/CE/TI/NIT: solo dígitos', () => {
    expect(validateDocNumber('1020304050', 'CC')).toBeNull();
    expect(validateDocNumber('12A4', 'CC')).not.toBeNull();
    expect(validateDocNumber('900123-1', 'NIT')).not.toBeNull();
  });

  it('PAS: admite letras y números', () => {
    expect(validateDocNumber('AB123CD', 'PAS')).toBeNull();
    expect(validateDocNumber('AB 12', 'PAS')).not.toBeNull(); // espacio fuera
  });

  it('sanea según el tipo', () => {
    expect(sanitizeDocNumber('12a.34', 'CC')).toBe('1234');
    expect(sanitizeDocNumber('ab-12', 'PAS')).toBe('ab12');
  });
});
