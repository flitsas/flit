/**
 * Tests HU #11007 (Feature #11004, CF-07) — Panel de tracking de identidad compartido.
 *
 * AC1 — Sin gate SuperAdmin: el panel se renderiza y consulta el endpoint por validationId para
 *        cualquier usuario del módulo.
 * AC2 — Tracking de una prevalidación standalone: la línea de tiempo se pinta con solo el validationId.
 * AC3 — Aislamiento cross-tenant: un 404 se traduce al estado de error del panel.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  getBiometricAuditByValidation: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getBiometricAuditByValidation: mocks.getBiometricAuditByValidation,
  },
}));

import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';

beforeEach(() => vi.clearAllMocks());

describe('IdentityValidationTrackingPanel (HU #11007)', () => {
  it('AC1 — al abrir "Ver tracking" consulta el endpoint por validationId, sin evaluar ningún gate de rol', async () => {
    const user = userEvent.setup();
    mocks.getBiometricAuditByValidation.mockResolvedValue({
      validationId: 'val-1',
      events: [
        {
          occurredAt: '2026-07-20T10:00:00Z',
          stage: 'send',
          outcome: 'ok',
          httpStatus: 200,
          signaturePresent: true,
          secretPresent: true,
          decryptOk: true,
          providerStatus: null,
          errorType: null,
          message: null,
        },
      ],
      referencedFromOtherProcedure: false,
    });

    render(<IdentityValidationTrackingPanel validationId="val-1" />);

    await user.click(screen.getByRole('button', { name: /ver tracking/i }));

    expect(mocks.getBiometricAuditByValidation).toHaveBeenCalledWith('val-1');
    expect(await screen.findByText('Envío al proveedor')).toBeInTheDocument();
    expect(screen.getByText('OK (HTTP 200)')).toBeInTheDocument();
  });

  it('AC2 — pinta la línea de tiempo de una prevalidación standalone (sin instanceId) usando solo validationId', async () => {
    const user = userEvent.setup();
    mocks.getBiometricAuditByValidation.mockResolvedValue({
      validationId: 'val-standalone',
      events: [
        {
          occurredAt: '2026-07-20T10:00:00Z',
          stage: 'webhook_received',
          outcome: 'aprobado',
          httpStatus: null,
          signaturePresent: null,
          secretPresent: null,
          decryptOk: true,
          providerStatus: null,
          errorType: null,
          message: null,
        },
      ],
      referencedFromOtherProcedure: false,
    });

    render(<IdentityValidationTrackingPanel validationId="val-standalone" />);
    await user.click(screen.getByRole('button', { name: /ver tracking/i }));

    expect(await screen.findByText('Notificación recibida')).toBeInTheDocument();
    expect(screen.getByText('Aprobado')).toBeInTheDocument();
    // La columna «Cifrado» dice si el cuerpo cifrado del proveedor se pudo descifrar. Antes lo
    // resumía en «OK», que bajo ese encabezado se leía como el algoritmo; ahora lo dice en
    // palabras.
    expect(screen.getByText('Verificado')).toBeInTheDocument();
  });

  it('AC3 — un 404 cross-tenant se muestra como error, sin filtrar la existencia del registro', async () => {
    const user = userEvent.setup();
    mocks.getBiometricAuditByValidation.mockRejectedValue(
      new Error('Validación de identidad no encontrada.'),
    );

    render(<IdentityValidationTrackingPanel validationId="val-otro-tenant" />);
    await user.click(screen.getByRole('button', { name: /ver tracking/i }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/no encontrada/i);
  });

  it('sin eventos registrados muestra el estado vacío del panel', async () => {
    const user = userEvent.setup();
    mocks.getBiometricAuditByValidation.mockResolvedValue({
      validationId: 'val-vacio',
      events: [],
      referencedFromOtherProcedure: false,
    });

    render(<IdentityValidationTrackingPanel validationId="val-vacio" />);
    await user.click(screen.getByRole('button', { name: /ver tracking/i }));

    expect(await screen.findByText(/sin eventos registrados/i)).toBeInTheDocument();
  });

  it('validación reutilizada de otro trámite muestra el aviso informativo', async () => {
    const user = userEvent.setup();
    mocks.getBiometricAuditByValidation.mockResolvedValue({
      validationId: 'val-reusada',
      events: [],
      referencedFromOtherProcedure: true,
    });

    render(<IdentityValidationTrackingPanel validationId="val-reusada" />);
    await user.click(screen.getByRole('button', { name: /ver tracking/i }));

    expect(await screen.findByText(/reutilizada de otro trámite/i)).toBeInTheDocument();
  });
});
