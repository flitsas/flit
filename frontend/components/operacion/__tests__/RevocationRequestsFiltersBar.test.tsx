// HU #12578 (Feature #12565) — AC1: filtros (fecha, OT, estado) de la vista dedicada "Revocatorias",
// "los mismos filtros del listado general". Uso de ejemplo:
// <RevocationRequestsFiltersBar value={...} onChange={fn} transitOfficeOptions={[...]} /> →
// cada cambio dispara onChange de inmediato (sin estado "draft/aplicado").
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  RevocationRequestsFiltersBar,
  type RevocationRequestsFiltersValue,
} from '../RevocationRequestsFiltersBar';

const EMPTY: RevocationRequestsFiltersValue = {
  requestedFrom: '',
  requestedTo: '',
  statuses: [],
  transitOfficeId: '',
};

describe('RevocationRequestsFiltersBar', () => {
  it('AC1 — con transitOfficeOptions (lado gestor) ofrece el selector de organismo', () => {
    render(
      <RevocationRequestsFiltersBar
        value={EMPTY}
        onChange={vi.fn()}
        transitOfficeOptions={[{ id: 'ot-1', name: 'OT Bogotá', code: '11-001' }]}
      />,
    );

    expect(screen.getByRole('combobox', { name: /organismo de tránsito/i })).toBeInTheDocument();
  });

  it('sin transitOfficeOptions (lado OT) NO ofrece el selector de organismo', () => {
    render(<RevocationRequestsFiltersBar value={EMPTY} onChange={vi.fn()} />);

    expect(screen.queryByRole('combobox', { name: /organismo de tránsito/i })).not.toBeInTheDocument();
  });

  it('cambiar la fecha inicial dispara onChange con el nuevo valor, preservando el resto', async () => {
    const onChange = vi.fn();
    render(<RevocationRequestsFiltersBar value={EMPTY} onChange={onChange} />);

    await userEvent.type(screen.getByLabelText(/Fecha inicial de solicitud/i), '2026-01-01');

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ requestedFrom: '2026-01-01' }));
  });

  it('AC1 — el popover de estado permite marcar más de un sub-estado (multi-select)', async () => {
    const onChange = vi.fn();
    render(<RevocationRequestsFiltersBar value={EMPTY} onChange={onChange} />);

    await userEvent.click(screen.getByRole('button', { name: /^Estado$/i }));
    await userEvent.click(screen.getByRole('checkbox', { name: /solicitada/i }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ statuses: ['solicitada'] }));
  });

  it('con filtros activos, "Limpiar filtros" los resetea todos', async () => {
    const onChange = vi.fn();
    render(
      <RevocationRequestsFiltersBar
        value={{ ...EMPTY, requestedFrom: '2026-01-01', statuses: ['solicitada'] }}
        onChange={onChange}
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: /Limpiar filtros/i }));

    expect(onChange).toHaveBeenCalledWith(EMPTY);
  });

  it('sin filtros activos, "Limpiar filtros" no se muestra', () => {
    render(<RevocationRequestsFiltersBar value={EMPTY} onChange={vi.fn()} />);

    expect(screen.queryByRole('button', { name: /Limpiar filtros/i })).not.toBeInTheDocument();
  });

  it('disabled=true deshabilita los controles de fecha', () => {
    render(<RevocationRequestsFiltersBar value={EMPTY} onChange={vi.fn()} disabled />);

    expect(screen.getByLabelText(/Fecha inicial de solicitud/i)).toBeDisabled();
  });
});
