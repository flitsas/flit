import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, within } from '@testing-library/react';

import type {
  TenantBiometricValidation,
  TenantBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listTenantBiometricValidations: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listTenantBiometricValidations: mocks.listTenantBiometricValidations,
  },
}));

import { Validaciones } from '@/components/atom/modules/Validaciones';

const ROW_APROBADA: TenantBiometricValidation = {
  id: 'v-1',
  instanceId: 'inst-1',
  referenceNumber: 'TRM-2026-000001',
  modalidad: 'traspaso',
  parte: 'comprador',
  nombre: 'Ana Compradora',
  tipoDoc: 'CC',
  documento: '1020304050',
  estado: 'aprobado',
  score: 95,
  provider: 'kyverum',
  expired: false,
  motivoRechazo: null,
  createdAt: '2026-06-20T15:30:00Z',
  validadoAt: '2026-06-20T15:40:00Z',
};

const ROW_RECHAZADA: TenantBiometricValidation = {
  id: 'v-2',
  instanceId: 'inst-2',
  referenceNumber: 'TRM-2026-000002',
  modalidad: 'matricula_inicial',
  parte: null,
  nombre: 'Luis Vendedor',
  tipoDoc: 'CC',
  documento: '7788',
  estado: 'rechazado',
  score: 30,
  provider: 'kyverum',
  expired: false,
  motivoRechazo: 'La verificación del documento no fue exitosa.',
  createdAt: '2026-06-21T10:00:00Z',
  validadoAt: null,
};

const FULL: TenantBiometricValidationsResponse = {
  validations: [ROW_APROBADA, ROW_RECHAZADA],
  stats: { total: 8, aprobadas: 3, enProceso: 3, rechazadas: 1, expiradas: 1 },
};

const EMPTY: TenantBiometricValidationsResponse = {
  validations: [],
  stats: { total: 0, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe('Validaciones — AC8 estados de UI', () => {
  it('Cargando: muestra el placeholder accesible antes de la primera respuesta', async () => {
    let resolveFn: (v: TenantBiometricValidationsResponse) => void = () => {};
    const pending = new Promise<TenantBiometricValidationsResponse>((r) => {
      resolveFn = r;
    });
    mocks.listTenantBiometricValidations.mockReturnValue(pending);

    render(<Validaciones />);

    // role="status" con el texto sr-only de carga.
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent(/cargando validaciones de identidad/i);

    await act(async () => {
      resolveFn(EMPTY);
      await pending;
    });
  });

  it('Vacío: muestra mensaje explícito cuando no hay validaciones', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(EMPTY);

    render(<Validaciones />);

    expect(await screen.findByText(/aún no hay validaciones de identidad/i)).toBeInTheDocument();
  });

  it('Error: muestra role="alert" con reintento cuando falla la carga', async () => {
    mocks.listTenantBiometricValidations.mockRejectedValue(new Error('500 Internal Server Error'));

    render(<Validaciones />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/no se pudieron cargar las validaciones/i);
    expect(within(alert).getByRole('button', { name: /reintentar/i })).toBeInTheDocument();
  });

  it('Lleno: pinta KPIs reales y una fila por validación', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);

    render(<Validaciones />);

    // KPIs reales (no mock).
    expect(await screen.findByText('TRM-2026-000001')).toBeInTheDocument();
    expect(screen.getByText('TRM-2026-000002')).toBeInTheDocument();

    // Una de las tarjetas KPI muestra el total real (8).
    expect(screen.getByText('Total validaciones')).toBeInTheDocument();
    expect(screen.getByText('8')).toBeInTheDocument();

    // Badges de estado.
    expect(screen.getByText('Aprobado')).toBeInTheDocument();
    expect(screen.getByText('Rechazado')).toBeInTheDocument();
  });
});

describe('Validaciones — datos y accesibilidad', () => {
  beforeEach(() => {
    mocks.listTenantBiometricValidations.mockResolvedValue(FULL);
  });

  it('cada fila enlaza al trámite de origen con aria-label descriptivo', async () => {
    render(<Validaciones />);

    const link = await screen.findByRole('link', { name: /validación de ana compradora/i });
    expect(link).toHaveAttribute('href', '/tramites/inst-1');
    expect(link.getAttribute('aria-label')).toMatch(/trámite trm-2026-000001/i);
  });

  it('enmascara el documento (no muestra el número completo)', async () => {
    render(<Validaciones />);

    // 1020304050 → ••••4050; el número completo NO aparece.
    expect(await screen.findByText('CC ••••4050')).toBeInTheDocument();
    expect(screen.queryByText(/1020304050/)).not.toBeInTheDocument();
  });

  it('muestra el motivo de rechazo sanitizado en la fila rechazada', async () => {
    render(<Validaciones />);

    expect(
      await screen.findByText('La verificación del documento no fue exitosa.'),
    ).toBeInTheDocument();
  });

  it('el botón Actualizar tiene nombre accesible', async () => {
    render(<Validaciones />);

    await screen.findByText('TRM-2026-000001');
    expect(
      screen.getByRole('button', { name: /actualizar validaciones de identidad/i }),
    ).toBeInTheDocument();
  });
});
