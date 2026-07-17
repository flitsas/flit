// Sub-estado interno de la ruta de placa (Feature #10587 / HU #10785) — helpers de badge secundario.
// Uso de ejemplo: plateFlowLabel('preasignado') → 'En cola del OT'
import { describe, expect, it } from 'vitest';
import {
  ESTADOS_TRAMITE,
  esperandoSoatDelGestor,
  esPlateFlowStatus,
  plateFlowChipStyle,
  plateFlowLabel,
  puedeDecidirOt,
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

// HU #10804 — el OT solo decide (Aprobar/Rechazar) en ruta estándar o con placa asignada + SOAT vigente.
// Uso de ejemplo: puedeDecidirOt('asignado', 'vigente') → true
describe('puedeDecidirOt (HU #10804)', () => {
  // AC4 — ruta estándar (sin sub-estado de placa): siempre puede decidir.
  it('permite decidir en ruta estándar (plate_flow_status null/desconocido)', () => {
    expect(puedeDecidirOt(null, null)).toBe(true);
    expect(puedeDecidirOt(undefined, undefined)).toBe(true);
    expect(puedeDecidirOt('entregado', null)).toBe(true); // valor no-plateflow ⇒ estándar
  });

  // AC1 — placa asignada con SOAT vigente: puede decidir.
  it('permite decidir con placa asignada y SOAT vigente', () => {
    expect(puedeDecidirOt('asignado', 'vigente')).toBe(true);
  });

  // AC2 — preasignado: nunca puede decidir (falta asignar placa).
  it('bloquea la decisión en preasignado sin importar el SOAT', () => {
    expect(puedeDecidirOt('preasignado', null)).toBe(false);
    expect(puedeDecidirOt('preasignado', 'vigente')).toBe(false);
  });

  // AC3 — asignado sin SOAT vigente (null/vencido/unknown): bloquea la decisión.
  it('bloquea la decisión en asignado cuando el SOAT no está vigente', () => {
    expect(puedeDecidirOt('asignado', null)).toBe(false);
    expect(puedeDecidirOt('asignado', 'vencido')).toBe(false);
    expect(puedeDecidirOt('asignado', 'unknown')).toBe(false);
  });
});

// HU #10804 — aviso "Esperando validación de SOAT del gestor" solo en asignado sin SOAT vigente.
describe('esperandoSoatDelGestor (HU #10804)', () => {
  it('es true en asignado sin SOAT vigente', () => {
    expect(esperandoSoatDelGestor('asignado', null)).toBe(true);
    expect(esperandoSoatDelGestor('asignado', 'vencido')).toBe(true);
  });
  it('es false en asignado con SOAT vigente', () => {
    expect(esperandoSoatDelGestor('asignado', 'vigente')).toBe(false);
  });
  it('es false fuera de asignado (preasignado o estándar)', () => {
    expect(esperandoSoatDelGestor('preasignado', null)).toBe(false);
    expect(esperandoSoatDelGestor(null, null)).toBe(false);
  });
});
