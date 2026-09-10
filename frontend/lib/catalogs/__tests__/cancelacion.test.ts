import { describe, expect, it } from 'vitest';
import {
  CANCELACION_CAUSALES,
  CANCELACION_DOC_ACTO_JUDICIAL,
  CANCELACION_DOC_ASEGURADORA,
  CANCELACION_DOC_AUTORIDAD,
  CANCELACION_DOC_DIJIN,
  cancelacionObservacionFur,
  documentosDeCausal,
  esCancelacionDeMatricula,
  parseCancelacionCausal,
} from '../cancelacion';

/**
 * Contrato de la causal de cancelación. Los ejemplos son los MISMOS que usan `CancelacionCausalTests`
 * y `FurTramiteObservationTests` en el backend, que es quien manda: si una redacción o un documento
 * cambia allá y no aquí, estos tests lo delatan en vez de dejar que el asistente mienta sobre lo que
 * el trámite va a exigir.
 */

describe('parseCancelacionCausal', () => {
  it('reconoce las cuatro causales', () => {
    expect(CANCELACION_CAUSALES.map((c) => c.codigo)).toEqual([
      'DECISION_JUDICIAL',
      'PERDIDA_TOTAL_FUERZA_MAYOR',
      'PERDIDA_TOTAL_ACCIDENTE',
      'DECISION_VOLUNTARIA',
    ]);
    for (const { codigo } of CANCELACION_CAUSALES) {
      expect(parseCancelacionCausal(codigo)).toBe(codigo);
    }
  });

  it('normaliza espacios y mayúsculas', () => {
    expect(parseCancelacionCausal('  decision_voluntaria  ')).toBe('DECISION_VOLUNTARIA');
  });

  it.each([null, undefined, '', '   ', 'PERDIDA_TOTAL', 'true'])(
    'no adivina una causal con %o',
    (valor) => {
      expect(parseCancelacionCausal(valor)).toBeNull();
      expect(documentosDeCausal(parseCancelacionCausal(valor))).toEqual([]);
    },
  );
});

describe('documentosDeCausal', () => {
  it('la decisión judicial se acredita con el acto del juez', () => {
    expect(documentosDeCausal('DECISION_JUDICIAL')).toEqual([CANCELACION_DOC_ACTO_JUDICIAL]);
  });

  it.each(['PERDIDA_TOTAL_FUERZA_MAYOR', 'PERDIDA_TOTAL_ACCIDENTE'] as const)(
    '%s exige los tres certificados, no uno cualquiera',
    (causal) => {
      expect(documentosDeCausal(causal)).toEqual([
        CANCELACION_DOC_DIJIN,
        CANCELACION_DOC_ASEGURADORA,
        CANCELACION_DOC_AUTORIDAD,
      ]);
    },
  );

  it('la decisión voluntaria se acredita con el certificado de la DIJIN', () => {
    expect(documentosDeCausal('DECISION_VOLUNTARIA')).toEqual([CANCELACION_DOC_DIJIN]);
  });
});

describe('cancelacionObservacionFur', () => {
  it.each([
    ['DECISION_JUDICIAL', 'CANCELACIÓN POR DECISIÓN JUDICIAL.'],
    ['PERDIDA_TOTAL_FUERZA_MAYOR', 'CANCELACIÓN POR PÉRDIDA TOTAL - FUERZA MAYOR.'],
    ['PERDIDA_TOTAL_ACCIDENTE', 'CANCELACIÓN POR PÉRDIDA TOTAL - ACCIDENTE.'],
    ['DECISION_VOLUNTARIA', 'CANCELACIÓN POR DECISIÓN VOLUNTARIA.'],
  ] as const)('%s imprime su literal', (causal, esperado) => {
    expect(cancelacionObservacionFur(causal)).toBe(esperado);
  });

  it('sin causal no inventa motivo', () => {
    expect(cancelacionObservacionFur(null)).toBeNull();
  });
});

describe('esCancelacionDeMatricula', () => {
  it.each(['CANCELACION_MATRICULA', 'cancelacion_matricula', '  CANCELACION_MATRICULA '])(
    'reconoce el tipo con %o',
    (codigo) => {
      expect(esCancelacionDeMatricula(codigo)).toBe(true);
    },
  );

  it.each([null, undefined, '', 'DUPLICADO_PLACA', 'MATRICULA_NUEVA'])(
    'no lo confunde con %o',
    (codigo) => {
      expect(esCancelacionDeMatricula(codigo)).toBe(false);
    },
  );
});
