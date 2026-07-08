// HU #10603 — Panel de consultas: RNMC condicionado por OT y diferenciado por actor (comprador/vendedor).
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PreflightPanel, checkRoleSuffix } from '../PreflightPanel';
import type {
  PreflightCheck,
  PreflightSnapshot,
} from '@/lib/api/types/procedure-runtime';

function snap(checks: PreflightCheck[]): PreflightSnapshot {
  return { overall: 'green', checks, createdAt: '2026-07-07T00:00:00Z' };
}

const rnmc = (key: string): PreflightCheck => ({
  key,
  label: 'Medidas correctivas (Policía)',
  status: 'ok',
  source: 'verifik_rnmc',
  message: '',
});

const baseProps = {
  loading: false,
  onRun: vi.fn(),
  riesgoAceptado: false,
  onToggleRiesgo: vi.fn(),
};

describe('checkRoleSuffix', () => {
  it('distingue comprador y vendedor por la clave RNMC', () => {
    expect(checkRoleSuffix('rnmc_comprador_medidas_correctivas')).toBe(' (comprador)');
    expect(checkRoleSuffix('rnmc_vendedor_medidas_correctivas')).toBe(' (vendedor)');
  });

  it('no agrega sufijo a checks que no son RNMC por actor', () => {
    expect(checkRoleSuffix('estado_vehiculo')).toBe('');
    expect(checkRoleSuffix('simit_comprador')).toBe('');
  });
});

describe('PreflightPanel — RNMC condicionado (HU #10603)', () => {
  it('muestra los dos checks RNMC diferenciados por rol', () => {
    render(
      <PreflightPanel
        snapshot={snap([
          rnmc('rnmc_comprador_medidas_correctivas'),
          rnmc('rnmc_vendedor_medidas_correctivas'),
        ])}
        {...baseProps}
      />,
    );
    expect(screen.getByText(/Medidas correctivas.*comprador/)).toBeInTheDocument();
    expect(screen.getByText(/Medidas correctivas.*vendedor/)).toBeInTheDocument();
  });

  it('sin RNMC exigido (OT no lo pide), el panel no muestra ningún check RNMC', () => {
    render(
      <PreflightPanel
        snapshot={snap([
          {
            key: 'estado_vehiculo',
            label: 'Estado del vehículo',
            status: 'ok',
            source: 'verifik',
            message: '',
          },
        ])}
        {...baseProps}
      />,
    );
    expect(screen.queryByText(/Medidas correctivas/)).not.toBeInTheDocument();
  });
});
