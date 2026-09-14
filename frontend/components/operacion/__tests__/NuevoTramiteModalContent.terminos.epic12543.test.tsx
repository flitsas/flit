// Epic #12543 — T&C dentro del modal «Nuevo trámite»: «Iniciar trámite» exige tipo elegido Y
// checkbox marcado (AC2/AC4, RN-06); el enlace abre en pestaña nueva sin tocar el checkbox (AC3,
// RN-02); al iniciar se registra la aceptación y SOLO con éxito se abre el asistente (AC5/AC6,
// RN-03); cada montaje del modal vuelve a pedirla (RN-05).
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NuevoTramiteModalContent } from '../NuevoTramiteModalContent';
import { TERMINOS_URL_FALLBACK } from '../TerminosCondicionesCheckbox';
import type { ProcedureTypeSummary } from '@/lib/api/types/procedure-parametrization';

const acceptProcedureTerms = vi.fn();
const getCurrentProcedureTerms = vi.fn();
vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    acceptProcedureTerms: (...a: unknown[]) => acceptProcedureTerms(...a),
    getCurrentProcedureTerms: (...a: unknown[]) => getCurrentProcedureTerms(...a),
  },
}));

const matricula = {
  id: 't-1',
  code: 'MATRICULA_NUEVA',
  name: 'Matrícula inicial',
  family: 'MATRICULAS',
  wizardEnabled: true,
  publicationStatus: 'published',
} as unknown as ProcedureTypeSummary;

vi.mock('@/hooks/useTiposHabilitados', () => ({
  useTiposHabilitados: () => ({
    familias: [{ family: 'MATRICULAS', tipos: [matricula] }],
    status: 'success',
    error: null,
    reload: vi.fn(),
  }),
}));

/** Elige «Matrícula Tradicional» en la tarjeta de Matrícula Inicial. */
async function elegirMatricula() {
  await userEvent.click(screen.getByRole('button', { name: /Matrícula Inicial: Selecciona tipo/ }));
  await userEvent.click(screen.getByRole('option', { name: 'Matrícula Tradicional' }));
}

function montar() {
  const onElegir = vi.fn();
  render(<NuevoTramiteModalContent onElegir={onElegir} onCancelar={vi.fn()} tituloEnContenedor />);
  return { onElegir };
}

describe('Nuevo trámite · Términos y Condiciones (Epic #12543)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getCurrentProcedureTerms.mockResolvedValue({ url: TERMINOS_URL_FALLBACK });
  });

  it('AC2/RN-06: con el tipo elegido pero sin marcar el checkbox, Iniciar sigue deshabilitado', async () => {
    montar();
    expect(screen.getByRole('checkbox')).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Iniciar trámite' })).toBeDisabled();

    await elegirMatricula();

    expect(screen.getByRole('button', { name: 'Iniciar trámite' })).toBeDisabled();
  });

  it('RN-06: con el checkbox marcado pero sin tipo, Iniciar sigue deshabilitado', async () => {
    montar();
    await userEvent.click(screen.getByRole('checkbox'));
    expect(screen.getByRole('button', { name: 'Iniciar trámite' })).toBeDisabled();
  });

  it('AC3/RN-02: el enlace abre el documento en pestaña nueva y no marca el checkbox', async () => {
    montar();
    const link = await screen.findByRole('link', { name: /Ver Términos y Condiciones/ });
    expect(link).toHaveAttribute('href', TERMINOS_URL_FALLBACK);
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'));

    await userEvent.click(link);
    expect(screen.getByRole('checkbox')).not.toBeChecked();
  });

  it('AC4/AC5/AC6: tipo + checkbox habilitan Iniciar; al iniciar registra la aceptación y solo entonces abre el asistente', async () => {
    acceptProcedureTerms.mockResolvedValue({ id: 'acc-1' });
    const { onElegir } = montar();

    await elegirMatricula();
    await userEvent.click(screen.getByRole('checkbox'));
    const iniciar = screen.getByRole('button', { name: 'Iniciar trámite' });
    expect(iniciar).toBeEnabled();

    await userEvent.click(iniciar);

    await waitFor(() => expect(onElegir).toHaveBeenCalledWith('MATRICULA_NUEVA'));
    expect(acceptProcedureTerms).toHaveBeenCalledWith('MATRICULA_NUEVA');
    expect(acceptProcedureTerms.mock.invocationCallOrder[0]).toBeLessThan(onElegir.mock.invocationCallOrder[0]);
  });

  it('RN-03: si el registro falla, avisa y NO abre el asistente', async () => {
    acceptProcedureTerms.mockRejectedValue(new Error('500'));
    const { onElegir } = montar();

    await elegirMatricula();
    await userEvent.click(screen.getByRole('checkbox'));
    await userEvent.click(screen.getByRole('button', { name: 'Iniciar trámite' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/No pudimos registrar tu aceptación/);
    expect(onElegir).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Iniciar trámite' })).toBeEnabled();
  });

  it('RN-05: un montaje nuevo del modal arranca con el checkbox desmarcado', async () => {
    acceptProcedureTerms.mockResolvedValue({ id: 'acc-1' });
    const primero = montar();
    await elegirMatricula();
    await userEvent.click(screen.getByRole('checkbox'));
    await userEvent.click(screen.getByRole('button', { name: 'Iniciar trámite' }));
    await waitFor(() => expect(primero.onElegir).toHaveBeenCalled());

    // El modal se cierra y se vuelve a abrir: nada quedó persistido en el cliente.
    document.body.innerHTML = '';
    montar();
    expect(screen.getByRole('checkbox')).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Iniciar trámite' })).toBeDisabled();
  });
});
