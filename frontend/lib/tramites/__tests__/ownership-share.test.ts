// Múltiple Propietario (ADR-0053) — lógica pura de reparto porcentual.
import { describe, expect, it } from 'vitest';
import type { ProcedureActor } from '@/lib/api/types/procedure-runtime';
import {
  MAX_OWNERS_PER_SIDE,
  actorsOrderedByOrdinal,
  OWNERSHIP_SUM_MESSAGE,
  OWNERSHIP_ZERO_MESSAGE,
  applySolidarioAbsorption,
  computeOrdinals,
  defaultPercentageForNewActor,
  duplicateDocumentIndicesWithinSide,
  identityStatusForActor,
  indicesForRol,
  isFirstOfRol,
  redistributeAfterRemoval,
  round2,
  shiftIndexMapOnInsert,
  shiftIndexMapOnRemove,
  validateOwnershipShares,
  withOwnershipFields,
} from '../ownership-share';

function actor(overrides: Partial<ProcedureActor> = {}): ProcedureActor {
  return {
    rol: 'comprador',
    tipoDocumento: 'CC',
    numeroDocumento: '',
    nombreCompleto: '',
    email: '',
    ...overrides,
  };
}

describe('round2', () => {
  it('redondea a 2 decimales sin arrastrar basura de punto flotante', () => {
    expect(round2(33.333333)).toBe(33.33);
    expect(round2(66.666666)).toBe(66.67);
    expect(round2(0.1 + 0.2)).toBe(0.3);
  });
});

describe('indicesForRol / isFirstOfRol', () => {
  const actors = [
    actor({ rol: 'vendedor', numeroDocumento: '1' }),
    actor({ rol: 'comprador', numeroDocumento: '2' }),
    actor({ rol: 'comprador', numeroDocumento: '3' }),
  ];

  it('agrupa por rol respetando el orden del array', () => {
    expect(indicesForRol(actors, 'comprador')).toEqual([1, 2]);
    expect(indicesForRol(actors, 'vendedor')).toEqual([0]);
  });

  it('el primer índice del grupo es el ordinal=1 (principal/solidario)', () => {
    expect(isFirstOfRol(actors, 1)).toBe(true);
    expect(isFirstOfRol(actors, 2)).toBe(false);
    expect(isFirstOfRol(actors, 0)).toBe(true);
  });
});

describe('applySolidarioAbsorption', () => {
  it('con un solo actor por lado no toca nada (caso mayoritario, sin regresión)', () => {
    const actors = [actor({ rol: 'comprador', porcentaje: undefined })];
    expect(applySolidarioAbsorption(actors, new Set())).toBe(actors);
  });

  it('el ordinal=1 absorbe el residuo (100 − suma de los demás) mientras no se edite a mano', () => {
    const actors = [
      actor({ rol: 'comprador', porcentaje: 999 /* cualquier valor: se recalcula */ }),
      actor({ rol: 'comprador', porcentaje: 30 }),
    ];
    const next = applySolidarioAbsorption(actors, new Set());
    expect(next[0].porcentaje).toBe(70);
    expect(next[1].porcentaje).toBe(30);
  });

  it('deja de absorber una vez que el lado está marcado como editado a mano', () => {
    const actors = [
      actor({ rol: 'comprador', porcentaje: 40 }),
      actor({ rol: 'comprador', porcentaje: 30 }),
    ];
    const next = applySolidarioAbsorption(actors, new Set(['comprador']));
    expect(next).toBe(actors); // sin cambios: misma referencia
    expect(next[0].porcentaje).toBe(40);
  });

  it('el residuo puede quedar negativo si el gestor edita a los agregados antes que al principal', () => {
    const actors = [
      actor({ rol: 'vendedor', porcentaje: 999 }),
      actor({ rol: 'vendedor', porcentaje: 60 }),
      actor({ rol: 'vendedor', porcentaje: 60 }),
    ];
    const next = applySolidarioAbsorption(actors, new Set());
    expect(next[0].porcentaje).toBe(-20);
  });

  it('devuelve la MISMA referencia si el residuo ya coincide (no re-renderiza de más)', () => {
    const actors = [
      actor({ rol: 'comprador', porcentaje: 70 }),
      actor({ rol: 'comprador', porcentaje: 30 }),
    ];
    expect(applySolidarioAbsorption(actors, new Set())).toBe(actors);
  });

  it('cada lado (vendedor/comprador) es un reparto independiente', () => {
    const actors = [
      actor({ rol: 'vendedor', porcentaje: 0 }),
      actor({ rol: 'vendedor', porcentaje: 25 }),
      actor({ rol: 'comprador', porcentaje: 0 }),
      actor({ rol: 'comprador', porcentaje: 10 }),
    ];
    const next = applySolidarioAbsorption(actors, new Set());
    expect(next[0].porcentaje).toBe(75);
    expect(next[2].porcentaje).toBe(90);
  });
});

