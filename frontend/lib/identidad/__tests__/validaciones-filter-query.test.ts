import { describe, expect, it } from 'vitest';
import { splitPersonaODocumentoQuery } from '@/lib/identidad/validaciones-filtros';

describe('splitPersonaODocumentoQuery', () => {
  it('vacío no filtra', () => {
    expect(splitPersonaODocumentoQuery('  ')).toEqual({ name: '', documentNumber: '', referenceNumber: '' });
  });

  it('nombre va a name', () => {
    expect(splitPersonaODocumentoQuery('Ana Compradora')).toEqual({
      name: 'Ana Compradora',
      documentNumber: '',
      // Ajuste 2026-11-10 (D1=a) — la caja también busca por radicado: la forma lleva la tercera clave.
      referenceNumber: '',
    });
  });

  it('cédula numérica va a documentNumber', () => {
    expect(splitPersonaODocumentoQuery('1020304050')).toEqual({
      name: '',
      documentNumber: '1020304050',
      referenceNumber: '',
    });
  });
});
