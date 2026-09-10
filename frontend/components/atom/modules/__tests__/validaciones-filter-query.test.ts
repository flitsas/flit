import { describe, expect, it } from 'vitest';
import { splitPersonaODocumentoQuery } from '@/components/atom/modules/ValidacionesFilterToolbar';

describe('splitPersonaODocumentoQuery', () => {
  it('vacío no filtra', () => {
    expect(splitPersonaODocumentoQuery('  ')).toEqual({ name: '', documentNumber: '' });
  });

  it('nombre va a name', () => {
    expect(splitPersonaODocumentoQuery('Ana Compradora')).toEqual({
      name: 'Ana Compradora',
      documentNumber: '',
    });
  });

  it('cédula numérica va a documentNumber', () => {
    expect(splitPersonaODocumentoQuery('1020304050')).toEqual({
      name: '',
      documentNumber: '1020304050',
    });
  });
});