describe('redistributeAfterRemoval', () => {
  it('con un solo actor restante, queda con 100% escrito (encargo cerrado)', () => {
    const actors = [
      actor({ rol: 'comprador', numeroDocumento: '1', porcentaje: 60 }),
      actor({ rol: 'comprador', numeroDocumento: '2', porcentaje: 40 }),
    ];
    const next = redistributeAfterRemoval(actors, 1);
    expect(next).toHaveLength(1);
    expect(next[0].porcentaje).toBe(100);
  });

  it('redistribuye proporcionalmente entre los que quedan del mismo lado', () => {
    const actors = [
      actor({ rol: 'comprador', numeroDocumento: '1', porcentaje: 50 }),
      actor({ rol: 'comprador', numeroDocumento: '2', porcentaje: 30 }),
      actor({ rol: 'comprador', numeroDocumento: '3', porcentaje: 20 }),
    ];
    // Elimina al tercero (20%): se reparte entre 1 y 2 proporcional a su participación (50/80, 30/80).
    const next = redistributeAfterRemoval(actors, 2);
    expect(next).toHaveLength(2);
    expect(next[0].porcentaje).toBeCloseTo(62.5, 5);
    expect(next[1].porcentaje).toBeCloseTo(37.5, 5);
  });

  it('reparto equitativo si los remanentes estaban todos en 0 (residuo negativo agotado)', () => {
    const actors = [
      actor({ rol: 'comprador', numeroDocumento: '1', porcentaje: 0 }),
      actor({ rol: 'comprador', numeroDocumento: '2', porcentaje: 0 }),
      actor({ rol: 'comprador', numeroDocumento: '3', porcentaje: 100 }),
    ];
    const next = redistributeAfterRemoval(actors, 2);
    expect(next[0].porcentaje).toBe(50);
    expect(next[1].porcentaje).toBe(50);
  });

  it('no toca actores de otro lado', () => {
    const actors = [
      actor({ rol: 'vendedor', numeroDocumento: 'v1', porcentaje: 100 }),
      actor({ rol: 'comprador', numeroDocumento: 'c1', porcentaje: 60 }),
      actor({ rol: 'comprador', numeroDocumento: 'c2', porcentaje: 40 }),
    ];
    const next = redistributeAfterRemoval(actors, 2);
    expect(next).toHaveLength(2);
    expect(next[0].rol).toBe('vendedor');
    expect(next[0].porcentaje).toBe(100);
    expect(next[1].porcentaje).toBe(100);
  });
});

