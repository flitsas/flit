import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ParametrizationHelp } from '../ParametrizationHelp';

// FEATURE-08 / HU-FE-04 (CFD-11) — ayuda contextual (acordeón, sin API).

describe('ParametrizationHelp', () => {
  it('muestra el acordeón con las secciones del configurador (AC-03)', () => {
    render(<ParametrizationHelp />);
    expect(screen.getByRole('button', { name: 'Entrada' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Fuentes' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Actores' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Documentos' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Solicitud de placa' })).toBeInTheDocument();
  });

  it('los paneles inician colapsados y se expanden al pulsar (AC-05 WCAG aria-expanded)', async () => {
    const user = userEvent.setup();
    render(<ParametrizationHelp />);

    const header = screen.getByRole('button', { name: 'Fuentes' });
    expect(header).toHaveAttribute('aria-expanded', 'false');

    await user.click(header);

    expect(header).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText(/RUNT, SIMIT/)).toBeInTheDocument();
  });

  it('un solo panel abierto a la vez (AC-06 estados de UI)', async () => {
    const user = userEvent.setup();
    render(<ParametrizationHelp />);

    await user.click(screen.getByRole('button', { name: 'Entrada' }));
    expect(screen.getByRole('button', { name: 'Entrada' })).toHaveAttribute('aria-expanded', 'true');

    await user.click(screen.getByRole('button', { name: 'Documentos' }));
    expect(screen.getByRole('button', { name: 'Entrada' })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('button', { name: 'Documentos' })).toHaveAttribute('aria-expanded', 'true');
  });
});
