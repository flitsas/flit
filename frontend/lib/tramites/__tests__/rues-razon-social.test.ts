import { describe, expect, it } from 'vitest';
import { shortRuesRazonSocial } from '@/lib/tramites/rues-razon-social';

describe('shortRuesRazonSocial', () => {
  it('deja la razón social intacta si no hay coma', () => {
    expect(shortRuesRazonSocial('EMPRESA DEMO S.A.S.')).toBe('EMPRESA DEMO S.A.S.');
  });

  it('corta en la primera coma y recorta espacios', () => {
    expect(
      shortRuesRazonSocial(
        'BANCOLOMBIA S.A., ADEMÁS  PODRÁ GIRAR BAJO LA DENOMINACIÓN BANCO DE COLOMBIA S.A.',
      ),
    ).toBe('BANCOLOMBIA S.A.');
  });

  it('devuelve vacío si no hay texto', () => {
    expect(shortRuesRazonSocial('  ')).toBe('');
    expect(shortRuesRazonSocial(null)).toBe('');
  });
});
