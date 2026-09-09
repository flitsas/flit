import { describe, expect, it } from 'vitest';
import { tramiteLabel } from '../tramites-row-labels';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12181 — la fila del listado nombra el TIPO, no su familia.
 *
 * El caso que motiva la HU no es solo «Otros»: `TRASPASO_STANDARD` y `TRASPASO_UNILATERAL` son
 * trámites distintos —en el unilateral el comprador ni siquiera comparece— y los dos se leían
 * «Traspaso». La familia no identifica en ninguna de las tres.
 */
function fila(parcial: Partial<InstanceSummary>): InstanceSummary {
  return { modalidad: 'TRASPASO', ...parcial } as InstanceSummary;
}

describe('tramiteLabel', () => {
  it('nombra el tipo específico dentro de OTROS', () => {
    expect(tramiteLabel(fila({ modalidad: 'OTROS', tipoNombre: 'Levantamiento de prenda' })))
      .toBe('Levantamiento de prenda');
  });

  it('distingue dos tipos de la misma familia que antes se leían igual', () => {
    expect(tramiteLabel(fila({ modalidad: 'TRASPASO', tipoNombre: 'Traspaso' }))).toBe('Traspaso');
    expect(tramiteLabel(fila({ modalidad: 'TRASPASO', tipoNombre: 'Traspaso unilateral' })))
      .toBe('Traspaso unilateral');
  });

  it('nombra el tipo también en matrículas', () => {
    expect(tramiteLabel(fila({ modalidad: 'MATRICULAS', tipoNombre: 'Matrícula inicial' })))
      .toBe('Matrícula inicial');
  });

  it('recorta el espacio sobrante del nombre', () => {
    expect(tramiteLabel(fila({ tipoNombre: '  Blindaje  ' }))).toBe('Blindaje');
  });

  it.each([
    ['MATRICULAS', 'Matrícula'],
    ['TRASPASO', 'Traspaso'],
    ['OTROS', 'Otros'],
  ] as const)('sin tipo parametrizado cae a la familia (%s)', (modalidad, esperado) => {
    // Un backend anterior al campo, o un tipo sin nombre: la celda no puede quedar vacía.
    expect(tramiteLabel(fila({ modalidad, tipoNombre: null }))).toBe(esperado);
    expect(tramiteLabel(fila({ modalidad, tipoNombre: '   ' }))).toBe(esperado);
  });

  it('una fila sin familia reconocible se sigue pintando', () => {
    expect(tramiteLabel(fila({ modalidad: 'DESCONOCIDA' as never, tipoNombre: null }))).toBe('—');
  });
});
