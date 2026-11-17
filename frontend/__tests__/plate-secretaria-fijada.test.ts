import { describe, expect, it } from 'vitest';
import { placaQueFijaSecretaria } from '@/lib/tramites/plate-digit-preference';
import { carroceriaEsAusente, vehiculoSinCarroceria } from '@/lib/tramites/transformaciones-vehiculo';

const f = (fieldKey: string, valueText: string, source = 'ict') => ({ fieldKey, valueText, source });

describe('placaQueFijaSecretaria', () => {
  it('tipo 4 con placa ict devuelve la placa', () => {
    expect(placaQueFijaSecretaria([f('plate', 'ABC123'), f('plate_assignment_type', '4')])).toBe('ABC123');
  });
  it.each(['2', '3', ''])('tipo "%s" no fija la secretaría', (tipo) => {
    expect(placaQueFijaSecretaria([f('plate', 'ABC123'), f('plate_assignment_type', tipo)])).toBeNull();
  });
  it('sin tipo, o con placa que no es de la integración, no fija nada', () => {
    expect(placaQueFijaSecretaria([f('plate', 'ABC123')])).toBeNull();
    expect(
      placaQueFijaSecretaria([f('plate', 'ABC123', 'consultation'), f('plate_assignment_type', '4')]),
    ).toBeNull();
  });
});

describe('vehiculoSinCarroceria (espejo de VehicleBodyTypePolicy.SinCarroceria)', () => {
  it.each([['SIN CARROCERIA'], ['  sin   carrocería '], ['-'], ['  '], ['']])('"%s" es ausente', (v) => {
    expect(carroceriaEsAusente(v)).toBe(true);
  });
  it('un valor real no es ausente', () => {
    expect(carroceriaEsAusente('SEDAN')).toBe(false);
  });
  it('sin el snapshot RUNT no se asume ausencia', () => {
    expect(vehiculoSinCarroceria([f('vehicle_body_type', '')])).toBe(false);
  });
  it('con snapshot vacío o SIN CARROCERIA sí', () => {
    expect(vehiculoSinCarroceria([f('vehicle_body_type_runt', 'SIN CARROCERIA')])).toBe(true);
    expect(vehiculoSinCarroceria([f('vehicle_body_type_runt', '')])).toBe(true);
    expect(vehiculoSinCarroceria([f('vehicle_body_type_runt', 'SEDAN')])).toBe(false);
  });
});
