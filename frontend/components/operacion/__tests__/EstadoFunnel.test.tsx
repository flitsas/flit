// Tira de KPIs por estado del listado de trámites: conteo por estado + filtro (toggle).
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { EstadoFunnel } from '../EstadoFunnel';

const counts = {
  borrador: 5,
  preparado: 2,
  entregado: 3,
  aprobado: 7,
  rechazado: 1,
  anulado: 0,
  subsanacion: 4,
};

// El nombre accesible incluye el conteo ("Borrador: 5 trámites") para que un lector de pantalla
// anuncie el dato sin depender del número que se ve al lado del icono.
const tarjeta = (label: string) => screen.getByRole('button', { name: new RegExp(`^${label}:`) });

describe('EstadoFunnel', () => {
  it('renderiza un botón por estado con su conteo y nombre accesible', () => {
    render(<EstadoFunnel counts={counts} active="" onSelect={vi.fn()} />);
    const borrador = tarjeta('Borrador');
    expect(borrador).toBeInTheDocument();
    expect(borrador.textContent).toContain('5');
    expect(tarjeta('Aprobado').textContent).toContain('7');
    // Los 7 estados de negocio. 'subsanacion' (HU #10870/#10874) ya tiene tarjeta propia: el
    // embudo anterior la omitía pese a estar en el vocabulario.
    for (const label of [
      'Borrador',
      'Preparado',
      'Entregado',
      'Aprobado',
      'En subsanación',
      'Rechazado',
      'Anulado',
    ]) {
      expect(tarjeta(label)).toBeInTheDocument();
    }
    expect(tarjeta('En subsanación').textContent).toContain('4');
    // La ruta de placa NO añade tarjetas: su progreso es un sub-estado interno bajo 'entregado'.
    expect(screen.queryByRole('button', { name: /^Preasignado:/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Asignado:/ })).toBeNull();
  });

  it('singulariza el nombre accesible cuando hay un solo trámite', () => {
    render(<EstadoFunnel counts={counts} active="" onSelect={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Rechazado: 1 trámite' })).toBeInTheDocument();
  });

  it('al hacer clic en un estado inactivo lo selecciona como filtro', () => {
    const onSelect = vi.fn();
    render(<EstadoFunnel counts={counts} active="" onSelect={onSelect} />);
    fireEvent.click(tarjeta('Entregado'));
    expect(onSelect).toHaveBeenCalledWith('entregado');
  });

  it('al hacer clic en el estado ya activo limpia el filtro (toggle) y marca aria-pressed', () => {
    const onSelect = vi.fn();
    render(<EstadoFunnel counts={counts} active="entregado" onSelect={onSelect} />);
    const entregado = tarjeta('Entregado');
    expect(entregado).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(entregado);
    expect(onSelect).toHaveBeenCalledWith('');
  });
});
