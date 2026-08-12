import { describe, expect, it } from 'vitest';
import { furAutoObservations } from '../fur-auto-observations';
import type { FieldValue } from '@/lib/api/types/procedure-runtime';

/**
 * Vista previa de lo que el backend anexa solo al recuadro de observaciones del FUR.
 *
 * Los ejemplos son los MISMOS que usan `FurServicioVinculadoraObservationTests` y
 * `FurTransformationObservationsTests` en el backend, que es quien manda: si una redacción cambia
 * allá y no aquí, estos tests lo delatan en vez de dejar que la vista previa mienta.
 */

function fields(pairs: Record<string, string>): FieldValue[] {
  return Object.entries(pairs).map(([fieldKey, valueText]) => ({ fieldKey, valueText }) as FieldValue);
}

describe('furAutoObservations', () => {
  it('sin datos no promete nada', () => {
    expect(furAutoObservations([])).toEqual([]);
    expect(furAutoObservations(null)).toEqual([]);
  });

  // ── Transformaciones (ADR-0029) ────────────────────────────────────────────

  it('declara el color NUEVO cuando difiere del snapshot RUNT', () => {
    expect(
      furAutoObservations(fields({ vehicle_color_runt: 'AZUL', vehicle_color: 'ROJO' })),
    ).toEqual(['Cambio de color: ROJO.']);
  });

  it('sin cambio real no declara transformación', () => {
    expect(
      furAutoObservations(fields({ vehicle_color_runt: 'AZUL', vehicle_color: 'azul' })),
    ).toEqual([]);
  });

  it('sin snapshot RUNT no hay diff que declarar', () => {
    expect(furAutoObservations(fields({ vehicle_color: 'ROJO' }))).toEqual([]);
  });

  it('acumula color, combustible y carrocería en el orden del backend', () => {
    expect(
      furAutoObservations(
        fields({
          vehicle_color_runt: 'AZUL',
          vehicle_color: 'ROJO',
          vehicle_fuel_runt: 'GASOLINA',
          vehicle_fuel: 'DIESEL',
          vehicle_body_type_runt: 'SEDAN',
          vehicle_body_type: 'CAMIONETA',
        }),
      ),
    ).toEqual([
      'Cambio de color: ROJO.',
      'Cambio de combustible: DIESEL.',
      'Cambio de carrocería: CAMIONETA.',
    ]);
  });

  // ── Tipo de servicio + empresa vinculadora ─────────────────────────────────

  it('declara servicio, razón social y NIT', () => {
    expect(
      furAutoObservations(
        fields({
          vehicle_service: 'PUBLICO',
          empresa_vinculadora_razon_social: 'TRANSPORTES SAS',
          empresa_vinculadora_nit: '900123456',
        }),
      ),
    ).toEqual(['Servicio: PÚBLICO. Empresa vinculadora: TRANSPORTES SAS, NIT 900123456.']);
  });

  it('sin NIT no deja comas sueltas', () => {
    expect(
      furAutoObservations(
        fields({ vehicle_service: 'PUBLICO', empresa_vinculadora_razon_social: 'TRANSPORTES SAS' }),
      ),
    ).toEqual(['Servicio: PÚBLICO. Empresa vinculadora: TRANSPORTES SAS.']);
  });

  it('sin empresa vinculadora no anuncia nada, ni el servicio suelto', () => {
    expect(furAutoObservations(fields({ vehicle_service: 'PARTICULAR' }))).toEqual([]);
  });

  it('un valor de servicio fuera del catálogo no se pierde', () => {
    expect(
      furAutoObservations(
        fields({ vehicle_service: 'carga pesada', empresa_vinculadora_razon_social: 'TRANSPORTES SAS' }),
      ),
    ).toEqual(['Servicio: CARGA PESADA. Empresa vinculadora: TRANSPORTES SAS.']);
  });

  it('transformaciones y vinculadora conviven, con la vinculadora al final', () => {
    const segments = furAutoObservations(
      fields({
        vehicle_color_runt: 'AZUL',
        vehicle_color: 'ROJO',
        vehicle_service: 'PUBLICO',
        empresa_vinculadora_razon_social: 'TRANSPORTES SAS',
        empresa_vinculadora_nit: '900123456',
      }),
    );

    expect(segments).toHaveLength(2);
    expect(segments[0]).toBe('Cambio de color: ROJO.');
    expect(segments[1]).toContain('Empresa vinculadora');
  });
});
