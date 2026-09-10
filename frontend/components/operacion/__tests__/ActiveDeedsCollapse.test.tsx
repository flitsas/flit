import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  fetchActiveDeeds: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    fetchActiveDeeds: mocks.fetchActiveDeeds,
  },
}));

import {
  ActiveDeedsCollapse,
  deedVigenciaTone,
  deedVigenciaLabel,
} from '@/components/operacion/ActiveDeedsCollapse';
import type { ActiveDeed } from '@/lib/api/types/procedure-runtime';

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchActiveDeeds.mockResolvedValue([]);
});

describe('deedVigenciaTone / deedVigenciaLabel — umbrales', () => {
  it('mapea días restantes al tono por umbral (≤7 danger, ≤30 warning, resto success)', () => {
    expect(deedVigenciaTone(0)).toBe('danger');
    expect(deedVigenciaTone(7)).toBe('danger');
    expect(deedVigenciaTone(8)).toBe('warning');
    expect(deedVigenciaTone(30)).toBe('warning');
    expect(deedVigenciaTone(31)).toBe('success');
    expect(deedVigenciaTone(365)).toBe('success');
  });

  it('etiqueta días restantes en singular/plural y vencimiento inmediato', () => {
    expect(deedVigenciaLabel(0)).toBe('Vence hoy');
    expect(deedVigenciaLabel(-3)).toBe('Vence hoy');
    expect(deedVigenciaLabel(1)).toBe('1 día');
    expect(deedVigenciaLabel(45)).toBe('45 días');
  });
});

describe('ActiveDeedsCollapse — collapse contraído + carga perezosa', () => {
  it('arranca contraído y NO consulta hasta que se abre (lazy load)', async () => {
    render(<ActiveDeedsCollapse />);

    const toggle = screen.getByRole('button', {
      name: /Escrituras vigentes de la compañía/i,
    });
    // Contraído por defecto: sin fetch y sin panel visible.
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(mocks.fetchActiveDeeds).not.toHaveBeenCalled();
    expect(screen.queryByLabelText('Escrituras vigentes')).toBeNull();

    await userEvent.setup().click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await waitFor(() => expect(mocks.fetchActiveDeeds).toHaveBeenCalledTimes(1));
  });

  it('lista NIT, nombre y badge de vigencia con el tono por umbral', async () => {
    const deeds: ActiveDeed[] = [
      { id: 'deed-1', nit: '900111222', name: 'Transportes Andinos SAS', diasRestantes: 120, vigenciaHasta: '2026-11-20' },
      { id: 'deed-2', nit: '901333444', name: 'Logística del Café SAS', diasRestantes: 5, vigenciaHasta: '2026-07-28' },
    ];
    mocks.fetchActiveDeeds.mockResolvedValue(deeds);

    render(<ActiveDeedsCollapse />);
    await userEvent.setup().click(
      screen.getByRole('button', { name: /Escrituras vigentes de la compañía/i }),
    );

    expect(await screen.findByText('Transportes Andinos SAS')).toBeInTheDocument();
    expect(screen.getByText('NIT 900111222')).toBeInTheDocument();
    expect(screen.getByText('Logística del Café SAS')).toBeInTheDocument();
    expect(screen.getAllByText('Sin RL vinculado')).toHaveLength(2);

    // Badges de vigencia: 120 días (verde/success) y 5 días (rojo/danger).
    const vigente = screen.getByLabelText('Vigencia: 120 días restantes');
    const porVencer = screen.getByLabelText('Vigencia: 5 días restantes');
    expect(vigente).toHaveTextContent('120 días');
    expect(porVencer).toHaveTextContent('5 días');
    expect(vigente).toHaveStyle({ color: 'var(--badge-success-fg)' });
    expect(porVencer).toHaveStyle({ color: 'var(--badge-danger-fg)' });
  });

  it('muestra el RL vinculado a la escritura (nombre y documento)', async () => {
    const deeds: ActiveDeed[] = [
      {
        id: 'deed-1',
        nit: '900111222',
        name: 'Transportes Andinos SAS',
        diasRestantes: 120,
        vigenciaHasta: '2026-11-20',
        representativeId: 'rep-1',
        representativeName: 'Juan Pérez Gómez',
        representativeDocumentType: 'CC',
        representativeDocumentNumber: '1038409485',
      },
    ];
    mocks.fetchActiveDeeds.mockResolvedValue(deeds);

    render(<ActiveDeedsCollapse />);
    await userEvent.setup().click(
      screen.getByRole('button', { name: /Escrituras vigentes de la compañía/i }),
    );

    expect(await screen.findByText(/RL: Juan Pérez Gómez · CC 1038409485/)).toBeInTheDocument();
    expect(screen.queryByText('Sin RL vinculado')).toBeNull();
  });

  it('lista VARIAS escrituras del MISMO NIT (una fila por escritura) con su descripción', async () => {
    // Feature #10929: dos escrituras vigentes de la misma compañía → dos filas, distinguidas por
    // la descripción; la llave por deed.id evita colisiones de key en React.
    const deeds: ActiveDeed[] = [
      {
        id: 'deed-a',
        nit: '900111222',
        name: 'Transportes Andinos SAS',
        diasRestantes: 40,
        vigenciaHasta: '2026-09-01',
        description: 'Escritura 1234 Notaría 5',
      },
      {
        id: 'deed-b',
        nit: '900111222',
        name: 'Transportes Andinos SAS',
        diasRestantes: 120,
        vigenciaHasta: '2026-11-20',
        description: 'Escritura 5678 Notaría 9',
      },
    ];
    mocks.fetchActiveDeeds.mockResolvedValue(deeds);

    render(<ActiveDeedsCollapse />);
    await userEvent.setup().click(
      screen.getByRole('button', { name: /Escrituras vigentes de la compañía/i }),
    );

    // Ambas escrituras del mismo NIT se renderizan (no se colapsan a una).
    expect(await screen.findByText('Escritura 1234 Notaría 5')).toBeInTheDocument();
    expect(screen.getByText('Escritura 5678 Notaría 9')).toBeInTheDocument();
    // El nombre de la compañía aparece una vez por fila.
    expect(screen.getAllByText('Transportes Andinos SAS')).toHaveLength(2);
    // Dos ítems de escritura en la lista.
    expect(screen.getByLabelText('Escrituras vigentes').querySelectorAll('li')).toHaveLength(2);
  });

  it('muestra estado vacío cuando no hay escrituras vigentes', async () => {
    mocks.fetchActiveDeeds.mockResolvedValue([]);
    render(<ActiveDeedsCollapse />);
    await userEvent.setup().click(
      screen.getByRole('button', { name: /Escrituras vigentes de la compañía/i }),
    );
    expect(
      await screen.findByText('La compañía no tiene escrituras vigentes.'),
    ).toBeInTheDocument();
  });

  it('no vuelve a consultar al alternar abierto/cerrado (una sola carga)', async () => {
    mocks.fetchActiveDeeds.mockResolvedValue([]);
    const user = userEvent.setup();
    render(<ActiveDeedsCollapse />);
    const toggle = screen.getByRole('button', {
      name: /Escrituras vigentes de la compañía/i,
    });

    await user.click(toggle); // abre → carga
    await waitFor(() => expect(mocks.fetchActiveDeeds).toHaveBeenCalledTimes(1));
    await user.click(toggle); // cierra
    await user.click(toggle); // reabre → NO recarga
    expect(mocks.fetchActiveDeeds).toHaveBeenCalledTimes(1);
  });
});
