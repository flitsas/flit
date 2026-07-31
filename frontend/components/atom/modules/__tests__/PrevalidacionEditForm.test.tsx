/**
 * Tests HU #10944 (Feature #10864, CF-03) — unidad de PrevalidacionEditForm y helper
 * parseRateLimitDetail. Vitest + RTL.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  editPrevalidacion: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    editPrevalidacion: mocks.editPrevalidacion,
  },
  TramitesApiError: class TramitesApiError extends Error {
    constructor(
      public status: number,
      message: string,
      public problem: Record<string, unknown> | null = null,
    ) {
      super(message);
      this.name = 'TramitesApiError';
    }
  },
}));

import { PrevalidacionEditForm, parseRateLimitDetail } from '@/components/atom/modules/PrevalidacionEditForm';
import type { TenantBiometricValidation } from '@/lib/api/types/procedure-runtime';

const ROW: TenantBiometricValidation = {
  id: 'pv-1',
  instanceId: null,
  referenceNumber: null,
  modalidad: null,
  partyRole: null,
  name: 'Carlos Prueba',
  documentType: 'CE',
  documentNumber: '555666777',
  status: 'enviado',
  score: null,
  provider: 'mock',
  expired: false,
  rejectionReason: null,
  createdAt: '2026-07-20T10:00:00Z',
  validatedAt: null,
  validUntil: null,
  daysRemaining: null,
  captureUrl: null,
  linkExpiresAt: null,
  email: 'carlos.prueba@old.com', // CF-05 (HU #11006)
};

describe('parseRateLimitDetail (HU #10944, D10)', () => {
  it('extrae los minutos restantes del mensaje de cooldown', () => {
    expect(parseRateLimitDetail('Espera 3 minuto(s) antes de reenviar de nuevo.')).toEqual({
      cooldownMinutes: 3,
    });
  });

  it('detecta el tope de reenvíos agotado', () => {
    expect(
      parseRateLimitDetail('Se agotaron los reenvíos disponibles. Anula el registro y crea una prevalidación nueva.'),
    ).toEqual({ maxedOut: true });
  });

  it('devuelve un objeto vacío si el mensaje no es de rate limit', () => {
    expect(parseRateLimitDetail('La identidad ya está aprobada.')).toEqual({});
  });
});

describe('PrevalidacionEditForm (HU #10944)', () => {
  const onClose = vi.fn();
  const onSaved = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('precarga el nombre y deshabilita tipo/número de documento con la razón visible (AC3)', () => {
    render(<PrevalidacionEditForm row={ROW} onClose={onClose} onSaved={onSaved} />);

    expect((screen.getByLabelText(/^nombre$/i) as HTMLInputElement).value).toBe('Carlos Prueba');
    expect(screen.getByLabelText(/tipo de documento/i)).toBeDisabled();
    expect(screen.getByLabelText(/número de documento/i)).toBeDisabled();
    expect(screen.getByText(/no son editables porque definen la identidad/i)).toBeInTheDocument();
  });

  it('CF-01: no ofrece campos de persona jurídica ni representante legal', () => {
    render(<PrevalidacionEditForm row={ROW} onClose={onClose} onSaved={onSaved} />);

    expect(screen.queryByRole('button', { name: /representante legal/i })).toBeNull();
    expect(screen.queryByLabelText(/nombre del representante legal/i)).toBeNull();
    expect(screen.queryByLabelText(/correo del representante legal/i)).toBeNull();
    expect(screen.queryByText(/persona jurídica/i)).toBeNull();
  });

  it('rechaza un correo con formato inválido antes de llamar a la API', async () => {
    const user = userEvent.setup();
    render(<PrevalidacionEditForm row={ROW} onClose={onClose} onSaved={onSaved} />);

    await user.type(screen.getByLabelText(/nuevo correo electrónico/i), 'no-es-un-correo');
    expect(screen.getByText(/^correo inválido$/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /guardar y reenviar/i })).toBeDisabled();
    expect(mocks.editPrevalidacion).not.toHaveBeenCalled();
  });

  it('invoca onClose al pulsar Cancelar', async () => {
    const user = userEvent.setup();
    render(<PrevalidacionEditForm row={ROW} onClose={onClose} onSaved={onSaved} />);

    await user.click(screen.getByRole('button', { name: /cancelar/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('propaga un 429 al callback onRateLimited con los minutos detectados', async () => {
    const { TramitesApiError } = await import('@/lib/api/tramites-client');
    mocks.editPrevalidacion.mockRejectedValueOnce(
      new TramitesApiError(429, 'Espera 4 minuto(s) antes de reenviar de nuevo.', null),
    );
    const onRateLimited = vi.fn();

    const user = userEvent.setup();
    render(
      <PrevalidacionEditForm row={ROW} onClose={onClose} onSaved={onSaved} onRateLimited={onRateLimited} />,
    );

    await user.type(screen.getByLabelText(/nuevo correo electrónico/i), 'nuevo@correo.com');
    await user.click(screen.getByRole('button', { name: /guardar y reenviar/i }));

    await waitFor(() => {
      expect(onRateLimited).toHaveBeenCalledWith({ cooldownMinutes: 4 });
    });
    expect(screen.getByRole('alert')).toHaveTextContent(/espera 4 minuto/i);
  });
});
