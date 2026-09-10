import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12163 — Dashboard de Trámites: menú de acciones avanzadas del administrador.
 *
 * AC1 — el menú de acciones de una fila ofrece las 5 acciones nuevas (Limpiar/Cargar consolidado
 * bajo "Gestionar consolidado", Cambiar estado, Anular, Reenviar validación, Reasignar gestor)
 * SOLO cuando el permiso correspondiente está en el JWT (o el usuario es SuperAdmin).
 * AC2 — sobre un trámite Aprobado, "Cambiar estado" y "Anular" llegan deshabilitadas con tooltip.
 * AC3 — Cambiar estado y Anular piden confirmación antes de aplicar; toda acción deja feedback
 * visible (toast) con el resultado.
 */

const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn().mockResolvedValue({}),
  listFilterFields: vi.fn().mockResolvedValue([]),
  listInstanceEstadoCounts: vi.fn().mockResolvedValue({}),
  getConsultationConfig: vi.fn(),
  setPriority: vi.fn(),
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  getInstance: vi.fn(),
  listBiometricExpediente: vi.fn(),
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  completePlateFlow: vi.fn(),
  // HU #12163 — las 5(6) acciones administrativas avanzadas.
  adminLimpiarConsolidado: vi.fn(),
  adminCargarConsolidado: vi.fn(),
  adminCambiarEstado: vi.fn(),
  adminAnular: vi.fn(),
  adminReenviarValidacionIdentidad: vi.fn(),
  adminReasignarGestor: vi.fn(),
  adminListGestoresDisponibles: vi.fn(),
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

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { ToastProvider } from '@/components/admin/Toast';

function renderTable() {
  return render(
    <ToastProvider>
      <TramitesTable />
    </ToastProvider>,
  );
}

