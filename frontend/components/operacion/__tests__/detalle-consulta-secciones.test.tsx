import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12362 — secciones del detalle en modo consulta (code review PR #370, hallazgo MAYOR).
 *
 * Uso de ejemplo: la cabeza abre el detalle de un trámite de un hijo. El modal pide UNA vez
 * `GET /tramites/network/instances/{id}` (detalle consolidado, con `actors` y `fieldValues`) y las
 * secciones NO disparan las rutas propias que el servidor rechaza por diseño (404 anti-enumeración):
 * `getActors`, `getCommercial`, `getPrenda`, `listBiometricExpediente`, `getPreflight`.
 *
 * - Actores: pinta los del DTO consolidado (nombre, documento, correo).
 * - Comercial/Prenda, Identidad (expediente + trazabilidad), Preflight: copy «fuera de tu alcance»
 *   como `role="status"`, sin error técnico ni reintento, y CERO peticiones.
 * - Fuera de consulta: paridad — las mismas llamadas de hoy.
 * - Contrato: `getNetworkInstance` desenvuelve `{ tenantId, tenantName, instance }`.
 */

const mocks = vi.hoisted(() => ({
  getInstance: vi.fn(),
  getNetworkInstance: vi.fn(),
  getAttachments: vi.fn(),
  getNetworkAttachments: vi.fn(),
  getChecklist: vi.fn(),
  getActors: vi.fn(),
  getCommercial: vi.fn(),
  getPrenda: vi.fn(),
  getPreflight: vi.fn(),
  listBiometricExpediente: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  downloadNetworkAttachment: vi.fn(),
  downloadBiometricCertificado: vi.fn(),
  startSubsanacion: vi.fn(),
}));

/** Rutas PROPIAS de las secciones: para un trámite de un hijo responden 404 por diseño. */
const RUTAS_PROPIAS_DE_SECCION = [
  'getActors',
  'getCommercial',
  'getPrenda',
  'listBiometricExpediente',
  'getPreflight',
] as const;

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  setActiveTramitesTenant: vi.fn(),
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteDetalleModal } from '@/components/operacion/TramiteDetalleModal';
import { TramiteDetalleActores } from '@/components/operacion/detalle/TramiteDetalleActores';
import { TramiteDetalleComercial } from '@/components/operacion/detalle/TramiteDetalleComercial';
import { TramiteDetalleIdentidad } from '@/components/operacion/detalle/TramiteDetalleIdentidad';
import { TramiteDetalleVehiculo } from '@/components/operacion/detalle/TramiteDetalleVehiculo';
import { ConsultaModeProvider } from '@/components/operacion/ConsultaModeContext';
import {
  COPY_SECCION_FUERA_DE_ALCANCE,
  actoresDesdeDetalleConsolidado,
} from '@/lib/tramites/network-scope';

const CABEZA = '11111111-1111-1111-1111-111111111111';
const HIJO = '22222222-2222-2222-2222-222222222222';

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

/** `instance.actors` tal como lo devuelve el DTO consolidado (`ProcedureInstanceActorDto`). */
const ACTORES_DTO = [
  {
    actorType: 'vendedor',
    documentType: 'CC',
    documentNumber: '2000001',
    fullName: 'Vendedora De La Red',
    email: 'vendedora@red.test',
  },
  {
    actorType: 'comprador',
    documentType: 'NIT',
    documentNumber: '900123456',
    fullName: 'Compradora De La Red SAS',
    email: 'compradora@red.test',
  },
];

/** Detalle consolidado ya aplanado por `tramitesClient.getNetworkInstance` (shape del modal). */
function detalleRed(over: Record<string, unknown> = {}) {
  return {
    id: 'inst-red',
    referenceNumber: 'TR-RED',
    status: 'entregado',
    procedureTypeId: 'pt-1',
    tenantId: HIJO,
    tenantName: 'Concesionario Hijo SAS',
    createdAt: '2026-06-18T00:00:00Z',
    submittedAt: null,
    completedAt: null,
    statusHistory: [],
    fieldValues: [],
    actors: ACTORES_DTO,
    fromNetwork: true,
    ...over,
  };
}

