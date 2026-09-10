import { describe, expect, it } from 'vitest';
import type { ProcedureAttachment } from '@/lib/api/types/procedure-runtime';
import {
  BLINDAJE_DOC_TIPO,
  BLINDAJE_OPCIONES,
  blindajeCompleto,
  blindajeObservacionFur,
  dejaElVehiculoBlindado,
  parseBlindajeOpcion,
} from '../blindaje';

/**
 * Contrato de la opción de blindaje. Los ejemplos son los MISMOS que usan `BlindajeOpcionTests` y
 * `FurBlindajeObservationTests` en el backend, que es quien manda: si una redacción cambia allá y no
 * aquí, estos tests lo delatan en vez de dejar que la vista previa mienta.
 */

function attachment(tipo: string): ProcedureAttachment {
  return { id: crypto.randomUUID(), tipo, filename: `${tipo}.pdf` } as ProcedureAttachment;
}

describe('parseBlindajeOpcion', () => {
  it('reconoce los cuatro códigos', () => {
    expect(BLINDAJE_OPCIONES.map((o) => o.codigo)).toEqual([
      'NIVEL_1',
      'NIVEL_2',
      'NIVEL_3',
      'DESMONTE',
    ]);
    for (const { codigo } of BLINDAJE_OPCIONES) {
      expect(parseBlindajeOpcion(codigo)).toBe(codigo);
    }
  });

  it('normaliza espacios y mayúsculas', () => {
    expect(parseBlindajeOpcion('  nivel_2  ')).toBe('NIVEL_2');
  });

  it.each([null, undefined, '', '   ', 'NIVEL_4', 'true'])(
    'no adivina un nivel con %o',
    (valor) => {
      // Sin opción el FUR marca casilla pero NO inventa el texto: adivinar aquí declararía ante el
      // organismo un blindaje que nadie eligió.
      expect(parseBlindajeOpcion(valor)).toBeNull();
    },
  );
});

describe('dejaElVehiculoBlindado', () => {
  it('solo los tres niveles dejan el vehículo blindado', () => {
    expect(dejaElVehiculoBlindado('NIVEL_1')).toBe(true);
    expect(dejaElVehiculoBlindado('NIVEL_2')).toBe(true);
    expect(dejaElVehiculoBlindado('NIVEL_3')).toBe(true);
  });

  it('el desmonte lo deja SIN blindaje, aunque el trámite sea un blindaje', () => {
    expect(dejaElVehiculoBlindado('DESMONTE')).toBe(false);
    expect(dejaElVehiculoBlindado(null)).toBe(false);
  });
});

describe('blindajeObservacionFur', () => {
  it.each([
    ['NIVEL_1', 'BLINDAJE NIVEL 1.'],
    ['NIVEL_2', 'BLINDAJE NIVEL 2.'],
    ['NIVEL_3', 'BLINDAJE NIVEL 3.'],
    ['DESMONTE', 'DESMONTE DE BLINDAJE.'],
  ] as const)('%s se declara como %s', (opcion, esperado) => {
    expect(blindajeObservacionFur(opcion)).toBe(esperado);
  });

  it('sin opción no promete nada', () => {
    expect(blindajeObservacionFur(null)).toBeNull();
  });
});

describe('blindajeCompleto', () => {
  it('exige opción declarada Y certificado', () => {
    expect(blindajeCompleto(null, [attachment(BLINDAJE_DOC_TIPO)])).toBe(false);
    expect(blindajeCompleto('NIVEL_2', [])).toBe(false);
    expect(blindajeCompleto('NIVEL_2', [attachment(BLINDAJE_DOC_TIPO)])).toBe(true);
  });

  it('el certificado es obligatorio también en el desmonte', () => {
    // Retirar un blindaje también hay que acreditarlo ante el organismo.
    expect(blindajeCompleto('DESMONTE', [])).toBe(false);
    expect(blindajeCompleto('DESMONTE', [attachment(BLINDAJE_DOC_TIPO)])).toBe(true);
  });

  it('otro adjunto del expediente no cuenta como certificado', () => {
    expect(blindajeCompleto('NIVEL_1', [attachment('otro'), attachment('soat')])).toBe(false);
  });
});
