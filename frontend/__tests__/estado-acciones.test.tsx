import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// N 03 — acciones de transición del detalle del trámite: solo se muestran las
// que permite la máquina (allowedTransitions del wizard) y anular exige motivo.

const mocks = vi.hoisted(() => ({
  getWizardState: vi.fn(),
  transitionInstance: vi.fn(),
  // #10611/#10785 — el componente lee la instancia para el soat_estado y el sub-estado de placa.
  getInstance: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
}));

import { EstadoAcciones } from '@/components/operacion/EstadoAcciones';

const wizardWith = (status: string, allowedTransitions: string[]) => ({
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: false,
  blockers: [],
  status,
  allowedTransitions,
  steps: [],
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.transitionInstance.mockResolvedValue({ id: 'inst-1', status: 'anulado' });
  // Instancia mínima: sin ruta de placa (plateFlowStatus null) → no se pinta el panel de SOAT.
  mocks.getInstance.mockResolvedValue({ fieldValues: [], plateFlowStatus: null });
});

describe('EstadoAcciones — el backend manda', () => {
  it('borrador: chip "Borrador" + botón Anular; sin botón Subsanar', async () => {
    mocks.getWizardState.mockResolvedValue(wizardWith('borrador', ['anulado', 'preparado']));
    render(<EstadoAcciones instanceId="inst-1" />);

    expect(await screen.findByText('Borrador')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Anular trámite' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Subsanar' })).not.toBeInTheDocument();
  });

  it('rechazado: ofrece Anular y Subsanar (subsanación por el operador, sin volver a borrador)', async () => {
    mocks.getWizardState.mockResolvedValue(wizardWith('rechazado', ['subsanacion', 'anulado']));
    render(<EstadoAcciones instanceId="inst-1" />);

    expect(await screen.findByText('Rechazado')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Anular trámite' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Subsanar' })).toBeInTheDocument();
    // La subsanación reemplaza a "Volver a borrador": ya no se ofrece esa vuelta.
    expect(screen.queryByRole('button', { name: 'Volver a borrador' })).not.toBeInTheDocument();
  });

  it('subsanar: transiciona el trámite rechazado a subsanacion (sin motivo obligatorio)', async () => {
    mocks.getWizardState.mockResolvedValue(wizardWith('rechazado', ['subsanacion', 'anulado']));
    mocks.transitionInstance.mockResolvedValue({ id: 'inst-1', status: 'subsanacion' });
    const onChanged = vi.fn();
    const user = userEvent.setup();
    render(<EstadoAcciones instanceId="inst-1" onChanged={onChanged} />);

    await user.click(await screen.findByRole('button', { name: 'Subsanar' }));
    await user.click(screen.getByRole('button', { name: /Confirmar: Subsanar/ }));

    await waitFor(() =>
      expect(mocks.transitionInstance).toHaveBeenCalledWith('inst-1', 'subsanacion', undefined),
    );
    expect(onChanged).toHaveBeenCalledTimes(1);
  });

  it('estado final (aprobado, sin transiciones): no pinta ningún botón de acción', async () => {
    mocks.getWizardState.mockResolvedValue(wizardWith('aprobado', []));
    render(<EstadoAcciones instanceId="inst-1" />);

    expect(await screen.findByText('Aprobado')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Anular trámite' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Subsanar' })).not.toBeInTheDocument();
  });

  it('anular sin motivo NO llama al API y muestra el error; con motivo transiciona y notifica', async () => {
    mocks.getWizardState.mockResolvedValue(wizardWith('borrador', ['anulado', 'preparado']));
    const onChanged = vi.fn();
    const user = userEvent.setup();
    render(<EstadoAcciones instanceId="inst-1" onChanged={onChanged} />);

    await user.click(await screen.findByRole('button', { name: 'Anular trámite' }));
    await user.click(screen.getByRole('button', { name: /Confirmar: Anular trámite/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/motivo/i);
    expect(mocks.transitionInstance).not.toHaveBeenCalled();

    await user.type(screen.getByRole('textbox'), 'Cliente desistió de la compra');
    await user.click(screen.getByRole('button', { name: /Confirmar: Anular trámite/ }));

    await waitFor(() =>
      expect(mocks.transitionInstance).toHaveBeenCalledWith(
        'inst-1',
        'anulado',
        'Cliente desistió de la compra',
      ),
    );
    expect(onChanged).toHaveBeenCalledTimes(1);
  });

  it('un error del API (p. ej. conflicto de concurrencia) se muestra como alerta', async () => {
    mocks.getWizardState.mockResolvedValue(wizardWith('rechazado', ['subsanacion', 'anulado']));
    mocks.transitionInstance.mockRejectedValue(
      new Error('El trámite fue modificado por otro usuario, recarga e intenta de nuevo.'),
    );
    const user = userEvent.setup();
    render(<EstadoAcciones instanceId="inst-1" />);

    await user.click(await screen.findByRole('button', { name: 'Subsanar' }));
    await user.click(screen.getByRole('button', { name: /Confirmar: Subsanar/ }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/modificado por otro usuario/i);
  });
});