function llamadasDeSeccion(): string[] {
  return RUTAS_PROPIAS_DE_SECCION.filter((k) => mocks[k].mock.calls.length > 0);
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue({ ...detalleRed(), id: 'inst-propio', tenantId: CABEZA, fromNetwork: undefined });
  mocks.getNetworkInstance.mockResolvedValue(detalleRed());
  mocks.getAttachments.mockResolvedValue([]);
  mocks.getNetworkAttachments.mockResolvedValue([]);
  mocks.getChecklist.mockResolvedValue({ items: [] });
  mocks.getActors.mockResolvedValue([
    {
      rol: 'comprador',
      tipoDocumento: 'CC',
      numeroDocumento: '1000001',
      nombreCompleto: 'Comprador Propio',
      email: 'propio@test.io',
      telefono: '3000000000',
    },
  ]);
  mocks.getCommercial.mockResolvedValue(null);
  mocks.getPrenda.mockResolvedValue([]);
  mocks.getPreflight.mockResolvedValue(null);
  mocks.listBiometricExpediente.mockResolvedValue({
    validations: [],
    firmaBaulPartes: [],
    firmaBaulActores: [],
  });
  // Cualquier ruta propia en consulta sería un 404 real: que el test lo delate si se llama.
  for (const k of RUTAS_PROPIAS_DE_SECCION) {
    mocks[k].mockImplementation(async (...args: unknown[]) => {
      const instanceId = String(args[0] ?? '');
      if (instanceId === 'inst-red') {
        throw Object.assign(new Error('Not found'), { status: 404 });
      }
      return k === 'getActors'
        ? [
            {
              rol: 'comprador',
              tipoDocumento: 'CC',
              numeroDocumento: '1000001',
              nombreCompleto: 'Comprador Propio',
              email: 'propio@test.io',
            },
          ]
        : k === 'getPrenda'
          ? []
          : k === 'listBiometricExpediente'
            ? { validations: [], firmaBaulPartes: [], firmaBaulActores: [] }
            : null;
    });
  }
});

afterEach(() => {
  document.cookie = 'flit_token=; path=/; Max-Age=0';
});

