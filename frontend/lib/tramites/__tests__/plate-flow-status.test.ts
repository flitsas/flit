// Sub-estado interno de la ruta de placa (Feature #10587 / HU #10785) — helpers de badge secundario.
// Uso de ejemplo: plateFlowLabel('preasignado') → 'En cola del OT'
import { describe, expect, it } from 'vitest';
import {
  ESTADOS_TRAMITE,
  esPlateFlowStatus,
  plateFlowChipStyle,
  plateFlowLabel,
  PLATE_FLOW_LABELS,
} from '../estados';

describe('plate-flow status (HU #10785)', () => {
  // AC1 — preasignado/asignado dejan de ser estados de trámite.
  it('preasignado/asignado NO son estados del trámite', () => {
    expect(ESTADOS_TRAMITE).not.toContain('preasignado' as never);
    expect(ESTADOS_TRAMITE).not.toContain('asignado' as never);
    expect(ESTADOS_TRAMITE).toHaveLength(6);
  });

  // AC1/AC2 — vocabulario del sub-estado.
  it('reconoce los sub-estados de placa válidos', () => {
    expect(esPlateFlowStatus('preasignado')).toBe(true);
    expect(esPlateFlowStatus('asignado')).toBe(true);
    expect(esPlateFlowStatus('entregado')).toBe(false);
    expect(esPlateFlowStatus(null)).toBe(false);
    expect(esPlateFlowStatus(undefined)).toBe(false);
    expect(esPlateFlowStatus('')).toBe(false);
  });

  // AC2 — badge secundario con etiquetas de progreso de placa (no de estado del trámite).
  it('devuelve label del badge para un sub-estado y null en otro caso', () => {
    expect(plateFlowLabel('preasignado')).toBe(PLATE_FLOW_LABELS.preasignado);
    expect(plateFlowLabel('asignado')).toBe(PLATE_FLOW_LABELS.asignado);
    expect(PLATE_FLOW_LABELS.preasignado).toBe('En cola del OT');
    expect(PLATE_FLOW_LABELS.asignado).toBe('Con placa');
  });

  // AC3 — sin sub-estado (null / desconocido) no se pinta badge.
  it('no devuelve estilo de badge cuando no hay ruta de placa', () => {
    expect(plateFlowChipStyle(null)).toBeNull();
    expect(plateFlowChipStyle(undefined)).toBeNull();
    expect(plateFlowChipStyle('entregado')).toBeNull();
    expect(plateFlowLabel(null)).toBeNull();
  });

  // Contrato — colores del prototipo conservados (cian preasignado / índigo asignado).
  it('conserva los colores del prototipo por sub-estado', () => {
    expect(plateFlowChipStyle('preasignado')?.color).toBe('#0e7490');
    expect(plateFlowChipStyle('asignado')?.color).toBe('#4f46e5');
  });
});
