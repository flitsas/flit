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
