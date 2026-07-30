// Sub-estado interno de la ruta de placa — helpers de badge secundario.
import { describe, expect, it } from 'vitest';
import {
  ESTADOS_TRAMITE,
  esperandoProcesoDelGestor,
  esperandoSoatDelGestor,
  esPlateFlowStatus,
  plateFlowChipStyle,
  plateFlowLabel,
  puedeDecidirOt,
  PLATE_FLOW_LABELS,
} from '../estados';

describe('plate-flow status', () => {
  it('preasignado/asignado/terminado NO son estados del trámite', () => {
    expect(ESTADOS_TRAMITE).not.toContain('preasignado' as never);
    expect(ESTADOS_TRAMITE).not.toContain('asignado' as never);
    expect(ESTADOS_TRAMITE).not.toContain('terminado' as never);
    expect(ESTADOS_TRAMITE).toHaveLength(7);
  });

  it('reconoce los sub-estados de placa válidos', () => {
    expect(esPlateFlowStatus('preasignado')).toBe(true);
    expect(esPlateFlowStatus('asignado')).toBe(true);
    expect(esPlateFlowStatus('terminado')).toBe(true);
    expect(esPlateFlowStatus('entregado')).toBe(false);
    expect(esPlateFlowStatus(null)).toBe(false);
  });

  it('devuelve labels UI Sin asignar / Asignado / Terminado', () => {
    expect(plateFlowLabel('preasignado')).toBe('Sin asignar');
    expect(plateFlowLabel('asignado')).toBe('Asignado');
    expect(plateFlowLabel('terminado')).toBe('Terminado');
    expect(PLATE_FLOW_LABELS.preasignado).toBe('Sin asignar');
  });

  it('no devuelve estilo de badge cuando no hay ruta de placa', () => {
    expect(plateFlowChipStyle(null)).toBeNull();
    expect(plateFlowLabel(null)).toBeNull();
  });

  it('conserva colores por sub-estado', () => {
    expect(plateFlowChipStyle('preasignado')?.color).toBe('#0e7490');
    expect(plateFlowChipStyle('asignado')?.color).toBe('#4f46e5');
    expect(plateFlowChipStyle('terminado')?.color).toBe('#047857');
  });
});

describe('puedeDecidirOt', () => {
  it('permite decidir en ruta estándar', () => {
    expect(puedeDecidirOt(null)).toBe(true);
    expect(puedeDecidirOt('entregado')).toBe(true);
  });

  it('permite decidir solo en terminado (ruta de placa)', () => {
    expect(puedeDecidirOt('terminado')).toBe(true);
    expect(puedeDecidirOt('asignado', 'vigente')).toBe(false);
    expect(puedeDecidirOt('preasignado')).toBe(false);
  });
});

describe('esperandoProcesoDelGestor', () => {
  it('es true solo en asignado', () => {
    expect(esperandoProcesoDelGestor('asignado')).toBe(true);
    expect(esperandoProcesoDelGestor('terminado')).toBe(false);
    expect(esperandoProcesoDelGestor('preasignado')).toBe(false);
    expect(esperandoProcesoDelGestor(null)).toBe(false);
  });

  it('esperandoSoatDelGestor (compat) delega en esperandoProcesoDelGestor', () => {
    expect(esperandoSoatDelGestor('asignado', 'vigente')).toBe(true);
    expect(esperandoSoatDelGestor('terminado', null)).toBe(false);
  });
});