describe('validateOwnershipShares', () => {
  it('válido con un solo actor por lado, sin importar porcentaje', () => {
    const actors = [actor({ rol: 'comprador', porcentaje: undefined })];
    expect(validateOwnershipShares(actors).valid).toBe(true);
  });

  it('bloquea cuando la suma no es exactamente 100', () => {
    const actors = [
      actor({ rol: 'comprador', porcentaje: 50 }),
      actor({ rol: 'comprador', porcentaje: 49 }),
    ];
    const result = validateOwnershipShares(actors);
    expect(result.valid).toBe(false);
    expect(result.sumError).toBe(true);
    expect(result.zeroError).toBe(false);
  });

  it('bloquea cuando algún actor queda en 0% o menos', () => {
    const actors = [
      actor({ rol: 'comprador', porcentaje: 100 }),
      actor({ rol: 'comprador', porcentaje: 0 }),
    ];
    const result = validateOwnershipShares(actors);
    expect(result.valid).toBe(false);
    expect(result.zeroError).toBe(true);
  });

  it('los dos mensajes de bloqueo son textualmente los del encargo, no paráfrasis', () => {
    expect(OWNERSHIP_SUM_MESSAGE).toBe('La suma de los porcentajes debe ser exactamente 100%.');
    expect(OWNERSHIP_ZERO_MESSAGE).toBe(
      'Todos los propietarios deben tener un porcentaje mayor a 0%.',
    );
  });

  it('suma 100.00 exacta con redondeo de 2 decimales es válida (33.33+33.33+33.34)', () => {
    const actors = [
      actor({ rol: 'vendedor', porcentaje: 33.33 }),
      actor({ rol: 'vendedor', porcentaje: 33.33 }),
      actor({ rol: 'vendedor', porcentaje: 33.34 }),
    ];
    expect(validateOwnershipShares(actors).valid).toBe(true);
  });

  it('repartos independientes por lado: un lado inválido no contamina al otro', () => {
    const actors = [
      actor({ rol: 'vendedor', porcentaje: 50 }),
      actor({ rol: 'vendedor', porcentaje: 50 }),
      actor({ rol: 'comprador', porcentaje: 40 }),
      actor({ rol: 'comprador', porcentaje: 40 }),
    ];
    const result = validateOwnershipShares(actors);
    expect(result.valid).toBe(false);
    expect(result.sumError).toBe(true);
  });
});

describe('duplicateDocumentIndicesWithinSide', () => {
  it('detecta el mismo documento repetido dentro del mismo lado', () => {
    const actors = [
      actor({ rol: 'comprador', tipoDocumento: 'CC', numeroDocumento: '111' }),
      actor({ rol: 'comprador', tipoDocumento: 'CC', numeroDocumento: '111' }),
    ];
    expect(duplicateDocumentIndicesWithinSide(actors)).toEqual([1]);
  });

  it('NO marca duplicado entre lados distintos (relajación §4.4 Nivel 2 vive en otro validador)', () => {
    const actors = [
      actor({ rol: 'vendedor', tipoDocumento: 'CC', numeroDocumento: '111' }),
      actor({ rol: 'comprador', tipoDocumento: 'CC', numeroDocumento: '111' }),
    ];
    expect(duplicateDocumentIndicesWithinSide(actors)).toEqual([]);
  });

  it('documentos vacíos no cuentan como duplicados', () => {
    const actors = [
      actor({ rol: 'comprador', numeroDocumento: '' }),
      actor({ rol: 'comprador', numeroDocumento: '' }),
    ];
    expect(duplicateDocumentIndicesWithinSide(actors)).toEqual([]);
  });
});

describe('computeOrdinals / withOwnershipFields', () => {
  it('asigna ordinal 1-based dentro de cada lado', () => {
    const actors = [
      actor({ rol: 'vendedor' }),
      actor({ rol: 'comprador' }),
      actor({ rol: 'comprador' }),
      actor({ rol: 'comprador' }),
    ];
    expect(computeOrdinals(actors)).toEqual([1, 1, 2, 3]);
  });

  it('un solo actor por lado envía porcentaje null (comportamiento previo sin cambios)', () => {
    const actors = [actor({ rol: 'comprador', porcentaje: 100 })];
    const [out] = withOwnershipFields(actors);
    expect(out.ordinal).toBe(1);
    expect(out.porcentaje).toBeNull();
  });

  it('2+ actores por lado envían porcentaje redondeado a 2 decimales', () => {
    const actors = [
      actor({ rol: 'comprador', porcentaje: 33.333 }),
      actor({ rol: 'comprador', porcentaje: 66.667 }),
    ];
    const out = withOwnershipFields(actors);
    expect(out[0]).toMatchObject({ ordinal: 1, porcentaje: 33.33 });
    expect(out[1]).toMatchObject({ ordinal: 2, porcentaje: 66.67 });
  });
});

