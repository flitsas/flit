// HU #10799 — sección explícita de selección de placa preasignada (Flujo A) en el paso FUR.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  listAvailablePlatesForCompany: vi.fn(),
  getPlatePreassignStatus: vi.fn(),
  patchFieldValues: vi.fn(),
}));

vi.mock('@/lib/api/admin-plate-ranges', () => ({
  listAvailablePlatesForCompany: mocks.listAvailablePlatesForCompany,
  getPlatePreassignStatus: mocks.getPlatePreassignStatus,
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
  mocks.getPlatePreassignStatus.mockResolvedValue({ enabled: true });
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

  // HU #10806 AC3 — ruta de placa NO habilitada para la compañía/OT: avisa y NO muestra el selector.
  it('AC3 (HU #10806) — preasignación no habilitada: muestra aviso y oculta el selector', async () => {
    mocks.getPlatePreassignStatus.mockResolvedValue({ enabled: false });
    render(
      <PlacaPreasignadaSection instanceId="i" organismoId="o" plateValue="" plateSource="" readOnly={false} />,
    );
    expect(await screen.findByText(/no está habilitada para este organismo/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Buscar placa/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Dígito de preferencia de placa/i)).not.toBeInTheDocument();
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
    expect(screen.getByRole('button', { name: /Quitar placa/i })).toBeInTheDocument();
    expect(mocks.listAvailablePlatesForCompany).not.toHaveBeenCalled();
  });

  // HU #10806 AC1 — "Quitar placa" limpia el field plate (='') y reabre el selector + el dígito,
  // de modo que se pueda radicar sin placa o con dígito de preferencia tras haber elegido una.
  it('AC1 (HU #10806) — "Quitar placa" limpia el field plate y reabre el selector', async () => {
    const onRefresh = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(
      <PlacaPreasignadaSection
        instanceId="inst-x" organismoId="o" plateValue="ABC100" plateSource="user"
        readOnly={false} onRefresh={onRefresh}
      />,
    );
    await user.click(screen.getByRole('button', { name: /Quitar placa/i }));
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-x', [
        { formFieldId: null, fieldKey: 'plate', valueText: '' },
      ]),
    );
    expect(onRefresh).toHaveBeenCalled();
    // Tras el refresh, el padre re-renderiza sin placa → reaparece el selector y el dígito.
    rerender(
      <PlacaPreasignadaSection
        instanceId="inst-x" organismoId="o" plateValue="" plateSource=""
        readOnly={false} onRefresh={onRefresh}
      />,
    );
    expect(await screen.findByLabelText(/Buscar placa/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Dígito de preferencia de placa/i)).toBeInTheDocument();
  });
});

// HU #10805 — dígito de preferencia (guía para el OT); se captura al radicar sin placa.
// Uso de ejemplo: <PlacaPreasignadaSection preferredDigitValue="5" ... /> → select en "Termina en 5"
describe('PlacaPreasignadaSection — dígito de preferencia (HU #10805)', () => {
  // AC1/AC5 — el control ofrece exactamente "Sin preferencia" + 0..9 (entrada acotada, no texto libre).
  it('AC1/AC5 — ofrece el dígito de preferencia con opciones Sin preferencia + 0-9', async () => {
    render(
      <PlacaPreasignadaSection instanceId="i" organismoId="o" plateValue="" plateSource="" readOnly={false} />,
    );
    const select = await screen.findByLabelText(/Dígito de preferencia de placa/i);
    expect(within(select).getAllByRole('option')).toHaveLength(11); // Sin preferencia + 0..9
  });

  // AC1 — seleccionar un dígito lo persiste en el field plate_preferred_last_digit y refresca.
  it('AC1 — seleccionar un dígito lo persiste y refresca', async () => {
    const onRefresh = vi.fn();
    const user = userEvent.setup();
    render(
      <PlacaPreasignadaSection
        instanceId="inst-9" organismoId="o" plateValue="" plateSource="" readOnly={false} onRefresh={onRefresh}
      />,
    );
    const select = await screen.findByLabelText(/Dígito de preferencia de placa/i);
    await user.selectOptions(select, '5');
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-9', [
        { formFieldId: null, fieldKey: 'plate_preferred_last_digit', valueText: '5' },
      ]),
    );
    expect(onRefresh).toHaveBeenCalled();
  });

  // AC4 (edge) — si ya hay dígito persistido, el control lo prellena.
  it('AC4 — prellena el dígito de preferencia persistido', async () => {
    render(
      <PlacaPreasignadaSection
        instanceId="i" organismoId="o" plateValue="" plateSource="" preferredDigitValue="7" readOnly={false}
      />,
    );
    const select = (await screen.findByLabelText(
      /Dígito de preferencia de placa/i,
    )) as HTMLSelectElement;
    expect(select.value).toBe('7');
  });
});
