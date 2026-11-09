// HU #11628 — el dígito de preferencia de placa exige una elección consciente.
//
// Uso de ejemplo:
//   isPlateDigitUndecided({ muestraDigitoPlaca: true, vehiculoConPlacaRunt: false,
//     transitOfficeId: 'ot-1', preasignacionActiva: true, digitoPlacaUiValue: '' }) → true
//   toPlateDigitFieldValues('none') → [{ fieldKey: 'plate_preferred_last_digit', valueText: '' },
//     { fieldKey: 'plate_preferred_last_digit_declared', valueText: 'true' }]
import { describe, expect, it } from 'vitest';
import {
  DIGITO_PLACA_NO_DECIDIDO,
  DIGITO_PLACA_SIN_PREFERENCIA,
  PLATE_PREFERRED_LAST_DIGIT_DECLARED_KEY,
  PLATE_PREFERRED_LAST_DIGIT_KEY,
  isPlateDigitDecisionRequired,
  isPlateDigitUndecided,
  toPlateDigitFieldValues,
  avisoDigitoPlacaOt,
  esPlacaAsignadaPorOt,
  toPlateDigitUiValue,
} from '../plate-digit-preference';

const baseParams = {
  muestraDigitoPlaca: true,
  vehiculoConPlacaRunt: false,
  transitOfficeId: 'ot-1',
  preasignacionActiva: true as boolean | null,
};

describe('isPlateDigitDecisionRequired', () => {
  it('AC1 — exige decisión con matrícula, sin placa RUNT, organismo elegido y preasignación activa', () => {
    expect(isPlateDigitDecisionRequired(baseParams)).toBe(true);
  });

  it('AC4 — NO exige decisión cuando la preasignación está apagada (selector deshabilitado)', () => {
    expect(isPlateDigitDecisionRequired({ ...baseParams, preasignacionActiva: false })).toBe(false);
  });

  it('AC4 — NO exige decisión mientras se consulta el estado de la ruta (null = cargando)', () => {
    expect(isPlateDigitDecisionRequired({ ...baseParams, preasignacionActiva: null })).toBe(false);
  });

  it('no exige decisión sin organismo elegido todavía', () => {
    expect(isPlateDigitDecisionRequired({ ...baseParams, transitOfficeId: '' })).toBe(false);
  });

  it('AC2 (HU #10799) — no exige decisión si el vehículo ya trae placa del RUNT', () => {
    expect(isPlateDigitDecisionRequired({ ...baseParams, vehiculoConPlacaRunt: true })).toBe(false);
  });

  it('no exige decisión fuera del paso de matrícula (traspaso)', () => {
    expect(isPlateDigitDecisionRequired({ ...baseParams, muestraDigitoPlaca: false })).toBe(false);
  });
});

describe('isPlateDigitUndecided', () => {
  it('AC1 — sin decidir (valor vacío) con decisión exigible → true, bloquea Continuar', () => {
    expect(
      isPlateDigitUndecided({ ...baseParams, digitoPlacaUiValue: DIGITO_PLACA_NO_DECIDIDO }),
    ).toBe(true);
  });

  it('AC2 — "sin preferencia" declarada explícitamente → false, no bloquea', () => {
    expect(
      isPlateDigitUndecided({ ...baseParams, digitoPlacaUiValue: DIGITO_PLACA_SIN_PREFERENCIA }),
    ).toBe(false);
  });

  it('AC3 — dígito elegido (0-9) → false, no bloquea', () => {
    expect(isPlateDigitUndecided({ ...baseParams, digitoPlacaUiValue: '7' })).toBe(false);
  });

  it('AC4 — valor vacío pero decisión NO exigible (preasignación apagada) → false, no bloquea', () => {
    expect(
      isPlateDigitUndecided({
        ...baseParams,
        preasignacionActiva: false,
        digitoPlacaUiValue: DIGITO_PLACA_NO_DECIDIDO,
      }),
    ).toBe(false);
  });
});

describe('toPlateDigitFieldValues', () => {
  it('AC3 — dígito elegido persiste el mismo contrato de siempre (dígito) + declared=true', () => {
    expect(toPlateDigitFieldValues('4')).toEqual([
      { fieldKey: PLATE_PREFERRED_LAST_DIGIT_KEY, valueText: '4' },
      { fieldKey: PLATE_PREFERRED_LAST_DIGIT_DECLARED_KEY, valueText: 'true' },
    ]);
  });

  it('AC2 — "sin preferencia" persiste el contrato como cadena vacía (AUSENCIA, sin cambiarlo) + declared=true', () => {
    expect(toPlateDigitFieldValues(DIGITO_PLACA_SIN_PREFERENCIA)).toEqual([
      { fieldKey: PLATE_PREFERRED_LAST_DIGIT_KEY, valueText: '' },
      { fieldKey: PLATE_PREFERRED_LAST_DIGIT_DECLARED_KEY, valueText: 'true' },
    ]);
  });

  it('no decidido persiste cadena vacía + declared=false (distinguible de "sin preferencia")', () => {
    expect(toPlateDigitFieldValues(DIGITO_PLACA_NO_DECIDIDO)).toEqual([
      { fieldKey: PLATE_PREFERRED_LAST_DIGIT_KEY, valueText: '' },
      { fieldKey: PLATE_PREFERRED_LAST_DIGIT_DECLARED_KEY, valueText: 'false' },
    ]);
  });

  it('contrato: el valueText de plate_preferred_last_digit nunca es "none" — o dígito o cadena vacía', () => {
    for (const uiValue of ['', 'none', '0', '9']) {
      const [digitField] = toPlateDigitFieldValues(uiValue);
      expect(digitField.fieldKey).toBe(PLATE_PREFERRED_LAST_DIGIT_KEY);
      expect(digitField.valueText === '' || /^[0-9]$/.test(digitField.valueText)).toBe(true);
    }
  });
});