describe('defaultPercentageForNewActor', () => {
  it('reparte equitativamente entre el total de actores tras agregar', () => {
    expect(defaultPercentageForNewActor(2)).toBe(50);
    expect(defaultPercentageForNewActor(4)).toBe(25);
  });
});

describe('MAX_OWNERS_PER_SIDE', () => {
  it('el máximo por lado es 4 (ADR-0053)', () => {
    expect(MAX_OWNERS_PER_SIDE).toBe(4);
  });
});

// Riesgo identificado en el encargo: "estado fantasma" — una consulta RUNT de un actor eliminado
// (o desplazado) queda reasociada al actor equivocado si los mapas `Record<number, X>` no se
// reindexan en el mismo gesto que la inserción/eliminación del array `actors`.
describe('shiftIndexMapOnInsert', () => {
  it('desplaza +1 las claves >= al punto de inserción, deja intactas las anteriores', () => {
    const map = { 0: 'a', 1: 'b', 2: 'c' };
    expect(shiftIndexMapOnInsert(map, 1)).toEqual({ 0: 'a', 2: 'b', 3: 'c' });
  });

  it('insertar al final no desplaza nada', () => {
    const map = { 0: 'a', 1: 'b' };
    expect(shiftIndexMapOnInsert(map, 2)).toEqual({ 0: 'a', 1: 'b' });
  });

  it('mapa vacío no falla', () => {
    expect(shiftIndexMapOnInsert({}, 0)).toEqual({});
  });

  it('escenario de agregar un copropietario: la consulta del actor 2 (comprador) no se mezcla con la del 1 (vendedor recién insertado)', () => {
    // actors = [vendedor#0]; se agrega vendedor#1 (2do del lado vendedor) ANTES del comprador#1.
    // runt[1] pertenecía al comprador (posición 1); tras insertar en la posición 1, el comprador
    // pasa a la posición 2 y su consulta RUNT debe seguirlo, no quedarse fantasma en el índice 1.
    const runt = { 0: 'vendedor-runt', 1: 'comprador-runt' };
    const next = shiftIndexMapOnInsert(runt, 1);
    expect(next).toEqual({ 0: 'vendedor-runt', 2: 'comprador-runt' });
  });
});

describe('shiftIndexMapOnRemove', () => {
  it('elimina la clave del índice quitado y desplaza -1 las posteriores', () => {
    const map = { 0: 'a', 1: 'b', 2: 'c' };
    expect(shiftIndexMapOnRemove(map, 1)).toEqual({ 0: 'a', 1: 'c' });
  });

  it('eliminar el último no desplaza nada', () => {
    const map = { 0: 'a', 1: 'b' };
    expect(shiftIndexMapOnRemove(map, 1)).toEqual({ 0: 'a' });
  });

  it('ida y vuelta: insertar y luego eliminar el mismo índice deja el mapa como estaba', () => {
    const map = { 0: 'a', 1: 'b', 2: 'c' };
    const inserted = shiftIndexMapOnInsert(map, 1);
    const removed = shiftIndexMapOnRemove(inserted, 1);
    expect(removed).toEqual(map);
  });

  it('escenario de eliminar un copropietario: la consulta del actor que queda no hereda la del eliminado', () => {
    // actors = [comprador#0 (ordinal 1), comprador#1 (ordinal 2, se elimina), comprador#2 (ordinal 3)]
    // runt[2] es la consulta del ordinal 3; al eliminar el índice 1 debe pasar a ocupar el índice 1,
    // NUNCA fusionarse ni perderse con la del ordinal 1 (índice 0, que no se toca).
    const runt = { 0: 'ordinal-1-found', 1: 'ordinal-2-found', 2: 'ordinal-3-found' };
    const next = shiftIndexMapOnRemove(runt, 1);
    expect(next).toEqual({ 0: 'ordinal-1-found', 1: 'ordinal-3-found' });
  });
});

