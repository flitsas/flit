import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { PreflightPanel } from '@/components/operacion/PreflightPanel';
import { WizardReadOnlyProvider } from '@/components/operacion/WizardReadOnlyContext';
import type { PreflightSnapshot } from '@/lib/api/types/procedure-runtime';

// HU #10539 — Aviso de VIN matriculado y acceso a traspaso.
// El backend (HU #10538) agrega en el preflight de matrícula el check `vin_matricula`
// (warn, con secretaría + fecha del registro previo) cuando el VIN ya tiene matrícula.
// El PreflightPanel lo detecta y ofrece iniciar el traspaso del vehículo.
//
// Uso de ejemplo:
//   <PreflightPanel snapshot={snapshotConVinMatricula} onIniciarTraspaso={fn} showRunButton={false} ... />
//   → renderiza "Este VIN ya está matriculado" + botón "Iniciar traspaso de este vehículo".

const VIN_MATRICULA_MSG =
  'Este VIN ya tiene una matrícula registrada en Secretaría de Bogotá el 2025-03-10. ' +
  'Puede iniciar un traspaso de este vehículo en lugar de una nueva matrícula.';

/** Snapshot de preflight de matrícula, con o sin el check de conflicto `vin_matricula`. */
function snapshot(withConflict: boolean): PreflightSnapshot {
  return {
    overall: 'yellow',
    checks: [
      { key: 'soat', label: 'SOAT', status: 'ok', source: 'verifik', message: 'Vigente' },
      ...(withConflict
        ? [
            {
              key: 'vin_matricula',
              label: 'VIN ya matriculado',
              status: 'warn' as const,
              source: 'system',
              message: VIN_MATRICULA_MSG,
            },
          ]
        : []),
    ],
    createdAt: '2026-07-06T00:00:00Z',
  };
}

const baseProps = {
  loading: false,
  onRun: () => {},
  riesgoAceptado: false,
  onToggleRiesgo: () => {},
  // El disparo de la consulta vive fuera del panel en el paso de consulta (matrícula).
  showRunButton: false,
};

describe('HU #10539 — Aviso de VIN matriculado y CTA de traspaso (PreflightPanel)', () => {
  // AC1 — happy path: hay conflicto → aviso con la matrícula previa + botón de traspaso.
  it('AC1: con el check vin_matricula muestra la info de la matrícula previa y el botón de traspaso', () => {
    render(
      <PreflightPanel {...baseProps} snapshot={snapshot(true)} onIniciarTraspaso={() => {}} />,
    );
    expect(screen.getByText(/Este VIN ya está matriculado/i)).toBeInTheDocument();
    // El mensaje del check trae secretaría + fecha del registro previo.
    expect(screen.getByText(/matrícula registrada en Secretaría de Bogotá el 2025-03-10/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Iniciar traspaso de este vehículo/i }),
    ).toBeInTheDocument();
  });

  // AC1 — edge case: sin el check no se ofrece nada (matrícula limpia).
  it('AC1 (edge): sin el check vin_matricula NO se muestra el aviso ni el botón', () => {
    render(
      <PreflightPanel {...baseProps} snapshot={snapshot(false)} onIniciarTraspaso={() => {}} />,
    );
    expect(screen.queryByText(/Este VIN ya está matriculado/i)).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Iniciar traspaso/i }),
    ).not.toBeInTheDocument();
  });

  // AC1 — contrato: en solo lectura (trámite fuera de borrador) se ve el aviso pero no la acción.
  it('AC1 (contrato): en solo lectura se ve el aviso pero NO el botón de acción', () => {
    render(
      <WizardReadOnlyProvider readOnly={true}>
        <PreflightPanel {...baseProps} snapshot={snapshot(true)} onIniciarTraspaso={() => {}} />
      </WizardReadOnlyProvider>,
    );
    expect(screen.getByText(/Este VIN ya está matriculado/i)).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Iniciar traspaso/i }),
    ).not.toBeInTheDocument();
  });

  // AC2 — happy path: pulsar el botón dispara la navegación al traspaso (callback del wizard).
  it('AC2: pulsar "Iniciar traspaso de este vehículo" invoca onIniciarTraspaso', async () => {
    const onIniciar = vi.fn();
    render(
      <PreflightPanel {...baseProps} snapshot={snapshot(true)} onIniciarTraspaso={onIniciar} />,
    );
    await userEvent.click(
      screen.getByRole('button', { name: /Iniciar traspaso de este vehículo/i }),
    );
    expect(onIniciar).toHaveBeenCalledTimes(1);
  });

  // AC2 — contrato: sin handler (p. ej. en traspaso, donde no aplica) el botón no se ofrece
  // aunque el check estuviera presente. El panel es presentacional; el wizard inyecta la acción.
  it('AC2 (contrato): sin onIniciarTraspaso no se renderiza el botón de traspaso', () => {
    render(<PreflightPanel {...baseProps} snapshot={snapshot(true)} />);
    expect(
      screen.queryByRole('button', { name: /Iniciar traspaso/i }),
    ).not.toBeInTheDocument();
  });
});
