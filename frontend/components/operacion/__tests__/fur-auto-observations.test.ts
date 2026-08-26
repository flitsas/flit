import { describe, expect, it } from 'vitest';
import {
  FUR_OBSERVACIONES_PRESUPUESTO,
  furAutoObservations,
  furObservacionesDisponibles,
  furObservationsPreview,
} from '../fur-auto-observations';
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
    ).toEqual(['Color nuevo(NUEVO COLOR: ROJO)']);
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
      'Color nuevo(NUEVO COLOR: ROJO)',
      'Carroceria nueva(NUEVA CARROCERIA: CAMIONETA)',
      'COMBUSTIBLE_NUEVO: DIESEL',
    ]);
  });

  // ── Blindaje ───────────────────────────────────────────────────────────────

  it.each([
    ['NIVEL_1', 'BLINDAJE NIVEL 1.'],
    ['NIVEL_2', 'BLINDAJE NIVEL 2.'],
    ['NIVEL_3', 'BLINDAJE NIVEL 3.'],
    ['DESMONTE', 'DESMONTE DE BLINDAJE.'],
  ])('anuncia %s como %s', (opcion, esperado) => {
    // La casilla del FUR es un SÍ/NO y no distingue el nivel; el detalle solo cabe aquí.
    expect(furAutoObservations(fields({ blindaje_nivel: opcion }))).toEqual([esperado]);
  });

  it('sin opción de blindaje no anuncia nada', () => {
    expect(furAutoObservations(fields({ blindaje: 'true' }))).toEqual([]);
  });

  it('el blindaje va después de las transformaciones, como lo une el backend', () => {
    expect(
      furAutoObservations(
        fields({
          vehicle_color_runt: 'AZUL',
          vehicle_color: 'ROJO',
          blindaje_nivel: 'NIVEL_3',
        }),
      ),
    ).toEqual(['Color nuevo(NUEVO COLOR: ROJO)', 'BLINDAJE NIVEL 3.']);
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
    expect(segments[0]).toBe('Color nuevo(NUEVO COLOR: ROJO)');
    expect(segments[1]).toContain('Empresa vinculadora');
  });
});

describe('furObservationsPreview', () => {
  const vinculadora = fields({
    vehicle_service: 'PUBLICO',
    empresa_vinculadora_razon_social: 'TRANSPORTES SAS',
  });

  it('lo escrito va PRIMERO, como lo une el backend', () => {
    const { manual, auto } = furObservationsPreview('Entrega en ventanilla', vinculadora);

    expect(manual).toBe('Entrega en ventanilla');
    expect(auto).toEqual(['Servicio: PÚBLICO. Empresa vinculadora: TRANSPORTES SAS.']);
  });

  it('refleja el texto tal cual se va escribiendo, sin esperar a que se guarde', () => {
    // Lo que ve el gestor tecla a tecla: la vista previa se deriva del estado, no del persistido.
    expect(furObservationsPreview('Entr', vinculadora).manual).toBe('Entr');
    expect(furObservationsPreview('Entrega', vinculadora).manual).toBe('Entrega');
  });

  it('un texto en blanco no ocupa renglón', () => {
    expect(furObservationsPreview('   \n  ', vinculadora).manual).toBeNull();
    expect(furObservationsPreview(null, vinculadora).manual).toBeNull();
  });

  it('sin nada que mostrar, ni manual ni automático', () => {
    expect(furObservationsPreview('', [])).toEqual({ manual: null, auto: [] });
  });

  it('conserva los saltos de línea que escribe el gestor', () => {
    expect(furObservationsPreview('Línea 1\nLínea 2', []).manual).toBe('Línea 1\nLínea 2');
  });
});

/**
 * HU #11643 — el recuadro del FUR tiene sitio contado y el bloque automático entra primero, así que
 * lo que le queda al texto libre depende de cuánto ocupe aquello. El gestor tiene que enterarse
 * MIENTRAS escribe: antes lo descubría con el PDF ya generado, y lo que se perdía era justo lo suyo.
 */
describe('furObservacionesDisponibles', () => {
  it('sin texto automático, el presupuesto entero es para el gestor', () => {
    expect(furObservacionesDisponibles([])).toBe(FUR_OBSERVACIONES_PRESUPUESTO);
  });

  it('descuenta el bloque automático y su espacio separador', () => {
    // 30 caracteres + 1 de separación.
    expect(furObservacionesDisponibles(['Color nuevo(NUEVO COLOR: NEGRO MATE)'])).toBe(
      FUR_OBSERVACIONES_PRESUPUESTO - 'Color nuevo(NUEVO COLOR: NEGRO MATE)'.length - 1,
    );
  });

  it('varias declaraciones reducen más el espacio disponible', () => {
    const una = furObservacionesDisponibles(['Color nuevo(NUEVO COLOR: NEGRO)']);
    const dos = furObservacionesDisponibles([
      'Color nuevo(NUEVO COLOR: NEGRO)',
      'Carroceria nueva(NUEVA CARROCERIA: FURGON)',
    ]);
    expect(dos).toBeLessThan(una);
  });

  it('si lo automático agota el presupuesto, no queda nada y nunca es negativo', () => {
    const enorme = 'X'.repeat(FUR_OBSERVACIONES_PRESUPUESTO + 100);
    expect(furObservacionesDisponibles([enorme])).toBe(0);
  });
});
