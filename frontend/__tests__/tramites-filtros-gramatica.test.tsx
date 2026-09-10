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
// HU #12163 — el menú de acciones avanzadas del admin usa `useToast`: la tabla necesita
// `<ToastProvider>` en el árbol, igual que en producción.
import { ToastProvider } from '@/components/admin/Toast';

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
  // HU #12188 — el doble se comporta como el servidor también con la BÚSQUEDA: es él quien la
  // resuelve desde esta HU, así que un doble que devolviera siempre la misma fila haría creer que
  // el estado «Sin resultados» no se alcanza nunca.
  mocks.searchInstances.mockImplementation(async (params?: { busqueda?: string }) => {
    const texto = params?.busqueda?.trim().toLowerCase();
    const fila = instancia(1);
    const casa =
      !texto ||
      [fila.placa, fila.vin, fila.referenceNumber, fila.compradorNombre]
        .filter(Boolean)
        .join(' ')
        .toLowerCase()
        .includes(texto);
    return casa ? { items: [fila], total: 1 } : { items: [], total: 0 };
  });
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.listInstanceEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
});

describe('HU #12107 — AC2: el catálogo lo manda el servidor', () => {
  it('un campo que el frontend no conoce aparece en la barra sin tocar el frontend', async () => {
    mocks.listFilterFields.mockResolvedValue([CAMPO_INVENTADO]);
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    // UN clic. El panel ES el listado de campos: antes había que pulsar "Filtros" y luego un
    // "+ Filtro" que abría otro panel encima, dos superficies para llegar a la primera pregunta.
    await abrirFiltros();

    expect(
      within(screen.getByTestId('tramites-campos')).getByRole('button', { name: 'Color de carrocería' }),
    ).toBeInTheDocument();
  });

  it('sin catálogo no hay filtros que ofrecer, pero la tabla se pinta igual', async () => {
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    await abrirFiltros();
    // El panel abre, pero sin catálogo no tiene ningún campo que ofrecer.
    expect(screen.getByTestId('tramites-campos')).toBeEmptyDOMElement();
  });
});

describe('el panel de filtros es de un solo nivel', () => {
  it('elegir un campo cambia el contenido del panel, y "Volver" devuelve a la lista', async () => {
    mocks.listFilterFields.mockResolvedValue([CAMPO_INVENTADO]);
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    await abrirFiltros();
    await userEvent.click(
      within(screen.getByTestId('tramites-campos')).getByRole('button', {
        name: 'Color de carrocería',
      }),
    );

    // El editor SUSTITUYE a la lista dentro del mismo panel; no se abre otro encima.
    expect(screen.getByTestId('tramites-editor-color_carroceria')).toBeInTheDocument();
    expect(screen.queryByTestId('tramites-campos')).toBeNull();

    await userEvent.click(screen.getByRole('button', { name: /Volver a los filtros/ }));
    expect(screen.getByTestId('tramites-campos')).toBeInTheDocument();
    expect(screen.queryByTestId('tramites-editor-color_carroceria')).toBeNull();
  });

  it('mientras se edita un campo no hay dos "Aplicar" a la vez', async () => {
    mocks.listFilterFields.mockResolvedValue([CAMPO_INVENTADO]);
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    await abrirFiltros();
    // En la lista, el "Aplicar" del pie aplica EL LISTADO.
    expect(screen.getAllByRole('button', { name: 'Aplicar' })).toHaveLength(1);

    await userEvent.click(
      within(screen.getByTestId('tramites-campos')).getByRole('button', {
        name: 'Color de carrocería',
      }),
    );

    // Editando, el único "Aplicar" es el del editor, que confirma LA CONDICIÓN. Dos botones con
    // la misma palabra y distinto alcance, uno encima del otro, no se pueden distinguir.
    expect(screen.getAllByRole('button', { name: 'Aplicar' })).toHaveLength(1);
    expect(screen.getByTestId('tramites-aplicar-color_carroceria')).toBeInTheDocument();
  });
});

describe('HU #12107 — AC7: un catálogo que no carga no deja la pantalla inservible', () => {
  it('avisa y permite reintentar sin recargar la página', async () => {
    mocks.listFilterFields.mockRejectedValueOnce(new Error('503'));
    render(<ToastProvider><TramitesTable /></ToastProvider>);

    // La tabla NO depende del catálogo: sus filas están ahí.
    await screen.findByText('P0001');

    await abrirFiltros();
    expect(screen.getByText(/No se pudieron cargar los filtros/i)).toBeInTheDocument();

    mocks.listFilterFields.mockResolvedValue([CAMPO_INVENTADO]);
    await userEvent.click(screen.getByTestId('tramites-filtros-reintentar'));

    await waitFor(() =>
      expect(
        within(screen.getByTestId('tramites-campos')).getByRole('button', {
          name: 'Color de carrocería',
        }),
      ).toBeInTheDocument(),
    );
    expect(screen.queryByText(/No se pudieron cargar los filtros/i)).toBeNull();
  });
});

