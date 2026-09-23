import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { EstadoFunnel } from '../EstadoFunnel';

const counts = {
  borrador: 5,
  preparado: 2,
  entregado: 3,
  aprobado: 7,
  rechazado: 1,
  anulado: 0,
  subsanacion: 4,
  preasignacion: 6,
  asignado: 2,
  revocado: 0,
  rechazado_preasignacion: 1,
};

describe('EstadoFunnel', () => {
  it('renderiza una celda por estado con su conteo y nombre accesible', () => {
    render(<EstadoFunnel counts={counts} />);
    expect(screen.getByLabelText('Borrador: 5 trámites')).toBeInTheDocument();
    expect(screen.getByLabelText('Aprobado: 7 trámites')).toBeInTheDocument();
    // ADR-0059 (HU #12601 AC3) — la ruta de placa, Revocado y «Rechazado preasignación» tienen tarjeta.
    for (const label of [
      'Borrador',
      'Preparado',
      'Preasignación',
      'Asignado',
      'Entregado',
      'Aprobado',
      'En subsanación',
      'Rechazado',
      'Rechazado preasignación',
      'Revocado',
      'Anulado',
    ]) {
      expect(screen.getByLabelText(new RegExp(`^${label}:`))).toBeInTheDocument();
    }
    expect(screen.getByLabelText('En subsanación: 4 trámites')).toBeInTheDocument();
    expect(screen.getByLabelText('Preasignación: 6 trámites')).toBeInTheDocument();
    expect(screen.getByLabelText('Rechazado preasignación: 1 trámite')).toBeInTheDocument();
    expect(screen.getAllByRole('button')).toHaveLength(11);
  });

  it('«Rechazado preasignación» filtra con el pseudo-estado y sin conteo pinta cero', async () => {
    const onSelect = vi.fn();
    const user = userEvent.setup();
    const sinPseudo = { ...counts, rechazado_preasignacion: undefined };
    render(<EstadoFunnel counts={sinPseudo} selected="" onSelect={onSelect} />);

    await user.click(screen.getByRole('button', { name: 'Rechazado preasignación: 0 trámites' }));
    expect(onSelect).toHaveBeenCalledWith('rechazado_preasignacion');
  });

  it('singulariza el nombre accesible cuando hay un solo trámite', () => {
    render(<EstadoFunnel counts={counts} />);
    expect(screen.getByLabelText('Rechazado: 1 trámite')).toBeInTheDocument();
  });

  it('filtra al clic y quita el filtro al repetir el mismo estado', async () => {
    const onSelect = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(
      <EstadoFunnel counts={counts} selected="" onSelect={onSelect} />,
    );

    await user.click(screen.getByRole('button', { name: 'Borrador: 5 trámites' }));
    expect(onSelect).toHaveBeenCalledWith('borrador');

    rerender(<EstadoFunnel counts={counts} selected="borrador" onSelect={onSelect} />);
    expect(screen.getByRole('button', { name: 'Borrador: 5 trámites' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );

    await user.click(screen.getByRole('button', { name: 'Borrador: 5 trámites' }));
    expect(onSelect).toHaveBeenCalledWith('');
  });

  it('Epic #12686 — con `estados` pinta solo esas tarjetas, en ese orden', () => {
    render(
      <EstadoFunnel counts={counts} estados={['borrador', 'entregado', 'aprobado', 'anulado']} />,
    );
    const nombres = screen.getAllByRole('button').map((b) => b.getAttribute('aria-label'));
    expect(nombres).toEqual([
      'Borrador: 5 trámites',
      'Entregado: 3 trámites',
      'Aprobado: 7 trámites',
      'Anulado: 0 trámites',
    ]);
    expect(screen.queryByLabelText(/^Preparado:/)).not.toBeInTheDocument();
    expect(screen.getByRole('group')).toHaveClass('xl:grid-cols-4');
  });

  it('Epic #12686 — una columna por tarjeta en pantalla ancha', () => {
    render(
      <EstadoFunnel
        counts={counts}
        estados={['borrador', 'preasignacion', 'asignado', 'entregado', 'aprobado', 'rechazado', 'revocado', 'anulado']}
      />,
    );
    expect(screen.getByRole('group')).toHaveClass('xl:grid-cols-8');
  });

  it('Epic #12686 — «Rechazado desde preasignación» sin tarjeta propia resalta Rechazado', async () => {
    const onSelect = vi.fn();
    const user = userEvent.setup();
    render(
      <EstadoFunnel
        counts={counts}
        estados={['borrador', 'rechazado', 'anulado']}
        selected="rechazado_preasignacion"
        onSelect={onSelect}
      />,
    );

    const rechazado = screen.getByRole('button', { name: 'Rechazado: 1 trámite' });
    expect(rechazado).toHaveAttribute('aria-pressed', 'true');
    await user.click(rechazado);
    expect(onSelect).toHaveBeenCalledWith('');
  });
});
