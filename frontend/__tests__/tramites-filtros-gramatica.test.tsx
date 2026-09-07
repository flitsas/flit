import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12107 — la barra de filtros del listado con la gramática de Consultas.
 *
 * <p>Lo que se protege aquí es que el catálogo lo manda el SERVIDOR: es la propiedad que hace que
 * añadir un filtro deje de ser un cambio de frontend, y la única que no se puede comprobar mirando
 * el código de la pantalla.</p>
 */

const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  listInstanceEstadoCounts: vi.fn(),
  listFilterFields: vi.fn(),
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

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';

function instancia(i: number): InstanceSummary {
  const num = String(i).padStart(4, '0');
  return {
    id: `inst-${num}`, referenceNumber: `TR-${num}`, modalidad: 'TRASPASO', estado: 'borrador',
    placa: `P${num}`, vin: `VIN-${num}`, vehiculoMarca: 'Toyota', vehiculoLinea: 'Corolla',
    compradorNombre: `Comprador ${num}`, compradorDocumento: '1', vendedorNombre: `Vendedor ${num}`,
    vendedorDocumento: '2', organismoTransito: null, pasoActual: 2, totalPasos: 6,
    createdAt: '2026-06-18T00:00:00Z', draftFinalizedAt: null, identityValidationStatus: null,
    signaturePending: false, canSubmit: false, prioritario: false, tenantId: 't',
    companiaNombre: null, subsanacionActiva: false, subsanacionCount: 0, ultimoRechazoMotivo: null,
    updatedAt: null, gestorNombre: null, fuente: 'dashboard', firmaVendedorEstado: null,
    firmaCompradorEstado: null, consolidadoAttachmentId: null,
  } satisfies InstanceSummary;
}

/** Un campo que HOY no existe en el frontend: si aparece, es porque vino del servidor. */
const CAMPO_INVENTADO = {
  id: 'color_carroceria',
  label: 'Color de carrocería',
  kind: 'texto',
  group: 'Vehículo',
  operators: ['es_alguno', 'contiene'],
  options: [],
  hint: null,
  admiteLista: true,
};

async function abrirFiltros() {
  await userEvent.click(screen.getByRole('button', { name: /^Filtros/ }));
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
  mocks.listInstances.mockResolvedValue([instancia(1)]);
  mocks.searchInstances.mockImplementation(async () => ({ items: [instancia(1)], total: 1 }));
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.listInstanceEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
});

describe('HU #12107 — AC2: el catálogo lo manda el servidor', () => {
  it('un campo que el frontend no conoce aparece en la barra sin tocar el frontend', async () => {
    mocks.listFilterFields.mockResolvedValue([CAMPO_INVENTADO]);
    render(<TramitesTable />);
    await screen.findByText('P0001');

    await abrirFiltros();
    await userEvent.click(screen.getByTestId('tramites-agregar-filtro'));

    expect(
      within(screen.getByTestId('tramites-campos')).getByRole('button', { name: 'Color de carrocería' }),
    ).toBeInTheDocument();
  });

  it('sin catálogo no hay filtros que ofrecer, pero la tabla se pinta igual', async () => {
    render(<TramitesTable />);
    await screen.findByText('P0001');

    await abrirFiltros();
    // El botón de añadir existe pero no puede ofrecer nada: mejor deshabilitado que un panel vacío.
    expect(screen.getByTestId('tramites-agregar-filtro')).toBeDisabled();
  });
});

describe('HU #12107 — AC7: un catálogo que no carga no deja la pantalla inservible', () => {
  it('avisa y permite reintentar sin recargar la página', async () => {
    mocks.listFilterFields.mockRejectedValueOnce(new Error('503'));
    render(<TramitesTable />);

    // La tabla NO depende del catálogo: sus filas están ahí.
    await screen.findByText('P0001');

    await abrirFiltros();
    expect(screen.getByText(/No se pudieron cargar los filtros/i)).toBeInTheDocument();

    mocks.listFilterFields.mockResolvedValue([CAMPO_INVENTADO]);
    await userEvent.click(screen.getByTestId('tramites-filtros-reintentar'));

    await waitFor(() =>
      expect(screen.getByTestId('tramites-agregar-filtro')).toBeInTheDocument(),
    );
    expect(screen.queryByText(/No se pudieron cargar los filtros/i)).toBeNull();
  });
});

describe('HU #12107 — los chips hablan de lo APLICADO', () => {
  it('quitar un chip re-acota la tabla en el acto', async () => {
    mocks.listFilterFields.mockResolvedValue([CAMPO_INVENTADO]);
    render(<TramitesTable />);
    await screen.findByText('P0001');

    await abrirFiltros();
    await userEvent.click(screen.getByTestId('tramites-agregar-filtro'));
    await userEvent.click(
      within(screen.getByTestId('tramites-campos')).getByRole('button', { name: 'Color de carrocería' }),
    );
    await userEvent.type(screen.getByTestId('tramites-valores-color_carroceria'), 'Rojo');
    await userEvent.click(screen.getByTestId('tramites-aplicar-color_carroceria'));
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar' }));

    await waitFor(() =>
      expect(mocks.searchInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({
          condiciones: [{ fieldId: 'color_carroceria', operator: 'es_alguno', values: ['Rojo'] }],
        }),
      ),
    );

    // El chip de la tira usa la ETIQUETA del catálogo, no el id del campo.
    const chip = await screen.findByText(/Color de carrocería es Rojo/);
    expect(chip).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^Quitar filtro Color de carrocería/ }));

    // Quitarlo del chip lo retira de lo aplicado: la tabla se recarga sin esa condición. Retirarlo
    // solo del borrador dejaría la tabla igual y el chip desaparecido.
    await waitFor(() => {
      const ultima = mocks.searchInstances.mock.calls.at(-1)![0];
      expect(ultima.condiciones).toBeUndefined();
    });
  });
});

describe('HU #12107 — AC8: sin resultados se distingue de un error', () => {
  it('dice que ningún trámite coincide y ofrece limpiar los filtros', async () => {
    mocks.listFilterFields.mockResolvedValue([CAMPO_INVENTADO]);
    render(<TramitesTable />);
    await screen.findByText('P0001');

    // Hay trámites, pero ninguno casa: es el caso que hay que distinguir de "aún no hay trámites",
    // que se lee como una cuenta recién creada y no como un filtro demasiado estrecho.
    await userEvent.type(screen.getByPlaceholderText(/buscar/i), 'ZZZ');

    expect(await screen.findByText('Sin resultados')).toBeInTheDocument();
    expect(screen.getByText(/Ningún trámite coincide/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Limpiar filtros' })).toBeInTheDocument();
  });
});
