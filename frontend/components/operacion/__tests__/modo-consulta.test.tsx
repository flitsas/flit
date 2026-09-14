import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12362 — Vista consolidada: modo consulta transversal en el módulo de trámites.
 *
 * Uso de ejemplo: la cabeza de red (JWT `tenant_type: CONCESION`, `tenant_id: cabeza`) abre el
 * listado con una fila propia y una de un cliente hijo (`tenantId: hijo`, `fromNetwork: true`).
 * La fila del hijo no monta ningún control de escritura ni dispara ninguna mutación; la propia
 * sigue igual que hoy.
 *
 * AC1 — ningún control de mutación (prioridad, pausa, procesar, menú admin, asistente, masivas).
 * AC2 — nada dispara una escritura ni una actualización optimista.
 * AC3 — documentos: solo consulta; 403 ⇒ copy de «fuera de tu alcance», sin reintento.
 * AC4 — el trámite propio de la cabeza no cambia.
 * AC5 — un cliente sin jerarquía hace exactamente las mismas llamadas.
 * AC6 — selección mixta: acciones solo sobre propios + «N excluidos».
 * AC7 — enlace directo a `/tramites/{id}` de un hijo ⇒ detalle en consulta, sin escritura.
 * AC8 — tabla de casos enumerable, uno por punto de escritura.
 */

const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  searchNetworkInstances: vi.fn(),
  searchNetworkEstadoCounts: vi.fn(),
  getNetworkInstance: vi.fn(),
  listFilterFields: vi.fn(),
  getConsultationConfig: vi.fn(),
  setPriority: vi.fn(),
  getAttachments: vi.fn(),
  getChecklist: vi.fn(),
  getActors: vi.fn(),
  getCommercial: vi.fn(),
  getPrenda: vi.fn(),
  getPreflight: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  downloadBiometricCertificado: vi.fn(),
  getInstance: vi.fn(),
  listBiometricExpediente: vi.fn(),
  startSubsanacion: vi.fn(),
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  completePlateFlow: vi.fn(),
  adminLimpiarConsolidado: vi.fn(),
  adminCargarConsolidado: vi.fn(),
  adminCambiarEstado: vi.fn(),
  adminAnular: vi.fn(),
  adminReenviarValidacionIdentidad: vi.fn(),
  adminReasignarGestor: vi.fn(),
  adminListGestoresDisponibles: vi.fn(),
}));

/** Métodos de `tramitesClient` que ESCRIBEN: ninguno puede dispararse en modo consulta. */
const ESCRITURAS = [
  'setPriority',
  'pauseInstance',
  'pauseInstancesMassive',
  'completePlateFlow',
  'startSubsanacion',
  'adminLimpiarConsolidado',
  'adminCargarConsolidado',
  'adminCambiarEstado',
  'adminAnular',
  'adminReenviarValidacionIdentidad',
  'adminReasignarGestor',
] as const;

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  setActiveTramitesTenant: vi.fn(),
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: {} }),
    put: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: { visible: [] } }),
  },
}));

vi.mock('@/hooks/useAccessibleModules', () => ({
  useAccessibleModules: () => ({ modules: [], loading: false, ready: true, error: null }),
}));

const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
}));

