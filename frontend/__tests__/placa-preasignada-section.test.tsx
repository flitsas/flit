// Epic #12550 (HU #12650) — tarjeta «Placa» del paso FUR: la placa la decide el RUNT (Ruta Corta,
// solo lectura) o la asigna el organismo (Ruta Larga, solo dígito de preferencia). El selector de
// placas del inventario (HU #10799/#10806) se retiró: elegir una placa aquí saltaba la asignación
// del organismo.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  listAvailablePlatesForCompany: vi.fn(),
  getPlatePreassignStatus: vi.fn(),
  patchFieldValues: vi.fn(),
  generarFur: vi.fn(),
}));

vi.mock('@/lib/api/admin-plate-ranges', () => ({
  listAvailablePlatesForCompany: mocks.listAvailablePlatesForCompany,
  getPlatePreassignStatus: mocks.getPlatePreassignStatus,
}));
vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    patchFieldValues: mocks.patchFieldValues,
    generarFur: mocks.generarFur,
  },
}));

import { PlacaPreasignadaSection } from '@/components/operacion/FirmaFurStep';

beforeEach(() => {
  vi.clearAllMocks();
  mocks.patchFieldValues.mockResolvedValue({});
  mocks.generarFur.mockResolvedValue({ documents: [] });
});

describe('PlacaPreasignadaSection (Epic #12550, HU #12650)', () => {
  it('AC1 — Ruta Corta: placa del RUNT en solo lectura, sin dígito ni inventario', async () => {
    render(
      <PlacaPreasignadaSection instanceId="i" plateValue="WVT948" plateSource="consultation" readOnly={false} />,
    );
    const nota = await screen.findByTestId('fur-placa-ruta-corta');
    expect(nota).not.toHaveTextContent(/Ruta (Corta|Larga)/);
    expect(nota).toHaveTextContent('WVT948');
    expect(nota).toHaveTextContent(/no se pueden modificar/);
    expect(screen.queryByLabelText(/Dígito de preferencia de placa/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Buscar placa/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Quitar placa|Cambiar/ })).not.toBeInTheDocument();
  });

  it('AC2 — Ruta Larga: dígito de preferencia editable y NINGUNA placa del inventario', async () => {
    render(<PlacaPreasignadaSection instanceId="i" plateValue="" plateSource="" readOnly={false} />);
    expect(await screen.findByTestId('fur-placa-ruta-larga')).toHaveTextContent(/El organismo de tránsito la asignará en Preasignación/);
    expect(screen.getByLabelText(/Dígito de preferencia de placa/i)).toBeEnabled();
    expect(screen.queryByLabelText(/Buscar placa/i)).not.toBeInTheDocument();
    // AC2 — ni se consulta el inventario ni el estado de la ruta.
    expect(mocks.listAvailablePlatesForCompany).not.toHaveBeenCalled();
    expect(mocks.getPlatePreassignStatus).not.toHaveBeenCalled();
  });

  it('AC3 — desde este paso no se escribe la placa: solo el dígito de preferencia', async () => {
    const onRefresh = vi.fn();
    const user = userEvent.setup();
    render(
      <PlacaPreasignadaSection instanceId="inst-9" plateValue="" plateSource="" readOnly={false} onRefresh={onRefresh} />,
    );
    await user.selectOptions(await screen.findByLabelText(/Dígito de preferencia de placa/i), '5');
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-9', [
        { formFieldId: null, fieldKey: 'plate_preferred_last_digit', valueText: '5' },
      ]),
    );
    expect(onRefresh).toHaveBeenCalled();
    const escritos = mocks.patchFieldValues.mock.calls.flatMap((c) => c[1] as { fieldKey: string }[]);
    expect(escritos.some((f) => f.fieldKey === 'plate')).toBe(false);
    expect(escritos.some((f) => f.fieldKey === 'plate_route_active')).toBe(false);
  });

  it('AC5 — en solo lectura el dígito no se edita', async () => {
    render(<PlacaPreasignadaSection instanceId="i" plateValue="" plateSource="" readOnly />);
    expect(await screen.findByLabelText(/Dígito de preferencia de placa/i)).toBeDisabled();
  });

  // Borrador anterior a la Epic #12550 con una placa elegida por el gestor desde el inventario.
  it('placa elegida por el gestor antes del cambio (source user): se muestra y solo se puede quitar', async () => {
    const onRefresh = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(
      <PlacaPreasignadaSection instanceId="inst-x" plateValue="ABC100" plateSource="user" readOnly={false} onRefresh={onRefresh} />,
    );
    expect(screen.getByText('ABC100')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cambiar/i })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Quitar placa/i }));
    await waitFor(() =>
      expect(mocks.patchFieldValues).toHaveBeenCalledWith('inst-x', [
        { formFieldId: null, fieldKey: 'plate', valueText: '' },
      ]),
    );
    expect(mocks.generarFur).toHaveBeenCalledWith('inst-x');
    expect(onRefresh).toHaveBeenCalled();

    // Tras el refresh, el padre re-renderiza sin placa → Ruta Larga con el dígito.
    rerender(
      <PlacaPreasignadaSection instanceId="inst-x" plateValue="" plateSource="" readOnly={false} onRefresh={onRefresh} />,
    );
    expect(await screen.findByTestId('fur-placa-ruta-larga')).toBeInTheDocument();
  });
});

// HU #10805 — dígito de preferencia (guía para el OT); se captura al radicar sin placa.
describe('PlacaPreasignadaSection — dígito de preferencia (HU #10805)', () => {
  it('AC1/AC5 — ofrece el dígito de preferencia con opciones Sin preferencia + 0-9', async () => {
    render(<PlacaPreasignadaSection instanceId="i" plateValue="" plateSource="" readOnly={false} />);
    const select = await screen.findByLabelText(/Dígito de preferencia de placa/i);
    expect(within(select).getAllByRole('option')).toHaveLength(11); // Sin preferencia + 0..9
  });

  it('AC4 — prellena el dígito de preferencia persistido', async () => {
    render(
      <PlacaPreasignadaSection instanceId="i" plateValue="" plateSource="" preferredDigitValue="7" readOnly={false} />,
    );
    const select = (await screen.findByLabelText(/Dígito de preferencia de placa/i)) as HTMLSelectElement;
    expect(select.value).toBe('7');
  });
});