describe('toPlateDigitUiValue — rehidratación', () => {
  it('AC3 — dígito persistido gana siempre, sin importar la marca declared', () => {
    expect(toPlateDigitUiValue({ rawDigit: '5', declared: false })).toBe('5');
    expect(toPlateDigitUiValue({ rawDigit: '5', declared: true })).toBe('5');
  });

  it('AC2 — sin dígito pero declared=true → "sin preferencia" (distinguible de no decidido)', () => {
    expect(toPlateDigitUiValue({ rawDigit: '', declared: true })).toBe(DIGITO_PLACA_SIN_PREFERENCIA);
  });

  it('AC1 — sin dígito y declared=false → no decidido', () => {
    expect(toPlateDigitUiValue({ rawDigit: '', declared: false })).toBe(DIGITO_PLACA_NO_DECIDIDO);
  });
});

describe('AC5 — regresión de ruteo', () => {
  it('sin placa y sin dígito declarado, el contrato persistido sigue siendo cadena vacía (misma señal de siempre para IPlatePreassignPolicy)', () => {
    // El backend enruta por AUSENCIA de placa, no por este dígito (OtClientProcedure.cs:42-48).
    // Esta prueba documenta que la nueva marca `_declared` no altera el valor que el backend lee.
    const [digitField] = toPlateDigitFieldValues(DIGITO_PLACA_NO_DECIDIDO);
    expect(digitField.fieldKey).toBe(PLATE_PREFERRED_LAST_DIGIT_KEY);
    expect(digitField.valueText).toBe('');
  });
});

// HU 3 (registro diferido 2026-11-09) — aviso al gestor sobre la placa que asignó el OT frente a
// su dígito de preferencia.
//
// Uso de ejemplo:
//   avisoDigitoPlacaOt({ placa: 'ABC125', source: 'ot', digito: '5' })
//     → { tono: 'exito', texto: 'La placa ABC125 asignada por el organismo de tránsito termina en 5, tu dígito de preferencia.' }
describe('esPlacaAsignadaPorOt', () => {
  it('source "ot" con placa → asignada por el OT', () => {
    expect(esPlacaAsignadaPorOt('ABC125', 'ot')).toBe(true);
  });
  it('placa anterior al cambio (source vacío o null) → asignada por el OT', () => {
    expect(esPlacaAsignadaPorOt('ABC125', '')).toBe(true);
    expect(esPlacaAsignadaPorOt('ABC125', null)).toBe(true);
    expect(esPlacaAsignadaPorOt('ABC125', undefined)).toBe(true);
  });
  it('placa del RUNT o elegida por el gestor no es del OT; sin placa tampoco', () => {
    expect(esPlacaAsignadaPorOt('ABC125', 'consultation')).toBe(false);
    expect(esPlacaAsignadaPorOt('ABC125', 'user')).toBe(false);
    expect(esPlacaAsignadaPorOt('', 'ot')).toBe(false);
    expect(esPlacaAsignadaPorOt('  ', '')).toBe(false);
    expect(esPlacaAsignadaPorOt(null, 'ot')).toBe(false);
  });
});

describe('avisoDigitoPlacaOt', () => {
  it('AC coincide — tono éxito y literal exacto', () => {
    expect(avisoDigitoPlacaOt({ placa: 'ABC125', source: 'ot', digito: '5' })).toEqual({
      tono: 'exito',
      texto: 'La placa ABC125 asignada por el organismo de tránsito termina en 5, tu dígito de preferencia.',
    });
  });
  it('AC no coincide — tono informativo y literal exacto', () => {
    expect(avisoDigitoPlacaOt({ placa: 'ABC123', source: 'ot', digito: '5' })).toEqual({
      tono: 'info',
      texto:
        'La placa ABC123 asignada por el organismo de tránsito no termina en tu dígito de preferencia (5). El organismo lo toma solo como guía.',
    });
  });
  it('AC sin preferencia — vacía, "none" o nula → null', () => {
    expect(avisoDigitoPlacaOt({ placa: 'ABC125', source: 'ot', digito: '' })).toBeNull();
    expect(avisoDigitoPlacaOt({ placa: 'ABC125', source: 'ot', digito: 'none' })).toBeNull();
    expect(avisoDigitoPlacaOt({ placa: 'ABC125', source: 'ot', digito: null })).toBeNull();
  });
  it('placa con source vacío (asignada antes del cambio) también avisa; RUNT no', () => {
    expect(avisoDigitoPlacaOt({ placa: 'abc125 ', source: '', digito: '5' })?.texto).toBe(
      'La placa ABC125 asignada por el organismo de tránsito termina en 5, tu dígito de preferencia.',
    );
    expect(avisoDigitoPlacaOt({ placa: 'ABC125', source: 'consultation', digito: '5' })).toBeNull();
    expect(avisoDigitoPlacaOt({ placa: '', source: 'ot', digito: '5' })).toBeNull();
  });
});

