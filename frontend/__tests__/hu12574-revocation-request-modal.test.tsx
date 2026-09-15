import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { RevocationRequestButton } from '@/components/operacion/RevocationRequestButton';
import type { RevocationEligibility } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12574 (Feature #12565) — "Revocatoria – Modal Paso 1 y formulario Paso 2".
 *
 * AC1 — Paso 1 sin captura: al pulsar "Solicitar revocatoria" se abre un modal con la advertencia
 * "no tiene reversa", sin campos de captura, y hay que confirmar para avanzar al Paso 2.
 * AC2 — Paso 2 incompleto: enviar sin motivo, sin PDF o sin ambos checks bloquea el envío y señala
 * el campo faltante.
 * AC3 — Envío exitoso: el modal se cierra, no hay opción de retirar la solicitud (el botón queda
 * apagado con un estado de confirmación), y no se ofrece ninguna acción de retiro.
 */

const mocks = vi.hoisted(() => ({
  requestRevocation: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api/tramites-client')>(
    '@/lib/api/tramites-client',
  );
  return {
    ...actual,
    tramitesClient: { ...actual.tramitesClient, requestRevocation: mocks.requestRevocation },
  };
});

function base64Url(json: unknown): string {
  return btoa(JSON.stringify(json)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** JWT mínimo (sin firma real, `usePermissions` no la verifica) con el rol indicado. */
function setToken(role: string): void {
  const header = base64Url({ alg: 'none', typ: 'JWT' });
  const payload = base64Url({ sub: 'user-1', role });
  document.cookie = `flit_token=${header}.${payload}.; path=/`;
}

function clearToken(): void {
  document.cookie = 'flit_token=; path=/; Max-Age=0';
}

const eligibleWithoutWindow: RevocationEligibility = {
  sourceSupported: true,
  windowExpiresAt: null,
  windowExpired: false,
};

async function openModalAtStep2(): Promise<void> {
  await userEvent.click(screen.getByRole('button', { name: /Solicitar revocatoria/i }));
  const dialog = await screen.findByRole('dialog', { name: 'Solicitar revocatoria' });
  // AC1 — Paso 1: solo la advertencia, sin campos de captura.
  expect(within(dialog).getByText(/no tiene reversa/i)).toBeInTheDocument();
  expect(dialog.querySelector('textarea, input[type="file"], input[type="checkbox"]')).toBeNull();
  await userEvent.click(within(dialog).getByRole('button', { name: 'Continuar' }));
}

afterEach(() => {
  clearToken();
  mocks.requestRevocation.mockReset();
});

describe('RevocationRequestModal — HU #12574', () => {
  it('AC1 — Paso 1 es solo la advertencia y exige confirmar para llegar al Paso 2', async () => {
    setToken('AdminCompany');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={eligibleWithoutWindow} />);

    await openModalAtStep2();

    const dialog = await screen.findByRole('dialog', { name: /Solicitar revocatoria/ });
    expect(within(dialog).getByLabelText('Motivo de la solicitud')).toBeInTheDocument();
    expect(within(dialog).getByText('Documento de soporte (PDF)')).toBeInTheDocument();
    expect(
      within(dialog).getByText('Confirmo que la información registrada en esta solicitud es correcta.'),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(
        'Entiendo que esta solicitud de revocatoria no se puede retirar una vez enviada.',
      ),
    ).toBeInTheDocument();
  });

  it('AC2 — enviar el Paso 2 vacío bloquea el envío y señala cada campo faltante', async () => {
    setToken('AdminCompany');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={eligibleWithoutWindow} />);
    await openModalAtStep2();

    const dialog = await screen.findByRole('dialog', { name: /Solicitar revocatoria/ });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }));

    expect(within(dialog).getByText('Indica el motivo de la solicitud de revocatoria.')).toBeInTheDocument();
    expect(within(dialog).getByText('Adjunta el documento de soporte (PDF).')).toBeInTheDocument();
    expect(
      within(dialog).getByText('Debes confirmar que la información registrada es correcta.'),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText('Debes confirmar que entiendes que la solicitud no se puede retirar.'),
    ).toBeInTheDocument();
    expect(mocks.requestRevocation).not.toHaveBeenCalled();
  });

  it('AC2 — con motivo y PDF pero sin los 2 checks, el envío sigue bloqueado', async () => {
    setToken('AdminCompany');
    render(<RevocationRequestButton instanceId="inst-1" eligibility={eligibleWithoutWindow} />);
    await openModalAtStep2();

    const dialog = await screen.findByRole('dialog', { name: /Solicitar revocatoria/ });
    await userEvent.type(
      within(dialog).getByLabelText('Motivo de la solicitud'),
      'El vehículo no cumplía los requisitos al momento de aprobar.',
    );
    const file = new File(['%PDF-1.4'], 'soporte.pdf', { type: 'application/pdf' });
    const fileInput = dialog.querySelector('input[type="file"]') as HTMLInputElement;
    await userEvent.upload(fileInput, file);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }));

    expect(
      within(dialog).getByText('Debes confirmar que la información registrada es correcta.'),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText('Debes confirmar que entiendes que la solicitud no se puede retirar.'),
    ).toBeInTheDocument();
    expect(mocks.requestRevocation).not.toHaveBeenCalled();
  });

  it('AC3 — envío exitoso: cierra el modal, no ofrece retiro y el botón queda apagado', async () => {
    setToken('AdminCompany');
    mocks.requestRevocation.mockResolvedValue({
      id: 'rev-1',
      procedureInstanceId: 'inst-1',
      attemptNumber: 1,
      status: 'solicitada',
      requestedAt: '2026-09-15T12:00:00Z',
    });
    const onRequested = vi.fn();
    render(
      <RevocationRequestButton
        instanceId="inst-1"
        eligibility={eligibleWithoutWindow}
        onRequested={onRequested}
      />,
    );
    await openModalAtStep2();

    const dialog = await screen.findByRole('dialog', { name: /Solicitar revocatoria/ });
    await userEvent.type(
      within(dialog).getByLabelText('Motivo de la solicitud'),
      'El vehículo no cumplía los requisitos al momento de aprobar.',
    );
    const file = new File(['%PDF-1.4'], 'soporte.pdf', { type: 'application/pdf' });
    const fileInput = dialog.querySelector('input[type="file"]') as HTMLInputElement;
    await userEvent.upload(fileInput, file);
    await userEvent.click(
      within(dialog).getByLabelText('Confirmo que la información registrada en esta solicitud es correcta.'),
    );
    await userEvent.click(
      within(dialog).getByLabelText(
        'Entiendo que esta solicitud de revocatoria no se puede retirar una vez enviada.',
      ),
    );

    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }));

    expect(mocks.requestRevocation).toHaveBeenCalledWith('inst-1', {
      reason: 'El vehículo no cumplía los requisitos al momento de aprobar.',
      confirmAccuracy: true,
      confirmConsequences: true,
      file,
    });

    // El modal se cierra.
    expect(await screen.findByText('Solicitud de revocatoria enviada — en revisión')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    // No queda ningún botón accionable para volver a solicitar/retirar (AC3).
    expect(screen.queryByRole('button', { name: /Solicitar revocatoria/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /retirar/i })).not.toBeInTheDocument();
    expect(onRequested).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'rev-1', status: 'solicitada' }),
    );
  });

  it('AC2 — error 422 del backend (motivo_requerido) señala el campo motivo tras el intento de envío', async () => {
    setToken('AdminCompany');
    const { TramitesApiError } = await import('@/lib/api/tramites-client');
    mocks.requestRevocation.mockRejectedValue(
      new TramitesApiError(422, 'Debe indicar el motivo de la solicitud de revocatoria.', {
        title: 'motivo_requerido',
        detail: 'Debe indicar el motivo de la solicitud de revocatoria.',
      }),
    );
    render(<RevocationRequestButton instanceId="inst-1" eligibility={eligibleWithoutWindow} />);
    await openModalAtStep2();

    const dialog = await screen.findByRole('dialog', { name: /Solicitar revocatoria/ });
    // Se fuerza el envío al backend rellenando client-side y dejando que el server rechace (simula
    // una carrera donde el server tiene la última palabra sobre el campo).
    await userEvent.type(within(dialog).getByLabelText('Motivo de la solicitud'), 'x');
    const file = new File(['%PDF-1.4'], 'soporte.pdf', { type: 'application/pdf' });
    const fileInput = dialog.querySelector('input[type="file"]') as HTMLInputElement;
    await userEvent.upload(fileInput, file);
    await userEvent.click(
      within(dialog).getByLabelText('Confirmo que la información registrada en esta solicitud es correcta.'),
    );
    await userEvent.click(
      within(dialog).getByLabelText(
        'Entiendo que esta solicitud de revocatoria no se puede retirar una vez enviada.',
      ),
    );

    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }));

    expect(
      await within(dialog).findByText('Debe indicar el motivo de la solicitud de revocatoria.'),
    ).toBeInTheDocument();
  });
});