// AC7 — el asistente se sustituye por un centinela: lo que importa es SI se monta, no qué pinta.
const wizardMounted = vi.hoisted(() => vi.fn());
vi.mock('@/components/operacion/TramiteWizard', () => ({
  TramiteWizard: (props: { existingInstanceId: string }) => {
    wizardMounted(props.existingInstanceId);
    return <div data-testid="tramite-wizard">asistente</div>;
  },
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';
import { TramiteDetalleModal } from '@/components/operacion/TramiteDetalleModal';
import { TramiteDetalleDocumentos } from '@/components/operacion/detalle/TramiteDetalleDocumentos';
import { ConsultaModeProvider } from '@/components/operacion/ConsultaModeContext';
import { TramiteInstanceGate } from '@/components/operacion/TramiteInstanceGate';
import { ToastProvider } from '@/components/admin/Toast';
import {
  COPY_DOCUMENTOS_FUERA_DE_ALCANCE,
  describirErrorDeSeccion,
  isNetworkReadOnly,
  partirSeleccionPorAlcance,
  textoExcluidosRed,
} from '@/lib/tramites/network-scope';

const CABEZA = '11111111-1111-1111-1111-111111111111';
const HIJO = '22222222-2222-2222-2222-222222222222';
const PERMISOS_ADMIN = [
  'AdminTramiteCambiarEstado',
  'AdminTramiteAnular',
  'AdminTramiteLimpiarConsolidado',
  'AdminTramiteCargarConsolidado',
  'AdminTramiteReenviarValidacion',
  'AdminTramiteReasignarGestor',
];

function b64(o: unknown): string {
  return btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function setToken(payload: Record<string, unknown>): void {
  const token = `${b64({ alg: 'none' })}.${b64(payload)}.`;
  document.cookie = `flit_token=${token}; path=/`;
}

/** Cabeza de red (CONCESION) con todos los permisos administrativos de trámites. */
function tokenCabeza(): void {
  setToken({
    sub: 'user-cabeza',
    role: 'AdminCompany',
    tenant_id: CABEZA,
    tenant_type: 'CONCESION',
    is_group_parent: true,
    permissions: PERMISOS_ADMIN,
  });
}

/** Cliente SIN jerarquía (ni padre ni hijos). */
function tokenSinJerarquia(): void {
  setToken({
    sub: 'user-solo',
    role: 'AdminCompany',
    tenant_id: CABEZA,
    tenant_type: 'B2B',
    permissions: PERMISOS_ADMIN,
  });
}

function clearToken(): void {
  document.cookie = 'flit_token=; path=/; Max-Age=0';
}

function makeInstance(over: Partial<InstanceSummary> & Record<string, unknown> = {}): InstanceSummary {
  return {
    id: 'inst-propio',
    referenceNumber: 'TR-PROPIO',
    modalidad: 'TRASPASO',
    estado: 'entregado',
    placa: 'AAA111',
    vin: 'VIN-PROPIO',
    vehiculoMarca: 'Toyota',
    vehiculoLinea: 'Corolla',
    compradorNombre: 'Comprador',
    compradorDocumento: '1000001',
    vendedorNombre: 'Vendedor',
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
    tenantId: CABEZA,
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
  } as InstanceSummary;
}

/** Fila de un cliente HIJO tal como la devuelve `searchNetworkInstances`. */
function makeRed(over: Partial<InstanceSummary> & Record<string, unknown> = {}): InstanceSummary {
  return makeInstance({
    id: 'inst-red',
    referenceNumber: 'TR-RED',
    placa: 'BBB222',
    vin: 'VIN-RED',
    tenantId: HIJO,
    tenantName: 'Concesionario Hijo SAS',
    fromNetwork: true,
    ...over,
  });
}

function renderTable(): ReturnType<typeof render> {
  return render(
    <ToastProvider>
      <TramitesTable />
    </ToastProvider>,
  );
}

async function abrirAcciones(referenceNumber: string): Promise<void> {
  await userEvent.click(
    screen.getByRole('button', { name: new RegExp(`Acciones del trámite ${referenceNumber}`) }),
  );
}

function menuItems(): string[] {
  return screen.getAllByRole('menuitem').map((el) => el.textContent?.trim() ?? '');
}

function escriturasDisparadas(): string[] {
  return ESCRITURAS.filter((k) => mocks[k].mock.calls.length > 0);
}

function llamadasHechas(): string[] {
  return (Object.keys(mocks) as (keyof typeof mocks)[])
    .filter((k) => mocks[k].mock.calls.length > 0)
    .sort();
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.searchInstances.mockImplementation(async () => {
    const items = (await mocks.listInstances()) ?? [];
    return { items, total: items.length };
  });
  mocks.listInstances.mockResolvedValue([makeInstance()]);
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.searchNetworkInstances.mockResolvedValue({ items: [], total: 0 });
  mocks.searchNetworkEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
  const detalle = {
    id: 'inst-red',
    referenceNumber: 'TR-RED',
    status: 'entregado',
    tenantId: HIJO,
    tenantName: 'Concesionario Hijo SAS',
    createdAt: '2026-06-18T00:00:00Z',
    statusHistory: [],
    fieldValues: [],
    actors: [],
  };
  mocks.getInstance.mockResolvedValue({ ...detalle, id: 'inst-propio', tenantId: CABEZA });
  mocks.getNetworkInstance.mockResolvedValue({ ...detalle, fromNetwork: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.getChecklist.mockResolvedValue({ items: [] });
  mocks.listBiometricExpediente.mockResolvedValue({
    validations: [],
    firmaBaulPartes: [],
    firmaBaulActores: [],
  });
  mocks.setPriority.mockResolvedValue(undefined);
  mocks.pauseInstance.mockResolvedValue(undefined);
  mocks.pauseInstancesMassive.mockResolvedValue(undefined);
  mocks.startSubsanacion.mockResolvedValue(undefined);
});

afterEach(() => {
  clearToken();
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — helper `isNetworkReadOnly` (fuente única del modo consulta)', () => {
  it('es falso para el trámite propio y verdadero para el de un hijo', () => {
    expect(isNetworkReadOnly({ tenantId: CABEZA }, CABEZA)).toBe(false);
    expect(isNetworkReadOnly({ tenantId: HIJO }, CABEZA)).toBe(true);
  });

  it('es verdadero por PROCEDENCIA aunque el tenant coincida (ruta network/**)', () => {
    expect(isNetworkReadOnly({ tenantId: CABEZA, fromNetwork: true }, CABEZA)).toBe(true);
  });

  it('sin tenant del usuario (SuperAdmin) nunca activa el modo consulta por tenant', () => {
    expect(isNetworkReadOnly({ tenantId: HIJO }, null)).toBe(false);
    expect(isNetworkReadOnly({ tenantId: HIJO }, undefined)).toBe(false);
  });

  it('ítem nulo o sin tenant ⇒ falso, sin lanzar', () => {
    expect(isNetworkReadOnly(null, CABEZA)).toBe(false);
    expect(isNetworkReadOnly({ tenantId: '' }, CABEZA)).toBe(false);
  });

  it('parte una selección mixta y redacta el texto de excluidos', () => {
    const items = [makeInstance(), makeRed()];
    const r = partirSeleccionPorAlcance(items, new Set(['inst-propio', 'inst-red']), CABEZA);
    expect(r.propios.map((i) => i.id)).toEqual(['inst-propio']);
    expect(r.excluidos).toBe(1);
    expect(textoExcluidosRed(1)).toBe('1 excluido: trámites de la red (solo consulta)');
    expect(textoExcluidosRed(2)).toBe('2 excluidos: trámites de la red (solo consulta)');
    expect(textoExcluidosRed(0)).toBeNull();
  });

  it('describe un 403 en modo consulta como fuera de alcance y como error técnico fuera de él', () => {
    const e403 = Object.assign(new Error('Forbidden'), { status: 403 });
    expect(describirErrorDeSeccion(e403, true, 'x', COPY_DOCUMENTOS_FUERA_DE_ALCANCE)).toEqual({
      mensaje: COPY_DOCUMENTOS_FUERA_DE_ALCANCE,
      fueraDeAlcance: true,
    });
    expect(describirErrorDeSeccion(e403, false, 'x')).toEqual({
      mensaje: 'Forbidden',
      fueraDeAlcance: false,
    });
    const e500 = Object.assign(new Error('Boom'), { status: 500 });
    expect(describirErrorDeSeccion(e500, true, 'x').fueraDeAlcance).toBe(false);
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — AC1/AC2/AC8: listado, un caso por punto de escritura', () => {
  /**
   * Tabla de casos (AC8): [acción, fila de la red que hoy la ofrecería, cómo se interactúa].
   * Cada caso verifica (a) que la acción NO se ofrece y (b) que NINGUNA escritura se dispara.
   */
  const casos: {
    accion: string;
    fila: InstanceSummary;
    noDebeExistir: () => void;
    interactuar: () => Promise<void>;
  }[] = [
    {
      accion: 'prioridad (estrella)',
      fila: makeRed(),
      noDebeExistir: () => {
        expect(
          screen.queryByRole('button', { name: /prioritario el trámite TR-RED/i }),
        ).not.toBeInTheDocument();
      },
      interactuar: async () => {
        // La estrella de la fila propia sí existe; la de la red no. Nada que pulsar.
      },
    },
    {
      accion: 'pausar / reanudar (ICT)',
      fila: makeRed({ estado: 'borrador', origin: 'ict', isPaused: false }),
      noDebeExistir: () => {
        expect(screen.queryByRole('menuitem', { name: 'Pausar' })).not.toBeInTheDocument();
        expect(screen.queryByRole('menuitem', { name: 'Reanudar' })).not.toBeInTheDocument();
      },
      interactuar: async () => abrirAcciones('TR-RED'),
    },
    {
      accion: 'procesar (placa asignada)',
      fila: makeRed({ estado: 'entregado', plateFlowStatus: 'asignado' }),
      noDebeExistir: () => {
        expect(screen.queryByRole('menuitem', { name: 'Procesar' })).not.toBeInTheDocument();
      },
      interactuar: async () => abrirAcciones('TR-RED'),
    },
    {
      accion: 'menú administrativo (cambiar estado, anular, consolidado, reenviar, reasignar)',
      fila: makeRed({ estado: 'entregado' }),
      noDebeExistir: () => {
        for (const label of [
          'Cambiar estado',
          'Anular',
          'Gestionar consolidado',
          'Reenviar validación',
          'Reasignar gestor',
        ]) {
          expect(screen.queryByRole('menuitem', { name: label })).not.toBeInTheDocument();
        }
      },
      interactuar: async () => abrirAcciones('TR-RED'),
    },
    {
      accion: 'ver documentos / consolidado (gestión documental de la fila)',
      fila: makeRed({ consolidadoAttachmentId: 'att-consolidado' }),
      noDebeExistir: () => {
        expect(screen.queryByRole('menuitem', { name: 'Ver documentos' })).not.toBeInTheDocument();
        expect(screen.queryByRole('menuitem', { name: 'Ver consolidado' })).not.toBeInTheDocument();
      },
      interactuar: async () => abrirAcciones('TR-RED'),
    },
  ];

  for (const caso of casos) {
    it(`no ofrece «${caso.accion}» en una fila de la red y no dispara escritura`, async () => {
      tokenCabeza();
      mocks.listInstances.mockResolvedValue([makeInstance(), caso.fila]);
      renderTable();
      await screen.findByText('BBB222');
      await caso.interactuar();
      caso.noDebeExistir();
      expect(escriturasDisparadas()).toEqual([]);
    });
  }

  it('la fila de la red muestra el distintivo «Solo consulta» con texto e icono y el nombre del hijo', async () => {
    tokenCabeza();
    mocks.listInstances.mockResolvedValue([makeInstance(), makeRed()]);
    renderTable();
    await screen.findByText('BBB222');
    const badge = screen.getByRole('status', { name: /Solo consulta: trámite de un cliente de la red/ });
    expect(badge).toHaveTextContent('Solo consulta');
    expect(badge.querySelector('svg')).not.toBeNull();
    expect(screen.getByText('Concesionario Hijo SAS')).toBeInTheDocument();
    // La fila propia NO lleva distintivo.
    expect(screen.getAllByRole('status', { name: /Solo consulta/ })).toHaveLength(1);
  });

  it('un borrador de la red abre el DETALLE en consulta, nunca el asistente (sin navegar)', async () => {
    tokenCabeza();
    mocks.listInstances.mockResolvedValue([makeRed({ estado: 'borrador' })]);
    renderTable();
    await screen.findByText('BBB222');
    await abrirAcciones('TR-RED');
    // La acción de apertura se rotula «Ver», no «Continuar».
    expect(screen.getByRole('menuitem', { name: 'Ver' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Continuar' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Ver' }));
    expect(routerPush).not.toHaveBeenCalled();
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('status', { name: /Solo consulta/ })).toBeInTheDocument();
    // El detalle se lee por la ruta consolidada, no por la propia.
    await waitFor(() => expect(mocks.getNetworkInstance).toHaveBeenCalledWith('inst-red'));
    expect(mocks.getInstance).not.toHaveBeenCalled();
    expect(escriturasDisparadas()).toEqual([]);
  });

  it('el radicado (acceso por teclado a la fila) también abre el detalle en consulta', async () => {
    tokenCabeza();
    mocks.listInstances.mockResolvedValue([makeRed({ estado: 'borrador' })]);
    renderTable();
    await screen.findByText('BBB222');
    await userEvent.click(screen.getByRole('button', { name: 'Abrir trámite TR-RED' }));
    expect(routerPush).not.toHaveBeenCalled();
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    expect(escriturasDisparadas()).toEqual([]);
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — AC6: acciones masivas mixtas', () => {
  it('con propios y de la red seleccionados, la barra actúa solo sobre los propios e indica los excluidos', async () => {
    tokenCabeza();
    const propio = makeInstance({ estado: 'borrador', origin: 'ict', isPaused: false });
    const red = makeRed({ estado: 'borrador', origin: 'ict', isPaused: false });
    mocks.listInstances.mockResolvedValue([propio, red]);
    renderTable();
    await screen.findByText('BBB222');

    await userEvent.click(
      screen.getByRole('checkbox', { name: /Seleccionar el trámite TR-PROPIO/ }),
    );
    await userEvent.click(screen.getByRole('checkbox', { name: /Seleccionar el trámite TR-RED/ }));

    const barra = screen.getByRole('region', { name: 'Acciones masivas de pausa' });
    expect(barra).toHaveTextContent('2 seleccionados');
    expect(barra).toHaveTextContent('1 excluido: trámites de la red (solo consulta)');

    await userEvent.click(within(barra).getByRole('button', { name: /Pausar/ }));
    await waitFor(() => expect(mocks.pauseInstancesMassive).toHaveBeenCalledTimes(1));
    // Solo el propio viaja; el de la red ni se manda ni se marca en pantalla.
    expect(mocks.pauseInstancesMassive.mock.calls[0][0]).toEqual(['inst-propio']);
  });

  it('con SOLO trámites de la red seleccionados no se ofrece ninguna acción masiva', async () => {
    tokenCabeza();
    mocks.listInstances.mockResolvedValue([
      makeRed({ estado: 'borrador', origin: 'ict', isPaused: false }),
    ]);
    renderTable();
    await screen.findByText('BBB222');
    await userEvent.click(screen.getByRole('checkbox', { name: /Seleccionar el trámite TR-RED/ }));
    const barra = screen.getByRole('region', { name: 'Acciones masivas de pausa' });
    expect(within(barra).queryByRole('button', { name: /Pausar/ })).not.toBeInTheDocument();
    expect(within(barra).queryByRole('button', { name: /Reanudar/ })).not.toBeInTheDocument();
    expect(barra).toHaveTextContent('1 excluido: trámites de la red (solo consulta)');
    expect(mocks.pauseInstancesMassive).not.toHaveBeenCalled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — AC1/AC2/AC3: detalle en modo consulta', () => {
  const ITEM_RECHAZADO = makeRed({ estado: 'rechazado', ultimoRechazoMotivo: 'Falta SOAT' });

  it('no ofrece la CTA de subsanación aunque se pase `onAbrirAsistente`, y no llama a startSubsanacion', async () => {
    const onAbrirAsistente = vi.fn();
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-red"
        item={ITEM_RECHAZADO}
        onClose={() => undefined}
        onAbrirAsistente={onAbrirAsistente}
        consultaMode
      />,
    );
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Falta SOAT')).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: /Subsanar trámite/ })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: /Continuar la subsanación/ })).not.toBeInTheDocument();
    // Recorrer los toggles y el stepper no dispara nada.
    await userEvent.click(within(dialog).getByRole('button', { name: /Línea de Tiempo del Trámite/ }));
    await userEvent.click(within(dialog).getByRole('button', { name: /Trazabilidad de Identidad/ }));
    expect(mocks.startSubsanacion).not.toHaveBeenCalled();
    expect(onAbrirAsistente).not.toHaveBeenCalled();
    expect(escriturasDisparadas()).toEqual([]);
  });

  it('archivos finales: 403 ⇒ copy de fuera de alcance, sin error técnico ni reintento', async () => {
    mocks.getAttachments.mockRejectedValue(Object.assign(new Error('Forbidden'), { status: 403 }));
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-red"
        item={makeRed()}
        onClose={() => undefined}
        consultaMode
      />,
    );
    expect(await screen.findByText(COPY_DOCUMENTOS_FUERA_DE_ALCANCE)).toBeInTheDocument();
    expect(screen.queryByText('Forbidden')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Reintentar los archivos finales/ }),
    ).not.toBeInTheDocument();
  });

  it('archivos finales con adjuntos: se listan pero SIN botón de descarga', async () => {
    mocks.getAttachments.mockResolvedValue([
      {
        id: 'att-1',
        tipo: 'fur',
        filename: 'FUR.pdf',
        sha256: 'abc',
        source: 'system',
        mimetype: 'application/pdf',
        sizeBytes: 10,
        uploadedAt: '2026-06-18T00:00:00Z',
      },
    ]);
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-red"
        item={makeRed()}
        onClose={() => undefined}
        consultaMode
      />,
    );
    expect(await screen.findByText('FUR.pdf')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Descargar FUR.pdf/ })).not.toBeInTheDocument();
    expect(mocks.downloadAttachment).not.toHaveBeenCalled();
  });

  it('sección Documentos: en consulta no ofrece descarga; el 403 se pinta con el copy del AC3', async () => {
    mocks.getChecklist.mockResolvedValue({
      items: [{ key: 'soat', label: 'SOAT', docTipo: 'soat', obligatorio: true, satisfied: true }],
    });
    mocks.getAttachments.mockResolvedValue([
      {
        id: 'att-soat',
        tipo: 'soat',
        filename: 'soat.pdf',
        sha256: 'x',
        source: 'user',
        mimetype: 'application/pdf',
        sizeBytes: 10,
        uploadedAt: '2026-06-18T00:00:00Z',
      },
    ]);
    const { unmount } = render(
      <ConsultaModeProvider consultaMode>
        <TramiteDetalleDocumentos instanceId="inst-red" item={makeRed()} />
      </ConsultaModeProvider>,
    );
    expect(await screen.findByText(/SOAT/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Descargar/ })).not.toBeInTheDocument();
    unmount();

    mocks.getChecklist.mockRejectedValue(Object.assign(new Error('Forbidden'), { status: 403 }));
    render(
      <ConsultaModeProvider consultaMode>
        <TramiteDetalleDocumentos instanceId="inst-red" item={makeRed()} />
      </ConsultaModeProvider>,
    );
    const copy = await screen.findByText(COPY_DOCUMENTOS_FUERA_DE_ALCANCE);
    expect(copy).toHaveAttribute('role', 'status');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Reintentar/ })).not.toBeInTheDocument();
    expect(screen.queryByText('Forbidden')).not.toBeInTheDocument();
  });

  it('paridad: fuera del modo consulta el mismo 403 sigue siendo un error con reintento', async () => {
    mocks.getChecklist.mockRejectedValue(Object.assign(new Error('Forbidden'), { status: 403 }));
    render(<TramiteDetalleDocumentos instanceId="inst-propio" item={makeInstance()} />);
    expect(await screen.findByRole('alert')).toHaveTextContent('Forbidden');
    expect(screen.getByRole('button', { name: /Reintentar/ })).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — AC4: el trámite propio de la cabeza no cambia', () => {
  it('la fila propia conserva prioridad, menú admin y la llamada de escritura funciona igual', async () => {
    tokenCabeza();
    mocks.listInstances.mockResolvedValue([
      makeInstance({ estado: 'entregado', plateFlowStatus: 'asignado' }),
      makeRed(),
    ]);
    renderTable();
    await screen.findByText('AAA111');

    await userEvent.click(
      screen.getByRole('button', { name: /Marcar como prioritario el trámite TR-PROPIO/ }),
    );
    await waitFor(() => expect(mocks.setPriority).toHaveBeenCalledWith('inst-propio', true, undefined));

    await abrirAcciones('TR-PROPIO');
    const items = menuItems();
    for (const label of ['Ver', 'Procesar', 'Ver documentos', 'Cambiar estado', 'Anular', 'Reasignar gestor']) {
      expect(items).toContain(label);
    }
    expect(screen.queryByRole('status', { name: /Solo consulta/ })).toBeInTheDocument(); // solo la de la red
  });

  it('el detalle de un trámite propio rechazado sigue ofreciendo «Subsanar trámite»', async () => {
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-propio"
        item={makeInstance({ estado: 'rechazado', ultimoRechazoMotivo: 'Falta SOAT' })}
        onClose={() => undefined}
        onAbrirAsistente={() => undefined}
      />,
    );
    expect(await screen.findByRole('button', { name: /Subsanar trámite/ })).toBeInTheDocument();
    await waitFor(() => expect(mocks.getInstance).toHaveBeenCalledWith('inst-propio', undefined));
    expect(mocks.getNetworkInstance).not.toHaveBeenCalled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — AC5: un cliente sin jerarquía no percibe cambios', () => {
  it('el listado hace exactamente las mismas llamadas de hoy y ninguna de red', async () => {
    tokenSinJerarquia();
    mocks.listInstances.mockResolvedValue([
      makeInstance({ estado: 'borrador', origin: 'ict', isPaused: false }),
    ]);
    renderTable();
    await screen.findByText('AAA111');
    await waitFor(() => expect(mocks.getConsultationConfig).toHaveBeenCalled());

    expect(llamadasHechas()).toEqual(
      ['getConsultationConfig', 'listFilterFields', 'listInstances', 'searchEstadoCounts', 'searchInstances'].sort(),
    );
    expect(screen.queryByRole('status', { name: /Solo consulta/ })).not.toBeInTheDocument();
    // Y la interfaz es la de siempre: estrella, checkbox ICT, «Continuar» y «Pausar».
    expect(screen.getByRole('button', { name: /Marcar como prioritario el trámite TR-PROPIO/ })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /para pausar\/reanudar en lote/ })).toBeInTheDocument();
    await abrirAcciones('TR-PROPIO');
    expect(screen.getByRole('menuitem', { name: 'Continuar' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Pausar' })).toBeInTheDocument();
  });

  it('el enlace directo /tramites/{id} monta el asistente sin ninguna llamada previa', async () => {
    tokenSinJerarquia();
    render(<TramiteInstanceGate instanceId="inst-propio" />);
    expect(await screen.findByTestId('tramite-wizard')).toBeInTheDocument();
    expect(wizardMounted).toHaveBeenCalledWith('inst-propio');
    expect(mocks.getNetworkInstance).not.toHaveBeenCalled();
    expect(mocks.searchNetworkInstances).not.toHaveBeenCalled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — AC7: enlace directo a una pantalla de escritura de un hijo', () => {
  it('la cabeza ve el detalle en modo consulta en vez del asistente, sin escrituras ni error', async () => {
    tokenCabeza();
    mocks.searchNetworkInstances.mockResolvedValue({ items: [makeRed()], total: 1 });
    render(<TramiteInstanceGate instanceId="inst-red" />);

    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('status', { name: /Solo consulta/ })).toBeInTheDocument();
    expect(dialog).toHaveTextContent('TR-RED');
    expect(screen.queryByTestId('tramite-wizard')).not.toBeInTheDocument();
    expect(wizardMounted).not.toHaveBeenCalled();
    expect(mocks.getNetworkInstance).toHaveBeenCalledWith('inst-red');
    expect(mocks.getInstance).not.toHaveBeenCalled();
    expect(escriturasDisparadas()).toEqual([]);
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('si el listado consolidado no devuelve la fila, el detalle se arma desde el propio detalle', async () => {
    tokenCabeza();
    mocks.searchNetworkInstances.mockResolvedValue({ items: [], total: 0 });
    render(<TramiteInstanceGate instanceId="inst-red" />);
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('TR-RED');
    expect(dialog).toHaveTextContent('Concesionario Hijo SAS');
    expect(escriturasDisparadas()).toEqual([]);
  });

  it('un trámite PROPIO de la cabeza por enlace directo sigue abriendo el asistente', async () => {
    tokenCabeza();
    mocks.getNetworkInstance.mockResolvedValue({
      id: 'inst-propio',
      referenceNumber: 'TR-PROPIO',
      status: 'borrador',
      tenantId: CABEZA,
      tenantName: 'Cabeza',
      createdAt: '2026-06-18T00:00:00Z',
      statusHistory: [],
      fieldValues: [],
      actors: [],
      fromNetwork: true,
    });
    render(<TramiteInstanceGate instanceId="inst-propio" />);
    expect(await screen.findByTestId('tramite-wizard')).toBeInTheDocument();
    expect(mocks.searchNetworkInstances).not.toHaveBeenCalled();
  });

  it('si la ruta consolidada falla, no se muestra error: el asistente sigue siendo la pantalla dueña', async () => {
    tokenCabeza();
    mocks.getNetworkInstance.mockRejectedValue(Object.assign(new Error('Not found'), { status: 404 }));
    render(<TramiteInstanceGate instanceId="inst-x" />);
    expect(await screen.findByTestId('tramite-wizard')).toBeInTheDocument();
    expect(screen.queryByText('Not found')).not.toBeInTheDocument();
  });
});