// HU7 (registro diferido 2026-11-09, corrección del usuario) — el aviso depende de la clase:
// MOTO (lista exacta) compara el PRIMER dígito numérico (ABC52D → 5); CARRO y sin grupo, el ÚLTIMO
// dígito numérico (ABC125 → 5). Nunca el último carácter.
// Uso de ejemplo: avisoDigitoPlacaOt({ placa: 'ABC52D', source: 'ot', digito: '5', clase: 'MOTOCICLETA' }) → tono 'exito'
describe('avisoDigitoPlacaOt — dígito según la clase (HU7)', () => {
  it('moto ABC52D con preferencia 5 → coincide con literal de primer número', () => {
    expect(avisoDigitoPlacaOt({ placa: 'ABC52D', source: 'ot', digito: '5', clase: 'Motocicleta' })).toEqual({
      tono: 'exito',
      texto:
        'La placa ABC52D asignada por el organismo de tránsito tiene como primer número el 5, tu dígito de preferencia.',
    });
  });
  it('moto ABC52 con preferencia 5 → coincide', () => {
    expect(avisoDigitoPlacaOt({ placa: 'ABC52', source: 'ot', digito: '5', clase: 'CICLOMOTOR' })?.tono).toBe('exito');
  });
  it('moto ABC12D con preferencia 2 → NO coincide (no compara el último dígito ni la letra)', () => {
    expect(avisoDigitoPlacaOt({ placa: 'ABC12D', source: 'ot', digito: '2', clase: 'MOTOCICLETA' })).toEqual({
      tono: 'info',
      texto:
        'La placa ABC12D asignada por el organismo de tránsito no tiene como primer número tu dígito de preferencia (2). El organismo lo toma solo como guía.',
    });
  });
  it('carro ABC125 con 5 → coincide con el literal actual «termina en»', () => {
    expect(avisoDigitoPlacaOt({ placa: 'ABC125', source: 'ot', digito: '5', clase: 'AUTOMOVIL' })).toEqual({
      tono: 'exito',
      texto: 'La placa ABC125 asignada por el organismo de tránsito termina en 5, tu dígito de preferencia.',
    });
  });
  it('sin clase compara el ÚLTIMO dígito numérico, no el último carácter (ABC12D → 2)', () => {
    expect(avisoDigitoPlacaOt({ placa: 'ABC12D', source: 'ot', digito: '2' })).toEqual({
      tono: 'exito',
      texto: 'La placa ABC12D asignada por el organismo de tránsito termina en 2, tu dígito de preferencia.',
    });
    expect(avisoDigitoPlacaOt({ placa: 'ABC12D', source: 'ot', digito: '1', clase: '' })?.tono).toBe('info');
  });
  it('placa sin dígitos → sin aviso', () => {
    expect(avisoDigitoPlacaOt({ placa: 'ABCDEF', source: 'ot', digito: '5' })).toBeNull();
    expect(avisoDigitoPlacaOt({ placa: 'ABCDEF', source: 'ot', digito: '5', clase: 'MOTOCICLETA' })).toBeNull();
  });
});

// HU10 — el motocarro usa la rama del primer número y los literales de moto.
// Uso de ejemplo: avisoDigitoPlacaOt({ placa: '523ABC', source: 'ot', digito: '5', clase: 'MOTOCARRO' }) → éxito
describe('avisoDigitoPlacaOt — motocarro (HU10)', () => {
  it('523ABC con 5 → «tiene como primer número el 5»', () => {
    expect(avisoDigitoPlacaOt({ placa: '523ABC', source: 'ot', digito: '5', clase: 'MOTOCARRO' })).toEqual({
      tono: 'exito',
      texto:
        'La placa 523ABC asignada por el organismo de tránsito tiene como primer número el 5, tu dígito de preferencia.',
    });
  });
  it('123ABC con 3 en un TRICIMOTO → no coincide (compara el primero, no el último)', () => {
    expect(avisoDigitoPlacaOt({ placa: '123ABC', source: 'ot', digito: '3', clase: 'TRICIMOTO' })).toEqual({
      tono: 'info',
      texto:
        'La placa 123ABC asignada por el organismo de tránsito no tiene como primer número tu dígito de preferencia (3). El organismo lo toma solo como guía.',
    });
  });
});