describe('HU #12107 — los chips hablan de lo APLICADO', () => {
  it('quitar un chip re-acota la tabla en el acto', async () => {
    mocks.listFilterFields.mockResolvedValue([CAMPO_INVENTADO]);
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    await abrirFiltros();
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
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    // Hay trámites, pero ninguno casa: es el caso que hay que distinguir de "aún no hay trámites",
    // que se lee como una cuenta recién creada y no como un filtro demasiado estrecho.
    await userEvent.type(screen.getByPlaceholderText(/buscar/i), 'ZZZ');

    // La búsqueda va al servidor tras un respiro (HU #12188): hay que esperar su respuesta.
    expect(await screen.findByText('Sin resultados')).toBeInTheDocument();
    expect(screen.getByText(/Ningún trámite coincide/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Limpiar filtros' })).toBeInTheDocument();
  });
});

describe('HU #12108 — ordenamiento por subcampo desde la cabecera', () => {
  it('AC1: la cabecera de una celda compuesta ofrece sus datos, en los dos sentidos', async () => {
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    // "Radicado" apila las dos fechas: un clic no podría decir por cuál se ordena.
    await userEvent.click(screen.getByRole('button', { name: /^Ordenar Radicado$/ }));
    const menu = screen.getByRole('menu', { name: /Ordenar por, en Radicado/ });

    for (const opcion of ['Radicado', 'Fecha de creación', 'Fecha de actualización']) {
      expect(within(menu).getAllByRole('menuitemradio', { name: new RegExp(opcion) })).toHaveLength(2);
    }
  });

  it('AC4: elegir un criterio lo pide al servidor y vuelve a la primera página', async () => {
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    await userEvent.click(screen.getByRole('button', { name: /^Ordenar Radicado$/ }));
    await userEvent.click(
      within(screen.getByRole('menu', { name: /Ordenar por, en Radicado/ })).getByRole(
        'menuitemradio',
        { name: /Fecha de actualización: Más reciente/ },
      ),
    );

    await waitFor(() =>
      expect(mocks.searchInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({ sortBy: 'updatedAt', sortDir: 'desc' }),
      ),
    );
  });

  it('AC3: solo hay un criterio activo, y la cabecera lo dice', async () => {
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    await userEvent.click(screen.getByRole('button', { name: /^Ordenar Radicado$/ }));
    await userEvent.click(
      within(screen.getByRole('menu', { name: /Ordenar por, en Radicado/ })).getByRole(
        'menuitemradio',
        { name: /Fecha de creación: Más antigua/ },
      ),
    );

    // El nombre accesible dice por qué ordena AHORA: sin esto solo lo diría el icono.
    expect(
      await screen.findByRole('button', {
        name: /Ordenar Radicado\. Ahora: Fecha de creación ascendente/,
      }),
    ).toBeInTheDocument();
    // Y ninguna otra cabecera compuesta se muestra como activa.
    expect(screen.getByRole('button', { name: /^Ordenar Vehículo$/ })).toBeInTheDocument();
  });

  it('AC2: una columna con un solo dato ordenable abre el MISMO menú', async () => {
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    // "Comprador" lleva un solo dato, pero la cabecera se comporta igual que las compuestas. Con
    // el clic que alternaba, dos cabeceras vecinas idénticas respondían distinto al mismo gesto y
    // nada lo anunciaba: había que pulsar para descubrir cuál era cuál.
    await userEvent.click(screen.getByRole('button', { name: /^Ordenar Comprador$/ }));

    const menu = screen.getByRole('menu', { name: /Ordenar por, en Comprador/ });
    await userEvent.click(within(menu).getByRole('menuitemradio', { name: /Comprador: A-Z/ }));

    await waitFor(() =>
      expect(mocks.searchInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({ sortBy: 'comprador', sortDir: 'asc' }),
      ),
    );
  });

  it('el sentido se rotula según el tipo de dato: ni una fecha ni un número se ordenan de A a Z', async () => {
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    await userEvent.click(screen.getByRole('button', { name: /^Ordenar Radicado$/ }));
    const menu = screen.getByRole('menu', { name: /Ordenar por, en Radicado/ });

    // Tres tipos de dato, tres rótulos. Rotularlos igual obliga a adivinar qué hace la flecha en
    // cada fila del menú. Desde la HU #12154 el radicado es un NÚMERO: «A-Z» sobre un número no
    // le dice al usuario si el 10 va antes o después del 9.
    expect(
      within(menu).getByRole('menuitemradio', { name: 'Radicado: Menor a mayor' }),
    ).toBeInTheDocument();
    expect(
      within(menu).getByRole('menuitemradio', { name: 'Radicado: Mayor a menor' }),
    ).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitemradio', { name: /Radicado: A-Z/ })).toBeNull();

    expect(
      within(menu).getByRole('menuitemradio', { name: 'Fecha de creación: Más antigua' }),
    ).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitemradio', { name: /Fecha de creación: A-Z/ })).toBeNull();
  });

  it('toda columna ordenable ofrece su menú, también Vendedor y Secretaría', async () => {
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    // Se quedaron sin desplegable hasta que el catálogo del backend admitió ordenar por ellas:
    // la cabecera solo ofrece lo que el servidor sabe ordenar, así que el hueco venía de allí.
    for (const columna of ['Vendedor', 'Secretaría']) {
      await userEvent.click(screen.getByRole('button', { name: new RegExp(`^Ordenar ${columna}$`) }));
      expect(
        screen.getByRole('menu', { name: new RegExp(`Ordenar por, en ${columna}`) }),
      ).toBeInTheDocument();
      await userEvent.keyboard('{Escape}');
    }
  });

  it('AC6: el menú se cierra con Escape y devuelve el foco a la cabecera', async () => {
    render(<ToastProvider><TramitesTable /></ToastProvider>);
    await screen.findByText('P0001');

    const cabecera = screen.getByRole('button', { name: /^Ordenar Radicado$/ });
    await userEvent.click(cabecera);
    expect(screen.getByRole('menu', { name: /Ordenar por, en Radicado/ })).toBeInTheDocument();

    await userEvent.keyboard('{Escape}');

    expect(screen.queryByRole('menu')).toBeNull();
    // Sin devolver el foco, quien navega con teclado se queda dentro de un menú que ya no existe.
    expect(screen.getByRole('button', { name: /^Ordenar Radicado$/ })).toHaveFocus();
  });
});
