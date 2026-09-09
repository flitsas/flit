// Tests unitarios de la HU #12196 (Feature #12189) — acceso al historial por placa desde el
// listado operativo de trámites («Dashboard de Trámites»).
//
// Cubre los criterios de la HU:
//   AC1 — la acción "Ver historial de la placa" aparece en la fila cuando el usuario tiene acceso
//         al módulo `historial-placa`, y NO aparece cuando no lo tiene (brecha G6 del plan).
//   AC2 — al activarla se navega al módulo dedicado con la placa de ESA fila precargada, y el
//         módulo muestra la misma información que si se hubiese escrito la placa a mano.
//   AC3 — una fila sin placa (borrador que aún no la tiene) no ofrece la acción: se pinta
//         deshabilitada con motivo y no navega a una búsqueda vacía.
//   AC4 — la consulta del historial NO lleva tenant: ni en la URL de entrada ni en la llamada al
//         cliente. El alcance lo decide el servidor por rol (D1); acotarlo al tenant de la fila le
//         escondería al SuperAdmin justo los trámites de otras compañías.
//
// Uso de ejemplo:
//   render(<TramitesTable />) con `historial-placa` en los módulos accesibles → el menú de acciones
//   de la fila ofrece "Ver historial de la placa" y empuja `/?m=historial-placa&placa=ABC123`.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

// ── Cliente HTTP: sin red real ─────────────────────────────────────────────
const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  listFilterFields: vi.fn(),
  listInstanceEstadoCounts: vi.fn().mockResolvedValue({}),
  setPriority: vi.fn(),
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  getInstance: vi.fn(),
  listBiometricExpediente: vi.fn(),
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  completePlateFlow: vi.fn(),
  getConsultationConfig: vi.fn(),
  // AC4 — el historial tiene su propio método, sin parámetro de tenant en la firma.
  listPlateHistory: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: {} }),
    put: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: { visible: [] } }),
  },
}));

const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
}));

/**
 * Se mockea la LECTURA de módulos RBAC (la petición), no la REGLA: `useNavigableModules` y
 * `resolveNavigableModuleIds` corren de verdad, que es justo lo que hay que verificar — que el
 * atajo use el mismo gate que la navegación y no una segunda fuente de verdad.
 */