describe('actorsOrderedByOrdinal', () => {
  it('ordena por ordinal aunque la respuesta llegue desordenada', () => {
    const items = [{ id: 'b', ordinal: 2 }, { id: 'a', ordinal: 1 }, { id: 'c', ordinal: 3 }];
    expect(actorsOrderedByOrdinal(items).map((x) => x.item.id)).toEqual(['a', 'b', 'c']);
  });

  it('ordinal ausente se trata como 1 (compatibilidad con actores previos a ADR-0053)', () => {
    const items = [{ id: 'sin-ordinal' }, { id: 'con-ordinal-2', ordinal: 2 }];
    const result = actorsOrderedByOrdinal(items);
    expect(result[0].item.id).toBe('sin-ordinal');
    expect(result[0].ordinal).toBe(1);
    expect(result[1].item.id).toBe('con-ordinal-2');
  });

  it('lista vacía no falla', () => {
    expect(actorsOrderedByOrdinal([])).toEqual([]);
  });
});

describe('identityStatusForActor', () => {
  const comprador1 = { rol: 'comprador' as const, numeroDocumento: '111', personType: 'natural' as const };
  const comprador2Juridico = {
    rol: 'comprador' as const,
    numeroDocumento: '900222333',
    personType: 'juridical' as const,
  };

  it('usa la validación biométrica propia, correlacionada por documento', () => {
    const biometric = [
      { documentNumber: '111', partyRole: 'comprador' as const, status: 'aprobado' as const },
    ];
    expect(identityStatusForActor(comprador1, biometric)).toEqual({
      label: 'Identidad aprobada',
      tone: 'success',
    });
  });

  it('no confunde la validación de OTRO copropietario del mismo lado (mismo rol, distinto documento)', () => {
    const biometric = [
      { documentNumber: '999-de-otro-actor', partyRole: 'comprador' as const, status: 'aprobado' as const },
    ];
    expect(identityStatusForActor(comprador1, biometric).label).toBe('Pendiente');
  });

  it('mapea rechazado/vencido/en_proceso con tonos distintos (para que "a quién le falta" se note)', () => {
    expect(
      identityStatusForActor(comprador1, [
        { documentNumber: '111', partyRole: 'comprador', status: 'rechazado' },
      ]),
    ).toEqual({ label: 'Identidad rechazada', tone: 'danger' });
    expect(
      identityStatusForActor(comprador1, [
        { documentNumber: '111', partyRole: 'comprador', status: 'expirado' },
      ]),
    ).toEqual({ label: 'Identidad vencida', tone: 'danger' });
    expect(
      identityStatusForActor(comprador1, [
        { documentNumber: '111', partyRole: 'comprador', status: 'en_proceso' },
      ]),
    ).toEqual({ label: 'Validación en proceso', tone: 'info' });
  });

  it('sin validación propia, persona jurídica cubierta por el baúl del lado cae a "Firma del baúl"', () => {
    expect(identityStatusForActor(comprador2Juridico, [], ['comprador'])).toEqual({
      label: 'Firma del baúl',
      tone: 'info',
    });
  });

  it('persona natural NO usa la aproximación del baúl (esa cobertura es solo para el RL de jurídicas)', () => {
    expect(identityStatusForActor(comprador1, [], ['comprador']).label).toBe('Pendiente');
  });

  it('sin nada: "Pendiente" — nunca un falso aprobado por defecto', () => {
    expect(identityStatusForActor(comprador1, [], []).tone).toBe('warning');
  });

  it('partyRole null (matrícula, actor único histórico) también correlaciona por documento', () => {
    const biometric = [{ documentNumber: '111', partyRole: null, status: 'aprobado' as const }];
    expect(identityStatusForActor(comprador1, biometric).label).toBe('Identidad aprobada');
  });
});
