// HU #10799 — sección explícita de selección de placa preasignada (Flujo A) en el paso FUR.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  listAvailablePlatesForCompany: vi.fn(),
  patchFieldValues: vi.fn(),
}));

vi.mock('@/lib/api/admin-plate-ranges', () => ({
  listAvailablePlatesForCompany: mocks.listAvailablePlatesForCompany,
}));
vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: { patchFieldValues: mocks.patchFieldValues },
}));

import { PlacaPreasignadaSection } from '@/components/operacion/FirmaFurStep';

const plate = (id: string, p: string) => ({
  id,
  plateRangeId: 'r',
  tenantId: 't',
  transitOfficeId: 'o',
  plate: p,
  state: 'disponible' as const,
  procedureInstanceId: null,
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listAvailablePlatesForCompany.mockResolvedValue([plate('1', 'ABC100'), plate('2', 'ABC101')]);
  mocks.patchFieldValues.mockResolvedValue({});
});

describe('PlacaPreasignadaSection (HU #10799)', () => {
  it('AC2 — VIN con placa del RUNT (source consultation): no aplica, sin selector', async () => {
    render(
      <PlacaPreasignadaSection
        instanceId="i" organismoId="o" plateValue="XYZ999" plateSource="consultation" readOnly={false}
      />,
    );
    expect(await screen.findByText(/ya tiene placa asignada según el RUNT/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Buscar placa/i)).not.toBeInTheDocument();
    expect(mocks.listAvailablePlatesForCompany).not.toHaveBeenCalled();
  });

  it('AC1/AC5 — sin placa: muestra el selector con las placas y filtra por búsqueda', async () => {
    const user = userEvent.setup();
    render(
      <PlacaPreasignadaSection instanceId="i" organismoId="o" plateValue="" plateSource="" readOnly={false} />,
    );
    expect(await screen.findByRole('button', { name: 'ABC100' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'ABC101' })).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Buscar placa/i), '100');
    expect(screen.getByRole('button', { name: 'ABC100' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'ABC101' })).not.toBeInTheDocument();
  });

  it('AC3 — sin placas disponibles: informa que el OT asignará', async () => {
    mocks.listAvailablePlatesForCompany.mockResolvedValue([]);
    render(
      <PlacaPreasignadaSection instanceId="i" organismoId="o" plateValue="" plateSource="" readOnly={false} />,
    );
    expect(await screen.findByText(/No hay placas disponibles/i)).toBeInTheDocument();
  });

  it('AC4 — elegir una placa la persiste (field plate) y refresca', async () => {
    const onRefresh = vi.fn();
    const user = userEvent.setup();
    render(
      <PlacaPreasignadaSection
        instanceId="inst-1" organismoId="o" plateValue="" plateSource="" readOnly={false} onRefresh={onRefresh}
      />,
    );
    await user.click(await screen.findByRole('button', { name: 'ABC100' }));
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-1', [
        { formFieldId: null, fieldKey: 'plate', valueText: 'ABC100' },
      ]),
    );
    expect(onRefresh).toHaveBeenCalled();
  });

  it('placa ya elegida (source user): la muestra con opción Cambiar', () => {
    render(
      <PlacaPreasignadaSection
        instanceId="i" organismoId="o" plateValue="ABC100" plateSource="user" readOnly={false}
      />,
    );
    expect(screen.getByText(/Placa seleccionada:/i)).toBeInTheDocument();
    expect(screen.getByText('ABC100')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Cambiar/i })).toBeInTheDocument();
    expect(mocks.listAvailablePlatesForCompany).not.toHaveBeenCalled();
  });
});