const rbac = vi.hoisted(() => ({ codes: [] as string[] }));
vi.mock('@/hooks/useAccessibleModules', () => ({
  useAccessibleModules: () => ({
    modules: rbac.codes.map((code, i) => ({
      id: `mod-${i}`,
      code,
      name: code,
      sortOrder: i,
      actions: [],
    })),
    loading: false,
    error: null,
    ready: true,
  }),
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { HistorialPlaca } from '@/components/atom/modules/HistorialPlaca';

const ACCION = /Ver historial de la placa/i;

function instancia(overrides: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: 'inst-0001',
    referenceNumber: 'TR-0001',
    modalidad: 'TRASPASO',
    estado: 'borrador',
    placa: 'ABC123',
    vin: 'VIN-0001',
    vehiculoMarca: 'Toyota',
    vehiculoLinea: 'Corolla',
    compradorNombre: 'Comprador Uno',
    compradorDocumento: '1000001',
    vendedorNombre: 'Vendedor Uno',
    vendedorDocumento: '2000001',
    organismoTransito: null,
    pasoActual: 2,
    totalPasos: 6,
    createdAt: '2026-06-18T00:00:00Z',
    draftFinalizedAt: null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: false,
    prioritario: false,
    tenantId: '11111111-1111-1111-1111-111111111111',
    companiaNombre: null,
    subsanacionActiva: false,
    subsanacionCount: 0,
    ultimoRechazoMotivo: null,
    updatedAt: null,
    gestorNombre: null,
    fuente: 'dashboard',
    firmaVendedorEstado: 'pendiente',
    firmaCompradorEstado: 'pendiente',
    consolidadoAttachmentId: null,
    ...overrides,
  } as InstanceSummary;
}

/** Abre el menú "Acciones" de una fila (desde HU #11037 las acciones viven en un dropdown). */
async function abrirAcciones(referenceNumber = 'TR-0001') {
  await userEvent.click(
    screen.getByRole('button', { name: new RegExp(`Acciones del trámite ${referenceNumber}`) }),
  );
}

async function renderListado(items: InstanceSummary[]) {
  mocks.listInstances.mockResolvedValue(items);
  render(<TramitesTable />);
  await screen.findByText(items[0].referenceNumber);
}

beforeEach(() => {
  vi.clearAllMocks();
  rbac.codes = ['tramites', 'historial-placa'];
  mocks.searchInstances.mockImplementation(async (params?: unknown) => {
    const items = (await mocks.listInstances(params)) ?? [];
    return { items, total: items.length };
  });
  mocks.searchEstadoCounts.mockImplementation((params?: unknown) =>
    mocks.listInstanceEstadoCounts(params),
  );
  mocks.listFilterFields.mockResolvedValue([]);
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
});

describe('HU #12196 — acción por fila hacia el historial de la placa', () => {
  it('AC1 — con acceso al módulo, la fila ofrece "Ver historial de la placa"', async () => {
    await renderListado([instancia()]);
    await abrirAcciones();

    const accion = screen.getByRole('menuitem', { name: ACCION });
    expect(accion).toBeInTheDocument();
    expect(accion).toBeEnabled();
  });

  it('AC1 — sin el permiso `historial-placa` la acción NO existe (el resto del menú sí)', async () => {
    rbac.codes = ['tramites'];
    await renderListado([instancia()]);
    await abrirAcciones();

    // El menú se abrió y tiene sus acciones habituales…
    expect(screen.getByRole('menuitem', { name: /Ver documentos/i })).toBeInTheDocument();
    // …pero el atajo al historial no se ofrece: no está oculto ni deshabilitado, no se pinta.
    expect(screen.queryByRole('menuitem', { name: ACCION })).not.toBeInTheDocument();
  });

  it('AC2/AC4 — activarla navega al módulo con la placa de ESA fila y sin tenant alguno', async () => {
    await renderListado([
      instancia({ id: 'inst-0001', referenceNumber: 'TR-0001', placa: 'ABC123' }),
      instancia({ id: 'inst-0002', referenceNumber: 'TR-0002', placa: 'XYZ789' }),
    ]);

    await abrirAcciones('TR-0002');
    await userEvent.click(screen.getByRole('menuitem', { name: ACCION }));

    expect(routerPush).toHaveBeenCalledTimes(1);
    const destino = routerPush.mock.calls[0][0] as string;
    // Placa de la fila activada, no la de la primera fila del listado.
    expect(destino).toBe('/?m=historial-placa&placa=XYZ789');
    // AC4 — ni `t=` (el parámetro con el que el listado abre un trámite de otra compañía) ni
    // ningún otro rastro de tenant viaja en la entrada al historial.
    const query = new URL(destino, 'http://localhost').searchParams;
    expect(query.get('t')).toBeNull();
    expect(destino.toLowerCase()).not.toContain('tenant');
  });

  it('AC3 — una fila sin placa no ofrece la acción: deshabilitada, con motivo y sin navegar', async () => {
    await renderListado([instancia({ placa: null })]);
    await abrirAcciones();

    const accion = screen.getByRole('menuitem', { name: ACCION });
    expect(accion).toBeDisabled();
    expect(accion).toHaveAttribute('title', 'Este trámite todavía no tiene placa asignada.');

    await userEvent.click(accion);
    expect(routerPush).not.toHaveBeenCalled();
  });

  it('AC3 — una placa en blanco cuenta como sin placa (no navega a una búsqueda vacía)', async () => {
    await renderListado([instancia({ placa: '   ' })]);
    await abrirAcciones();

    expect(screen.getByRole('menuitem', { name: ACCION })).toBeDisabled();
    expect(routerPush).not.toHaveBeenCalled();
  });
});

describe('HU #12196 — el módulo recibe la placa precargada', () => {
  it('AC2 — con `initialPlaca` consulta sola y pinta el historial, sin que el usuario escriba', async () => {
    mocks.listPlateHistory.mockResolvedValue({
      items: [
        {
          ...instancia({ estado: 'aprobado' }),
          id: 'hist-1',
          referenceNumber: 'RAD-0009',
          placa: 'XYZ789',
        },
      ],
      total: 1,
    });

    render(<HistorialPlaca initialPlaca="XYZ789" />);

    expect(await screen.findByTestId('historial-placa-table')).toBeInTheDocument();
    expect(screen.getByText('RAD-0009')).toBeInTheDocument();
    // El buscador queda con la placa consultada: la pantalla es la misma que si se hubiera escrito.
    expect(screen.getByLabelText('Placa')).toHaveValue('XYZ789');
  });

  it('AC4 — la consulta del historial se hace SOLO con placa y paginación (sin tenant)', async () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [], total: 0 });

    render(<HistorialPlaca isSuperAdmin initialPlaca="abc123" />);

    await waitFor(() => expect(mocks.listPlateHistory).toHaveBeenCalledTimes(1));
    const args = mocks.listPlateHistory.mock.calls[0][0] as Record<string, unknown>;
    // Normalizada en el cliente y con la paginación del módulo; nada más.
    expect(args).toEqual({ placa: 'ABC123', skip: 0, take: 20 });
    expect(Object.keys(args).sort()).toEqual(['placa', 'skip', 'take']);
    // Ni siquiera para el SuperAdmin, que es quien podría "ayudar" mandando el tenant de la fila.
    expect(JSON.stringify(args).toLowerCase()).not.toContain('tenant');
    // Y no se cuela por el listado genérico, que sí admite `filterTenantId`.
    expect(mocks.listInstances).not.toHaveBeenCalled();
    expect(mocks.searchInstances).not.toHaveBeenCalled();
  });

  it('AC2 — sin `initialPlaca` no consulta nada: el módulo arranca en su estado inicial', async () => {
    render(<HistorialPlaca />);

    expect(screen.getByTestId('historial-placa-idle')).toBeInTheDocument();
    expect(mocks.listPlateHistory).not.toHaveBeenCalled();
  });
});
