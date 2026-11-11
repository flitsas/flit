/**
 * «Actualizar estado» del detalle de persona (módulo Validaciones) consulta a Kyverum Verify
 * (plan docs/plan-boton-actualizar-estado-identidad.md §4 L2 + L3).
 *
 * Uso de ejemplo:
 *   <PersonIdentityDetailDrawer documentType="CC" documentNumber="1" canManage onClose={…} onStatusChanged={…} />
 *   Con `canManage`, sin red ni mandatario: el botón reconcilia las validaciones Kyverum no finales
 *   (POST /biometric-validations/{id}/reconcile) y luego recarga. Sin eso, solo recarga.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  listPersonBiometricValidations: vi.fn(),
  listNetworkPersonIdentityValidations: vi.fn(),
  getBiometricAuditByValidation: vi.fn(),
  getNetworkIdentityAudit: vi.fn(),
  reconcileBiometricValidation: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listPersonBiometricValidations: mocks.listPersonBiometricValidations,
    listNetworkPersonIdentityValidations: mocks.listNetworkPersonIdentityValidations,
    getBiometricAuditByValidation: mocks.getBiometricAuditByValidation,
    getNetworkIdentityAudit: mocks.getNetworkIdentityAudit,
    reconcileBiometricValidation: mocks.reconcileBiometricValidation,
  },
}));

import { PersonIdentityDetailDrawer } from '@/components/atom/modules/PersonIdentityDetailDrawer';
import type {
  BiometricValidation,
  PersonBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';

const NO_RESUELVE = 'El proveedor aún no resuelve la validación. Intenta de nuevo en unos segundos.';
const BOTON_PROVEEDOR = 'Actualizar estado consultando al proveedor';
const BOTON_RECARGA = 'Actualizar estado';

function validacion(overrides: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'v-1',
    partyRole: null,
    name: 'Persona de prueba',
    documentType: 'CC',
    documentNumber: '1000000001',
    email: 'prueba@example.com',
    status: 'enviado',
    intentos: 0,
    maxIntentos: 5,
    score: null,
    expiresAt: '2026-12-30T10:00:00Z',
    validatedAt: null,
    expired: false,
    provider: 'kyverum',
    captureUrl: null,
    procedureInstanceId: null,
    createdAt: '2026-11-05T09:00:00Z',
    ...overrides,
  };
}

function respuesta(validations: BiometricValidation[]): PersonBiometricValidationsResponse {
  return {
    documentType: 'CC',
    documentNumber: '1000000001',
    name: 'Persona de prueba',
    validations,
    page: 1,
    pageSize: 50,
    total: validations.length,
    allTerminal: validations.every((v) => ['aprobado', 'rechazado', 'expirado'].includes(v.status)),
  };
}

type Props = Partial<React.ComponentProps<typeof PersonIdentityDetailDrawer>>;

function renderDrawer(props: Props = {}) {
  return render(
    <PersonIdentityDetailDrawer documentType="CC" documentNumber="1000000001" onClose={() => {}} {...props} />,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getBiometricAuditByValidation.mockResolvedValue({ validationId: 'v-1', events: [], referencedFromOtherProcedure: false });
  mocks.getNetworkIdentityAudit.mockResolvedValue({ validationId: 'v-1', events: [], referencedFromOtherProcedure: false });
});

describe('L2 — «Actualizar estado» consulta a Verify (con permiso de gestionar)', () => {
  it('prevalidación pendiente: POST reconcile → recarga → fila aprobada y avisa al listado', async () => {
    mocks.listPersonBiometricValidations
      .mockResolvedValueOnce(respuesta([validacion({ id: 'v-1', status: 'enviado' })]))
      .mockResolvedValue(respuesta([validacion({ id: 'v-1', status: 'aprobado', validatedAt: '2026-11-10T10:00:00Z' })]));
    mocks.reconcileBiometricValidation.mockResolvedValue({ status: 'aprobado', updated: true });
    const onStatusChanged = vi.fn();
    const user = userEvent.setup();
    renderDrawer({ canManage: true, onStatusChanged });

    await user.click(await screen.findByRole('button', { name: BOTON_PROVEEDOR }));

    await waitFor(() => expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(2));
    expect(mocks.reconcileBiometricValidation).toHaveBeenCalledTimes(1);
    expect(mocks.reconcileBiometricValidation.mock.calls[0][0]).toBe('v-1');
    // El POST va antes de la recarga.
    expect(mocks.reconcileBiometricValidation.mock.invocationCallOrder[0]).toBeLessThan(
      mocks.listPersonBiometricValidations.mock.invocationCallOrder[1],
    );
    expect(await screen.findByLabelText('Estado de la última validación: Aprobado')).toBeInTheDocument();
    expect(onStatusChanged).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Estado actualizado.')).toBeInTheDocument();
    expect(screen.getByText('Estado actualizado.').closest('[role="status"]')).not.toBeNull();
  });

  it('varias abiertas: reconcilia solo kyverum enviado/en_proceso (no pendiente_envio), en orden, y muestra un mensaje agregado', async () => {
    const lista = [
      validacion({ id: 'k-enviado', status: 'enviado', createdAt: '2026-11-09T10:00:00Z' }),
      validacion({ id: 'k-proceso', status: 'en_proceso', createdAt: '2026-11-08T10:00:00Z' }),
      validacion({ id: 'k-aprobado', status: 'aprobado', createdAt: '2026-11-07T10:00:00Z' }),
      validacion({ id: 'mock-enviado', status: 'enviado', provider: 'mock', createdAt: '2026-11-06T10:00:00Z' }),
      validacion({ id: 'k-cola', status: 'pendiente_envio', createdAt: '2026-11-05T10:00:00Z' }),
      validacion({ id: 'k-rechazado', status: 'rechazado', createdAt: '2026-11-04T10:00:00Z' }),
      validacion({ id: 'k-vencido', status: 'enviado', expired: true, createdAt: '2026-11-03T10:00:00Z' }),
      validacion({ id: 'manual-proceso', status: 'en_proceso', provider: 'manual', createdAt: '2026-11-02T10:00:00Z' }),
    ];
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta(lista));
    mocks.reconcileBiometricValidation.mockImplementation(async (id: string) => {
      if (id === 'k-proceso') throw new Error('El proveedor no respondió.');
      return { status: id === 'k-enviado' ? 'aprobado' : 'enviado', updated: id === 'k-enviado' };
    });
    const user = userEvent.setup();
    renderDrawer({ canManage: true });

    await user.click(await screen.findByRole('button', { name: BOTON_PROVEEDOR }));

    await waitFor(() => expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(2));
    expect(mocks.reconcileBiometricValidation.mock.calls.map((c) => c[0])).toEqual([
      'k-enviado',
      'k-proceso',
    ]);
    expect(
      await screen.findByText('Estado actualizado. Alguna validación no se pudo consultar al proveedor.'),
    ).toBeInTheDocument();
  });

  it('nada que reconciliar (todo final o mock): solo recarga, sin llamar a Verify', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(
      respuesta([
        validacion({ id: 'a', status: 'aprobado' }),
        validacion({ id: 'm', status: 'enviado', provider: 'mock' }),
      ]),
    );
    const user = userEvent.setup();
    renderDrawer({ canManage: true });

    // Sin candidatas, el nombre accesible no promete una consulta al proveedor.
    await user.click(await screen.findByRole('button', { name: BOTON_RECARGA }));

    await waitFor(() => expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(2));
    expect(mocks.reconcileBiometricValidation).not.toHaveBeenCalled();
  });

  it('solo pendiente_envio (sin id del proveedor todavía): solo recarga, sin llamar a Verify', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(
      respuesta([validacion({ id: 'k-cola', status: 'pendiente_envio' })]),
    );
    const user = userEvent.setup();
    renderDrawer({ canManage: true });

    await user.click(await screen.findByRole('button', { name: BOTON_RECARGA }));

    await waitFor(() => expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(2));
    expect(mocks.reconcileBiometricValidation).not.toHaveBeenCalled();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('envía el tenant de la persona (SuperAdmin mirando otra compañía)', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta([validacion({ id: 'v-1', status: 'enviado' })]));
    mocks.reconcileBiometricValidation.mockResolvedValue({ status: 'enviado', updated: false });
    const user = userEvent.setup();
    renderDrawer({ canManage: true, tenantId: 'compania-b' });

    await user.click(await screen.findByRole('button', { name: BOTON_PROVEEDOR }));

    await waitFor(() => expect(mocks.reconcileBiometricValidation).toHaveBeenCalledWith('v-1', 'compania-b'));
  });

  it('doble clic en el mismo tick: una sola tanda de consultas', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta([validacion({ id: 'v-1', status: 'enviado' })]));
    let resolver: (v: { status: string; updated: boolean }) => void = () => {};
    mocks.reconcileBiometricValidation.mockImplementation(() => new Promise((r) => { resolver = r; }));
    renderDrawer({ canManage: true });

    const boton = await screen.findByRole('button', { name: BOTON_PROVEEDOR });
    act(() => {
      boton.click();
      boton.click();
    });

    expect(mocks.reconcileBiometricValidation).toHaveBeenCalledTimes(1);
    await act(async () => {
      resolver({ status: 'enviado', updated: false });
    });
    await waitFor(() => expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(2));
    expect(mocks.reconcileBiometricValidation).toHaveBeenCalledTimes(1);
  });

  it('el polling de 5 s no recarga mientras la consulta al proveedor está en vuelo', async () => {
    vi.useFakeTimers();
    const vis = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    try {
      mocks.listPersonBiometricValidations.mockResolvedValue(respuesta([validacion({ id: 'v-1', status: 'enviado' })]));
      let resolver: (v: { status: string; updated: boolean }) => void = () => {};
      mocks.reconcileBiometricValidation.mockImplementation(() => new Promise((r) => { resolver = r; }));
      renderDrawer({ canManage: true });
      await act(async () => { await vi.advanceTimersByTimeAsync(0); });
      expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(1);

      act(() => {
        screen.getByRole('button', { name: BOTON_PROVEEDOR }).click();
      });
      await act(async () => { await vi.advanceTimersByTimeAsync(11000); });
      expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(1);

      await act(async () => {
        resolver({ status: 'enviado', updated: false });
        await vi.advanceTimersByTimeAsync(0);
      });
      // Al terminar, la recarga del propio botón.
      expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(2);
    } finally {
      vis.mockRestore();
      vi.useRealTimers();
    }
  });

  it('Verify aún no resuelve: mensaje informativo en role="status"', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta([validacion({ status: 'en_proceso' })]));
    mocks.reconcileBiometricValidation.mockResolvedValue({ status: 'en_proceso', updated: false });
    const onStatusChanged = vi.fn();
    const user = userEvent.setup();
    renderDrawer({ canManage: true, onStatusChanged });

    await user.click(await screen.findByRole('button', { name: BOTON_PROVEEDOR }));

    const msg = await screen.findByText(NO_RESUELVE);
    expect(msg.closest('[role="status"]')).not.toBeNull();
    expect(onStatusChanged).not.toHaveBeenCalled();
  });

  it('error del proveedor: muestra el detail en role="alert"; sin detail, el texto por defecto', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta([validacion({ status: 'enviado' })]));
    mocks.reconcileBiometricValidation.mockRejectedValueOnce(new Error('El proveedor de identidad no está disponible.'));
    const user = userEvent.setup();
    renderDrawer({ canManage: true });

    await user.click(await screen.findByRole('button', { name: BOTON_PROVEEDOR }));
    const alerta = await screen.findByRole('alert');
    expect(within(alerta).getByText('El proveedor de identidad no está disponible.')).toBeInTheDocument();

    mocks.reconcileBiometricValidation.mockRejectedValueOnce('sin detalle');
    await user.click(await screen.findByRole('button', { name: BOTON_PROVEEDOR }));
    expect(await screen.findByText('No se pudo actualizar el estado.')).toBeInTheDocument();
  });

  it('mientras consulta: deshabilitado y con aria-busy', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta([validacion({ status: 'enviado' })]));
    let resolver: (v: { status: string; updated: boolean }) => void = () => {};
    mocks.reconcileBiometricValidation.mockImplementation(() => new Promise((r) => { resolver = r; }));
    const user = userEvent.setup();
    renderDrawer({ canManage: true });

    const boton = await screen.findByRole('button', { name: BOTON_PROVEEDOR });
    await user.click(boton);
    await waitFor(() => expect(boton).toBeDisabled());
    expect(boton).toHaveAttribute('aria-busy', 'true');

    resolver({ status: 'enviado', updated: false });
    await waitFor(() => expect(boton).not.toBeDisabled());
    expect(boton).toHaveAttribute('aria-busy', 'false');
  });
});

describe('L2 — casos que solo recargan (no llaman a la ruta de escritura)', () => {
  it('vista de red: recarga por la ruta de red y no llama al POST', async () => {
    mocks.listNetworkPersonIdentityValidations.mockResolvedValue(respuesta([validacion({ status: 'enviado' })]));
    const user = userEvent.setup();
    renderDrawer({ canManage: true, networkTenantId: 'hija-1' });

    await user.click(await screen.findByRole('button', { name: BOTON_RECARGA }));

    await waitFor(() => expect(mocks.listNetworkPersonIdentityValidations).toHaveBeenCalledTimes(2));
    expect(mocks.reconcileBiometricValidation).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: BOTON_PROVEEDOR })).not.toBeInTheDocument();
  });

  it('mandatario: solo recarga', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta([validacion({ status: 'enviado' })]));
    const user = userEvent.setup();
    renderDrawer({ canManage: true, mandatario: true });

    await user.click(await screen.findByRole('button', { name: BOTON_RECARGA }));

    await waitFor(() => expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(2));
    expect(mocks.reconcileBiometricValidation).not.toHaveBeenCalled();
  });

  it('sin permiso de gestionar: solo recarga', async () => {
    mocks.listPersonBiometricValidations.mockResolvedValue(respuesta([validacion({ status: 'enviado' })]));
    const user = userEvent.setup();
    renderDrawer();

    await user.click(await screen.findByRole('button', { name: BOTON_RECARGA }));

    await waitFor(() => expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(2));
    expect(mocks.reconcileBiometricValidation).not.toHaveBeenCalled();
  });
});

describe('L3 — error de recarga visible aunque haya datos', () => {
  it('muestra el aviso con los datos aún visibles y lo limpia al recargar bien', async () => {
    mocks.listPersonBiometricValidations
      .mockResolvedValueOnce(respuesta([validacion({ status: 'enviado' })]))
      .mockRejectedValueOnce(new Error('Servicio no disponible.'))
      .mockResolvedValue(respuesta([validacion({ status: 'enviado' })]));
    const user = userEvent.setup();
    renderDrawer();

    const boton = await screen.findByRole('button', { name: BOTON_RECARGA });
    await screen.findByLabelText('Estado de la última validación: Enviado');

    await user.click(boton);
    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent('El refresco automático se detuvo');
    expect(alerta).toHaveTextContent('Actualizar estado');
    expect(alerta).toHaveTextContent('Servicio no disponible.');
    // Los datos previos siguen a la vista.
    expect(screen.getByLabelText('Estado de la última validación: Enviado')).toBeInTheDocument();

    await user.click(boton);
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
    expect(mocks.listPersonBiometricValidations).toHaveBeenCalledTimes(3);
  });
});
