import { describe, expect, it } from 'vitest';
import { ilustracionDelAsistente } from '../TramiteWizard';

// HU #14482 — el encabezado del asistente lleva la ilustración del tipo de trámite.
describe('ilustracionDelAsistente', () => {
  const base = { pideVendedor: false, entraPorVin: false, tipoCodigo: '', familia: '' };

  it('traspaso por capacidades o por el código del tipo, aunque la familia no lo diga', () => {
    expect(ilustracionDelAsistente({ ...base, pideVendedor: true, familia: 'OTROS' })).toBe('traspaso');
    expect(ilustracionDelAsistente({ ...base, tipoCodigo: 'TRASPASO_STANDARD', familia: 'OTROS' })).toBe('traspaso');
  });

  it('matrícula por entrada por VIN, por código o por familia (también la modalidad heredada)', () => {
    expect(ilustracionDelAsistente({ ...base, entraPorVin: true })).toBe('matricula');
    expect(ilustracionDelAsistente({ ...base, tipoCodigo: 'MATRICULA_INICIAL' })).toBe('matricula');
    expect(ilustracionDelAsistente({ ...base, familia: 'matricula_inicial' })).toBe('matricula');
  });

  it('los demás tipos usan la genérica de trámites', () => {
    expect(ilustracionDelAsistente({ ...base, tipoCodigo: 'LEVANTAMIENTO_PRENDA', familia: 'OTROS' })).toBe('tramites');
  });
});
