import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BusquedaRapidaAcordeon } from '../BusquedaRapidaAcordeon';

// Epic #12686 — HU #12806 / #12807.
const ITEMS = [
  { key: 'a', label: 'Más de 5 días en gestión', hint: 'Entregados viejos' },
  { key: 'b', label: 'Sin documento', hint: 'Borradores incompletos' },
];

afterEach(() => {
  window.localStorage.clear();
  vi.restoreAllMocks();
});

describe('BusquedaRapidaAcordeon', () => {
  it('AC1 — despliega los atajos sin conteo', () => {
    render(<BusquedaRapidaAcordeon items={ITEMS} selected="" onSelect={vi.fn()} storageKey="t1" />);

    expect(screen.getByRole('button', { name: 'Búsqueda rápida' })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: 'Más de 5 días en gestión' })).toHaveTextContent(/^Más de 5 días en gestión$/);
    expect(screen.getByRole('button', { name: 'Sin documento' })).toBeInTheDocument();
  });

  it('AC2/AC3 — elegir un atajo lo marca; segundo clic lo quita', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    const { rerender } = render(
      <BusquedaRapidaAcordeon items={ITEMS} selected="" onSelect={onSelect} storageKey="t2" />,
    );

    await user.click(screen.getByRole('button', { name: 'Sin documento' }));
    expect(onSelect).toHaveBeenCalledWith('b');

    rerender(<BusquedaRapidaAcordeon items={ITEMS} selected="b" onSelect={onSelect} storageKey="t2" />);
    expect(screen.getByRole('button', { name: 'Sin documento' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'Sin documento' }));
    expect(onSelect).toHaveBeenLastCalledWith('');
  });

  it('AC8 — recuerda que el usuario lo cerró', async () => {
    const user = userEvent.setup();
    const { unmount } = render(
      <BusquedaRapidaAcordeon items={ITEMS} selected="" onSelect={vi.fn()} storageKey="t3" />,
    );

    await user.click(screen.getByRole('button', { name: 'Búsqueda rápida' }));
    expect(screen.getByRole('button', { name: 'Búsqueda rápida' })).toHaveAttribute('aria-expanded', 'false');
    unmount();

    render(<BusquedaRapidaAcordeon items={ITEMS} selected="" onSelect={vi.fn()} storageKey="t3" />);
    expect(screen.getByRole('button', { name: 'Búsqueda rápida' })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('button', { name: 'Sin documento' })).not.toBeInTheDocument();
  });

  it('sin almacenamiento disponible sigue abriendo y cerrando', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('bloqueado');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('bloqueado');
    });
    const user = userEvent.setup();
    render(<BusquedaRapidaAcordeon items={ITEMS} selected="" onSelect={vi.fn()} storageKey="t4" />);

    const cabecera = screen.getByRole('button', { name: 'Búsqueda rápida' });
    expect(cabecera).toHaveAttribute('aria-expanded', 'true');
    await user.click(cabecera);
    expect(cabecera).toHaveAttribute('aria-expanded', 'false');
  });
});
