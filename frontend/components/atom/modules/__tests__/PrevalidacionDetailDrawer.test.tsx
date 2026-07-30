/**
 * Tests HU #11008 (Feature #11004, CF-06) — Drawer de detalle de prevalidación con poll.
 *
 * AC1 — Carga el detalle por id al abrir (estado cargando → lleno).
 * AC2 — Refresca cada 5s mientras el estado no sea terminal.
 * AC3 — Se detiene el poll al llegar a un estado terminal (p. ej. aprobado).
 * AC5 — El tracking (IdentityValidationTrackingPanel) se embebe dentro del drawer.
 * AC6 — Un error de carga muestra el estado de error, sin datos parciales.
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  getPrevalidacionDetail: vi.fn(),
  getBiometricAuditByValidation: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getPrevalidacionDetail: mocks.getPrevalidacionDetail,
    getBiometricAuditByValidation: mocks.getBiometricAuditByValidation,
  },
}));

import { PrevalidacionDetailDrawer } from '@/components/atom/modules/PrevalidacionDetailDrawer';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

function detail(overrides: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'pv-1',
    partyRole: null,
    name: 'Ana Ríos',
    documentType: 'CC',
    documentNumber: '1020304050',
    email: 'ana@example.com',
    status: 'en_proceso',
    intentos: 1,
    maxIntentos: 3,
    score: null,
    expiresAt: '2026-07-25T10:00:00Z',
    validatedAt: null,
    expired: false,
    provider: 'kyverum',
    captureUrl: '/api/v1/public/biometric/tok-1',
    rejectionReason: null,
    ultimoIntentoMotivo: null,
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getBiometricAuditByValidation.mockResolvedValue({
    validationId: 'pv-1',
    events: [],
    referencedFromOtherProcedure: false,
  });
});

afterEach(() => {
  vi.useRealTimers();
});

describe('PrevalidacionDetailDrawer (HU #11008)', () => {
  it('AC1 — carga el detalle por id y lo muestra al resolver', async () => {
    mocks.getPrevalidacionDetail.mockResolvedValueOnce(detail());

    render(<PrevalidacionDetailDrawer validationId="pv-1" onClose={() => {}} />);

    expect(mocks.getPrevalidacionDetail).toHaveBeenCalledWith('pv-1');
    expect(await screen.findByText('Ana Ríos')).toBeInTheDocument();
    expect(screen.getByText(/1 \/ 3/)).toBeInTheDocument();
  });

  it('AC6 — un error de carga muestra el estado de error sin datos parciales', async () => {
    mocks.getPrevalidacionDetail.mockRejectedValueOnce(new Error('Validación no encontrada.'));

    render(<PrevalidacionDetailDrawer validationId="pv-otro-tenant" onClose={() => {}} />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/no encontrada/i);
    expect(screen.queryByText('Ana Ríos')).toBeNull();
  });

  it('AC2 — refresca el detalle cada 5s mientras el estado no sea terminal', async () => {
    mocks.getPrevalidacionDetail.mockResolvedValue(detail());
    vi.useFakeTimers({ shouldAdvanceTime: true });

    await act(async () => {
      render(<PrevalidacionDetailDrawer validationId="pv-1" onClose={() => {}} />);
      await Promise.resolve();
    });
    expect(mocks.getPrevalidacionDetail).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(mocks.getPrevalidacionDetail).toHaveBeenCalledTimes(2);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(mocks.getPrevalidacionDetail).toHaveBeenCalledTimes(3);
  });

  it('AC3 — detiene el poll al llegar a un estado terminal (aprobado)', async () => {
    mocks.getPrevalidacionDetail.mockResolvedValue(
      detail({ status: 'aprobado', validatedAt: '2026-07-20T10:00:00Z' }),
    );
    vi.useFakeTimers({ shouldAdvanceTime: true });

    await act(async () => {
      render(<PrevalidacionDetailDrawer validationId="pv-1" onClose={() => {}} />);
      await Promise.resolve();
    });
    expect(mocks.getPrevalidacionDetail).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(15000);
    });

    expect(mocks.getPrevalidacionDetail).toHaveBeenCalledTimes(1);
  });

  it('AC4 — pausa el poll con la pestaña oculta (document.hidden) y lo reanuda al volver', async () => {
    mocks.getPrevalidacionDetail.mockResolvedValue(detail());
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const hiddenSpy = vi.spyOn(document, 'hidden', 'get');

    await act(async () => {
      render(<PrevalidacionDetailDrawer validationId="pv-1" onClose={() => {}} />);
      await Promise.resolve();
    });
    expect(mocks.getPrevalidacionDetail).toHaveBeenCalledTimes(1);

    // Pestaña oculta: el tick de 5s NO debe volver a consultar el endpoint.
    hiddenSpy.mockReturnValue(true);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(mocks.getPrevalidacionDetail).toHaveBeenCalledTimes(1);

    // Vuelve a primer plano: el siguiente tick retoma el poll con normalidad.
    hiddenSpy.mockReturnValue(false);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(mocks.getPrevalidacionDetail).toHaveBeenCalledTimes(2);

    hiddenSpy.mockRestore();
  });

  it('AC5 — embebe el tracking (IdentityValidationTrackingPanel) dentro del modal', async () => {
    mocks.getPrevalidacionDetail.mockResolvedValueOnce(detail());
    mocks.getBiometricAuditByValidation.mockResolvedValue({
      validationId: 'pv-1',
      events: [],
      referencedFromOtherProcedure: false,
    });

    render(<PrevalidacionDetailDrawer validationId="pv-1" onClose={() => {}} />);

    await screen.findByText('Ana Ríos');
    expect(screen.getByRole('button', { name: /ver tracking|tracking del proceso/i })).toBeInTheDocument();
  });

  it('HU #11069 — muestra trámites asociados (enlace, id y tipo) en el detalle', async () => {
    mocks.getPrevalidacionDetail.mockResolvedValueOnce(
      detail({
        procedureInstanceId: null,
        referenceNumber: null,
        modalidad: null,
        linkedProcedures: [
          {
            instanceId: 'inst-1',
            referenceNumber: 'TRM-2026-000006',
            status: 'borrador',
            modalidad: 'matricula_inicial',
          },
          {
            instanceId: 'inst-99',
            referenceNumber: 'TRM-2026-000099',
            status: 'preparado',
            modalidad: 'traspaso',
          },
        ],
      }),
    );

    render(<PrevalidacionDetailDrawer validationId="pv-1" onClose={() => {}} />);

    expect(await screen.findByRole('button', { name: /ver trámites asociados/i })).toBeInTheDocument();
    expect(screen.queryByText('TRM-2026-000006')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /ver trámites asociados/i }));

    const section = await screen.findByLabelText(/trámites asociados a esta validación/i);
    expect(within(section).getByText('TRM-2026-000006')).toBeInTheDocument();
    expect(within(section).getByText('TRM-2026-000099')).toBeInTheDocument();
    expect(within(section).getByRole('link', { name: /TRM-2026-000006/ })).toHaveAttribute(
      'href',
      '/tramites/inst-1',
    );
    expect(within(section).getByRole('link', { name: /TRM-2026-000099/ })).toHaveAttribute(
      'href',
      '/tramites/inst-99',
    );
    expect(within(section).getByText(/Matrícula inicial/i)).toBeInTheDocument();
    expect(within(section).getByText(/Traspaso/i)).toBeInTheDocument();
  });
});