/** Token de un usuario de compañía (no SuperAdmin) con el set de permisos indicado. */
function tokenConPermisos(permissions: string[]): string {
  const b64 = (o: unknown) =>
    btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64({ sub: 'user-1', role: 'AdminCompany', permissions })}.`;
}

function setToken(permissions: string[]): void {
  document.cookie = `flit_token=${tokenConPermisos(permissions)}; path=/`;
}

function clearToken(): void {
  document.cookie = 'flit_token=; path=/; Max-Age=0';
}

function makeInstance(over: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: 'inst-0001',
    referenceNumber: 'TR-0001',
    modalidad: 'TRASPASO',
    estado: 'borrador',
    placa: 'P0001',
    vin: 'VIN-0001',
    vehiculoMarca: 'Toyota',
    vehiculoLinea: 'Corolla',
    compradorNombre: 'Comprador 0001',
    compradorDocumento: '1000001',
    vendedorNombre: 'Vendedor 0001',
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
    ...over,
  } satisfies InstanceSummary;
}

async function abrirAcciones(referenceNumber = 'TR-0001') {
  await userEvent.click(
    screen.getByRole('button', { name: new RegExp(`Acciones del trámite ${referenceNumber}`) }),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.searchInstances.mockImplementation(async (params?: unknown) => {
    const items = (await mocks.listInstances(params)) ?? [];
    return { items, total: items.length };
  });
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
  mocks.listInstances.mockResolvedValue([makeInstance()]);
});

afterEach(() => {
  clearToken();
});

describe('HU #12163 — AC1: visibilidad por permiso', () => {
  it('sin ninguno de los 6 permisos, el menú NO ofrece ninguna de las acciones nuevas', async () => {
    setToken([]);
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();

    for (const label of [
      'Cambiar estado',
      'Anular',
      'Gestionar consolidado',
      'Reenviar validación',
      'Reasignar gestor',
    ]) {
      expect(screen.queryByRole('menuitem', { name: label })).not.toBeInTheDocument();
    }
    // El menú sigue teniendo sus acciones de siempre.
    expect(screen.getByRole('menuitem', { name: 'Ver documentos' })).toBeInTheDocument();
  });

  it('con un único permiso, solo aparece la acción correspondiente', async () => {
    setToken(['AdminTramiteCambiarEstado']);
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();

    expect(screen.getByRole('menuitem', { name: 'Cambiar estado' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Anular' })).not.toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Gestionar consolidado' })).not.toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Reenviar validación' })).not.toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Reasignar gestor' })).not.toBeInTheDocument();
  });

  it('con los 6 permisos, aparecen las 5 acciones (consolidado agrupa limpiar+cargar)', async () => {
    setToken([
      'AdminTramiteCambiarEstado',
      'AdminTramiteAnular',
      'AdminTramiteLimpiarConsolidado',
      'AdminTramiteCargarConsolidado',
      'AdminTramiteReenviarValidacion',
      'AdminTramiteReasignarGestor',
    ]);
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();

    expect(screen.getByRole('menuitem', { name: 'Cambiar estado' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Anular' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Gestionar consolidado' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Reenviar validación' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Reasignar gestor' })).toBeInTheDocument();
  });

  it('con solo un permiso de consolidado, "Gestionar consolidado" igual aparece', async () => {
    setToken(['AdminTramiteCargarConsolidado']);
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();

    expect(screen.getByRole('menuitem', { name: 'Gestionar consolidado' })).toBeInTheDocument();
  });
});

describe('HU #12163 — AC2: Aprobado deshabilita Cambiar estado y Anular', () => {
  const PERMISOS_COMPLETOS = [
    'AdminTramiteCambiarEstado',
    'AdminTramiteAnular',
    'AdminTramiteLimpiarConsolidado',
    'AdminTramiteCargarConsolidado',
    'AdminTramiteReenviarValidacion',
    'AdminTramiteReasignarGestor',
  ];

  it('en un trámite Aprobado, "Cambiar estado" y "Anular" están deshabilitadas con motivo', async () => {
    setToken(PERMISOS_COMPLETOS);
    mocks.listInstances.mockResolvedValue([makeInstance({ estado: 'aprobado' })]);
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();

    const cambiarEstado = screen.getByRole('menuitem', { name: 'Cambiar estado' });
    const anular = screen.getByRole('menuitem', { name: 'Anular' });
    expect(cambiarEstado).toBeDisabled();
    expect(anular).toBeDisabled();
    expect(cambiarEstado).toHaveAttribute(
      'title',
      'Un trámite Aprobado no admite cambio de estado administrativo.',
    );
    expect(anular).toHaveAttribute('title', 'Un trámite Aprobado no se puede anular.');

    // El resto de acciones nuevas NO se deshabilitan por estar en Aprobado.
    expect(screen.getByRole('menuitem', { name: 'Gestionar consolidado' })).not.toBeDisabled();

    // Un clic sobre un ítem deshabilitado no dispara ninguna llamada al backend.
    await userEvent.click(cambiarEstado);
    expect(mocks.adminCambiarEstado).not.toHaveBeenCalled();
  });

  it('fuera de Aprobado, ambas acciones están habilitadas', async () => {
    setToken(PERMISOS_COMPLETOS);
    mocks.listInstances.mockResolvedValue([makeInstance({ estado: 'entregado' })]);
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();

    expect(screen.getByRole('menuitem', { name: 'Cambiar estado' })).not.toBeDisabled();
    expect(screen.getByRole('menuitem', { name: 'Anular' })).not.toBeDisabled();
  });
});

// Bug #12376, defecto 2 — un trámite ya Anulado no debe poder seleccionarse de nuevo para anular
// (el backend ahora lo rechaza con CANNOT_ANNUL_ALREADY; el frontend evita el viaje redondo).
describe('HU #12163 / Bug #12376 — Anulado deshabilita "Anular"', () => {
  it('en un trámite ya Anulado, "Anular" está deshabilitada con motivo', async () => {
    setToken(['AdminTramiteAnular']);
    mocks.listInstances.mockResolvedValue([makeInstance({ estado: 'anulado' })]);
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();

    const anular = screen.getByRole('menuitem', { name: 'Anular' });
    expect(anular).toBeDisabled();
    expect(anular).toHaveAttribute('title', 'Este trámite ya está Anulado.');

    await userEvent.click(anular);
    expect(mocks.adminAnular).not.toHaveBeenCalled();
  });
});

describe('HU #12163 — AC3: Cambiar estado (confirmación + feedback)', () => {
  beforeEach(() => {
    setToken(['AdminTramiteCambiarEstado']);
  });

  it('pide confirmación en un modal antes de aplicar y muestra el resultado', async () => {
    mocks.listInstances.mockResolvedValue([makeInstance({ estado: 'entregado' })]);
    mocks.adminCambiarEstado.mockResolvedValue({
      id: 'inst-0001',
      previousStatus: 'entregado',
      newStatus: 'preparado',
      changedAt: '2026-09-08T10:00:00Z',
    });
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Cambiar estado' }));

    const dialog = await screen.findByRole('dialog', { name: /Cambiar estado/ });
    // El botón de confirmar empieza deshabilitado: no hay destino elegido todavía (AC3 —
    // la confirmación exige una elección explícita, no solo abrir el modal).
    expect(within(dialog).getByRole('button', { name: 'Confirmar cambio' })).toBeDisabled();

    await userEvent.selectOptions(within(dialog).getByLabelText('Nuevo estado'), 'preparado');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar cambio' }));

    expect(mocks.adminCambiarEstado).toHaveBeenCalledWith('inst-0001', 'preparado', null, undefined);
    // Feedback visible (toast) con el resultado — y el modal se cierra.
    expect(await screen.findByText('Estado cambiado a "Preparado".')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: /Cambiar estado/ })).not.toBeInTheDocument(),
    );
    // Refresca la tabla tras la mutación.
    await waitFor(() => expect(mocks.listInstances).toHaveBeenCalledTimes(2));
  });

  it('cancelar cierra el modal sin llamar al backend', async () => {
    mocks.listInstances.mockResolvedValue([makeInstance({ estado: 'entregado' })]);
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Cambiar estado' }));

    const dialog = await screen.findByRole('dialog', { name: /Cambiar estado/ });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    expect(screen.queryByRole('dialog', { name: /Cambiar estado/ })).not.toBeInTheDocument();
    expect(mocks.adminCambiarEstado).not.toHaveBeenCalled();
  });

  it('si el backend rechaza, muestra el error dentro del modal y como toast, sin cerrarlo', async () => {
    mocks.listInstances.mockResolvedValue([makeInstance({ estado: 'entregado' })]);
    mocks.adminCambiarEstado.mockRejectedValue(new Error('El trámite fue modificado por otro proceso.'));
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Cambiar estado' }));

    const dialog = await screen.findByRole('dialog', { name: /Cambiar estado/ });
    await userEvent.selectOptions(within(dialog).getByLabelText('Nuevo estado'), 'rechazado');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar cambio' }));

    // Un mensaje dentro del modal + un toast: los dos son "feedback visible" (AC3), no uno solo.
    expect(
      await within(dialog).findByText('El trámite fue modificado por otro proceso.'),
    ).toBeInTheDocument();
    expect(screen.getAllByText('El trámite fue modificado por otro proceso.').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByRole('dialog', { name: /Cambiar estado/ })).toBeInTheDocument();
  });
});

describe('HU #12163 — AC3: Anular (confirmación destructiva + feedback)', () => {
  beforeEach(() => {
    setToken(['AdminTramiteAnular']);
  });

  it('confirma antes de anular y muestra el resultado', async () => {
    mocks.listInstances.mockResolvedValue([makeInstance({ estado: 'entregado' })]);
    mocks.adminAnular.mockResolvedValue({
      id: 'inst-0001',
      previousStatus: 'entregado',
      newStatus: 'anulado',
      changedAt: '2026-09-08T10:00:00Z',
    });
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Anular' }));

    const dialog = await screen.findByRole('dialog', { name: /Anular trámite/ });
    await userEvent.type(
      within(dialog).getByLabelText('Motivo (opcional)'),
      'Radicación duplicada',
    );
    await userEvent.click(within(dialog).getByRole('button', { name: 'Anular trámite' }));

    expect(mocks.adminAnular).toHaveBeenCalledWith('inst-0001', 'Radicación duplicada', undefined);
    expect(await screen.findByText('Trámite TR-0001 anulado.')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: /Anular trámite/ })).not.toBeInTheDocument(),
    );
  });
});

describe('HU #12163 — Gestionar consolidado (Limpiar/Cargar)', () => {
  it('limpiar consolidado llama al endpoint y muestra feedback', async () => {
    setToken(['AdminTramiteLimpiarConsolidado']);
    mocks.listInstances.mockResolvedValue([makeInstance()]);
    mocks.adminLimpiarConsolidado.mockResolvedValue({ id: 'att-1' });
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Gestionar consolidado' }));

    const dialog = await screen.findByRole('dialog', { name: /Gestionar consolidado/ });
    // Sin el permiso de cargar, esa sección no aparece.
    expect(within(dialog).queryByText('Cargar consolidado')).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Regenerar consolidado' }));

    expect(mocks.adminLimpiarConsolidado).toHaveBeenCalledWith('inst-0001', undefined);
    expect(await screen.findByText('Consolidado regenerado.')).toBeInTheDocument();
  });

  it('cargar consolidado exige un archivo antes de habilitar el botón', async () => {
    setToken(['AdminTramiteCargarConsolidado']);
    mocks.listInstances.mockResolvedValue([makeInstance()]);
    mocks.adminCargarConsolidado.mockResolvedValue({ id: 'att-2' });
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Gestionar consolidado' }));

    const dialog = await screen.findByRole('dialog', { name: /Gestionar consolidado/ });
    expect(within(dialog).queryByText('Limpiar consolidado')).not.toBeInTheDocument();
    const cargarBtn = within(dialog).getByRole('button', { name: 'Cargar PDF' });
    expect(cargarBtn).toBeDisabled();

    const file = new File(['%PDF-1.4'], 'consolidado.pdf', { type: 'application/pdf' });
    const input = dialog.querySelector('input[type="file"]') as HTMLInputElement;
    await userEvent.upload(input, file);
    expect(cargarBtn).not.toBeDisabled();

    await userEvent.click(cargarBtn);
    expect(mocks.adminCargarConsolidado).toHaveBeenCalledWith('inst-0001', file, undefined);
    expect(await screen.findByText('Consolidado cargado.')).toBeInTheDocument();
  });
});

describe('HU #12163 — Reenviar validación de identidad', () => {
  beforeEach(() => {
    setToken(['AdminTramiteReenviarValidacion']);
  });

  it('sin validaciones registradas muestra el estado vacío (no ofrece reenviar)', async () => {
    mocks.listInstances.mockResolvedValue([makeInstance()]);
    mocks.listBiometricExpediente.mockResolvedValue({ validations: [], firmaBaulPartes: [] });
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Reenviar validación' }));

    const dialog = await screen.findByRole('dialog', { name: /Reenviar validación/ });
    expect(
      await within(dialog).findByText('Este trámite no tiene validaciones de identidad registradas.'),
    ).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Reenviar' })).not.toBeInTheDocument();
  });

  it('con una validación disponible, reenvía y muestra el resultado', async () => {
    mocks.listInstances.mockResolvedValue([makeInstance()]);
    mocks.listBiometricExpediente.mockResolvedValue({
      validations: [
        {
          id: 'val-1',
          partyRole: 'comprador',
          name: 'Juan Pérez',
          documentType: 'CC',
          documentNumber: '123',
          email: 'juan@example.com',
          status: 'entregado',
          intentos: 1,
          maxIntentos: 3,
          score: null,
          expiresAt: '2026-09-09T00:00:00Z',
          validatedAt: null,
          expired: false,
          provider: 'kyverum',
          captureUrl: null,
        },
      ],
      firmaBaulPartes: [],
    });
    mocks.adminReenviarValidacionIdentidad.mockResolvedValue({
      validation: {},
      captureUrl: 'https://kyverum.local/x',
      emailActualizado: false,
      queued: false,
    });
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Reenviar validación' }));

    const dialog = await screen.findByRole('dialog', { name: /Reenviar validación/ });
    // Con una sola validación (no traspaso) no hay nada que elegir: sin selector, se reenvía directo.
    expect(within(dialog).queryByLabelText('Validación a reenviar')).not.toBeInTheDocument();
    // Pero sí se ve a qué correo se reenviará.
    expect(within(dialog).getByText(/Correo registrado:/)).toBeInTheDocument();
    expect(within(dialog).getByText('juan@example.com')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Reenviar' }));

    expect(mocks.adminReenviarValidacionIdentidad).toHaveBeenCalledWith(
      'inst-0001',
      'val-1',
      null,
      undefined,
    );
    expect(await screen.findByText('Validación de identidad reenviada.')).toBeInTheDocument();
  });

  it('en traspaso (2 validaciones) sí muestra el selector, con rol y nombre completos', async () => {
    mocks.listInstances.mockResolvedValue([makeInstance()]);
    mocks.listBiometricExpediente.mockResolvedValue({
      validations: [
        {
          id: 'val-vendedor',
          partyRole: 'vendedor',
          name: 'Ana Gómez',
          documentType: 'CC',
          documentNumber: '111',
          email: 'ana@example.com',
          status: 'entregado',
          intentos: 1,
          maxIntentos: 3,
          score: null,
          expiresAt: '2026-09-09T00:00:00Z',
          validatedAt: null,
          expired: false,
          provider: 'kyverum',
          captureUrl: null,
        },
        {
          id: 'val-comprador',
          partyRole: 'comprador',
          name: 'Juan Pérez',
          documentType: 'CC',
          documentNumber: '123',
          email: 'juan@example.com',
          status: 'entregado',
          intentos: 1,
          maxIntentos: 3,
          score: null,
          expiresAt: '2026-09-09T00:00:00Z',
          validatedAt: null,
          expired: false,
          provider: 'kyverum',
          captureUrl: null,
        },
      ],
      firmaBaulPartes: [],
    });
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Reenviar validación' }));

    const dialog = await screen.findByRole('dialog', { name: /Reenviar validación/ });
    const select = await within(dialog).findByLabelText('Validación a reenviar');
    // Rol + nombre completos, sin el correo (lo que causaba el recorte visual reportado).
    expect(within(select).getByRole('option', { name: 'Vendedor · Ana Gómez' })).toBeInTheDocument();
    expect(within(select).getByRole('option', { name: 'Comprador · Juan Pérez' })).toBeInTheDocument();
    expect(within(dialog).getByText(/Correo registrado:/)).toBeInTheDocument();
  });
});

describe('HU #12163 — Reasignar gestor', () => {
  beforeEach(() => {
    setToken(['AdminTramiteReasignarGestor']);
  });

  it('sin gestores disponibles muestra el estado vacío', async () => {
    mocks.listInstances.mockResolvedValue([makeInstance()]);
    mocks.adminListGestoresDisponibles.mockResolvedValue([]);
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Reasignar gestor' }));

    const dialog = await screen.findByRole('dialog', { name: /Reasignar gestor/ });
    expect(
      await within(dialog).findByText('No hay gestores disponibles en este tenant.'),
    ).toBeInTheDocument();
  });

  it('reasigna al gestor elegido y muestra el resultado', async () => {
    mocks.listInstances.mockResolvedValue([makeInstance()]);
    mocks.adminListGestoresDisponibles.mockResolvedValue([
      { id: 'g-1', displayName: 'Ana Gestora', email: 'ana@example.com' },
      { id: 'g-2', displayName: 'Beto Gestor', email: 'beto@example.com' },
    ]);
    mocks.adminReasignarGestor.mockResolvedValue({
      id: 'inst-0001',
      previousAssignedToUserId: null,
      newAssignedToUserId: 'g-2',
      changedAt: '2026-09-08T10:00:00Z',
    });
    renderTable();
    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Reasignar gestor' }));

    const dialog = await screen.findByRole('dialog', { name: /Reasignar gestor/ });
    await userEvent.selectOptions(within(dialog).getByLabelText('Nuevo gestor'), 'g-2');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Reasignar' }));

    expect(mocks.adminReasignarGestor).toHaveBeenCalledWith('inst-0001', 'g-2', undefined);
    expect(await screen.findByText('Gestor reasignado.')).toBeInTheDocument();
  });
});