function renderSecciones(consultaMode: boolean, item: InstanceSummary, detalle = detalleRed()) {
  return render(
    <ConsultaModeProvider
      consultaMode={consultaMode}
      detalle={consultaMode ? (detalle as never) : null}
      detalleLoading={false}
      detalleError={null}
    >
      <TramiteDetalleActores instanceId={item.id} item={item} />
      <TramiteDetalleIdentidad instanceId={item.id} item={item} />
      <TramiteDetalleVehiculo instanceId={item.id} item={item} />
      <TramiteDetalleComercial instanceId={item.id} item={item} />
    </ConsultaModeProvider>,
  );
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — secciones en consulta: sin rutas propias, actores del DTO consolidado', () => {
  it('Actores pinta los del detalle consolidado y NO llama a getActors', async () => {
    renderSecciones(true, makeRed());
    expect(await screen.findByText('Vendedora De La Red')).toBeInTheDocument();
    expect(screen.getByText('Compradora De La Red SAS')).toBeInTheDocument();
    expect(screen.getByText('CC 2000001')).toBeInTheDocument();
    expect(screen.getByText('NIT 900123456')).toBeInTheDocument();
    expect(screen.getByText('vendedora@red.test')).toBeInTheDocument();
    expect(mocks.getActors).not.toHaveBeenCalled();
  });

  it('Actores con el DTO vacío muestra el estado vacío (no «fuera de alcance») y sin llamada', async () => {
    renderSecciones(true, makeRed(), detalleRed({ actors: [] }));
    expect(
      await screen.findByText('Este trámite no tiene actores registrados.'),
    ).toBeInTheDocument();
    expect(mocks.getActors).not.toHaveBeenCalled();
  });

  it('Comercial/Prenda, Identidad y Preflight muestran el copy de alcance como status, sin peticiones ni reintento', async () => {
    renderSecciones(true, makeRed());
    await screen.findByText('Vendedora De La Red');
    const avisos = screen.getAllByRole('status').filter(
      (el) => el.textContent?.trim() === COPY_SECCION_FUERA_DE_ALCANCE,
    );
    // Datos comerciales + Prenda/gravamen + Validación de identidad + Verificación de requisitos.
    expect(avisos).toHaveLength(4);
    expect(screen.queryByRole('button', { name: /Reintentar/ })).not.toBeInTheDocument();
    expect(screen.queryByText('Not found')).not.toBeInTheDocument();
    expect(llamadasDeSeccion()).toEqual([]);
    // Las especificaciones técnicas SÍ se leen, por la ruta consolidada.
    expect(mocks.getNetworkInstance).toHaveBeenCalledWith('inst-red');
    expect(mocks.getInstance).not.toHaveBeenCalled();
  });

  it('fuera de consulta: paridad — las mismas llamadas de hoy y los actores de getActors', async () => {
    renderSecciones(false, makeInstance());
    expect(await screen.findByText('Comprador Propio')).toBeInTheDocument();
    await waitFor(() =>
      expect(llamadasDeSeccion()).toEqual([
        'getActors',
        'getCommercial',
        'getPrenda',
        'listBiometricExpediente',
        'getPreflight',
      ]),
    );
    expect(mocks.getInstance).toHaveBeenCalledWith('inst-propio', undefined);
    expect(mocks.getNetworkInstance).not.toHaveBeenCalled();
    expect(
      screen.queryByText(COPY_SECCION_FUERA_DE_ALCANCE),
    ).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — modal completo en consulta: una sola lectura consolidada', () => {
  it('recorriendo Actores, Vehículo, Comercial y la trazabilidad de identidad no dispara ninguna ruta propia', async () => {
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-red"
        item={makeRed()}
        onClose={() => undefined}
        consultaMode
      />,
    );
    const dialog = await screen.findByRole('dialog');
    await waitFor(() => expect(mocks.getNetworkInstance).toHaveBeenCalledWith('inst-red'));

    await userEvent.click(within(dialog).getByRole('tab', { name: /Actores y validación/ }));
    expect(await within(dialog).findByText('Compradora De La Red SAS')).toBeInTheDocument();
    expect(within(dialog).getByText('Vendedora De La Red')).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole('tab', { name: /Trámite y vehículo/ }));
    await userEvent.click(within(dialog).getByRole('tab', { name: /Datos comerciales/ }));
    expect(
      within(dialog).getAllByRole('status').some(
        (el) => el.textContent?.trim() === COPY_SECCION_FUERA_DE_ALCANCE,
      ),
    ).toBe(true);

    await userEvent.click(within(dialog).getByRole('button', { name: /Trazabilidad de Identidad/ }));
    expect(
      within(dialog).getAllByRole('status').some(
        (el) => el.textContent?.trim() === COPY_SECCION_FUERA_DE_ALCANCE,
      ),
    ).toBe(true);

    expect(llamadasDeSeccion()).toEqual([]);
    expect(mocks.getInstance).not.toHaveBeenCalled();
    expect(within(dialog).queryByText('Not found')).not.toBeInTheDocument();
  });

  it('fuera de consulta el modal sigue pidiendo el expediente biométrico para la trazabilidad', async () => {
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-propio"
        item={makeInstance()}
        onClose={() => undefined}
      />,
    );
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: /Trazabilidad de Identidad/ }));
    await waitFor(() =>
      expect(mocks.listBiometricExpediente).toHaveBeenCalledWith('inst-propio', undefined),
    );
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12362 — contrato: `actoresDesdeDetalleConsolidado` (Actor embebido → ProcedureActor)', () => {
  it('mapea rol/documento/nombre/correo y asigna ordinal por posición dentro de cada rol', () => {
    const out = actoresDesdeDetalleConsolidado([
      ...ACTORES_DTO,
      {
        actorType: 'comprador',
        documentType: 'CC',
        documentNumber: '3000003',
        fullName: 'Copropietaria',
        email: null,
      },
    ]);
    expect(out).toEqual([
      {
        rol: 'vendedor',
        tipoDocumento: 'CC',
        numeroDocumento: '2000001',
        nombreCompleto: 'Vendedora De La Red',
        email: 'vendedora@red.test',
        ordinal: 1,
      },
      {
        rol: 'comprador',
        tipoDocumento: 'NIT',
        numeroDocumento: '900123456',
        nombreCompleto: 'Compradora De La Red SAS',
        email: 'compradora@red.test',
        ordinal: 1,
      },
      {
        rol: 'comprador',
        tipoDocumento: 'CC',
        numeroDocumento: '3000003',
        nombreCompleto: 'Copropietaria',
        email: '',
        ordinal: 2,
      },
    ]);
  });

  it('descarta actorType fuera del vocabulario y tolera null/undefined', () => {
    expect(actoresDesdeDetalleConsolidado(null)).toEqual([]);
    expect(actoresDesdeDetalleConsolidado(undefined)).toEqual([]);
    expect(
      actoresDesdeDetalleConsolidado([
        { actorType: 'testigo', documentType: 'CC', documentNumber: '1', fullName: 'X' },
      ]),
    ).toEqual([]);
  });

  it('no inventa teléfono, dirección, ciudad ni representante legal (no viajan en el embebido)', () => {
    const [actor] = actoresDesdeDetalleConsolidado([ACTORES_DTO[0]]);
    expect(actor).not.toHaveProperty('telefono');
    expect(actor).not.toHaveProperty('direccion');
    expect(actor).not.toHaveProperty('ciudad');
    expect(actor).not.toHaveProperty('representanteLegal');
    expect(actor).not.toHaveProperty('porcentaje');
  });
});
