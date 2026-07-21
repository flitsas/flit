import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PlateRequestSection } from '../PlateRequestSection';

// FEATURE-08 / HU-FE-04 (CFD-08) — PlateRequestSection: 4 estados de UI.

describe('PlateRequestSection', () => {
  it('estado none: muestra botón Solicitar placa y dispara onRequest (AC-01)', async () => {
    const user = userEvent.setup();
    const onRequest = vi.fn();
    render(<PlateRequestSection status="none" onRequest={onRequest} />);

    const btn = screen.getByRole('button', { name: /solicitar placa/i });
    expect(btn).toBeEnabled();
    await user.click(btn);
    expect(onRequest).toHaveBeenCalledTimes(1);
  });

  it('estado requesting: botón deshabilitado con "Solicitando…" (AC-02)', () => {
    render(<PlateRequestSection status="requesting" />);
    expect(screen.getByRole('button', { name: /solicitar placa/i })).toBeDisabled();
    expect(screen.getByText(/solicitando/i)).toBeInTheDocument();
  });

  it('estado pending: muestra solicitud en trámite (AC-02)', () => {
    render(<PlateRequestSection status="pending" />);
    expect(screen.getByRole('status')).toHaveTextContent(/en trámite/i);
  });

  it('estado completed: muestra la placa asignada (AC-02)', () => {
    render(<PlateRequestSection status="completed" assignedPlate="ABC123" />);
    expect(screen.getByText(/placa asignada: ABC123/i)).toBeInTheDocument();
  });
});
