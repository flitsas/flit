import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  setPriority: vi.fn(),
  // HU #11054 / #11055 — documentos y consolidado desde el listado.
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  // ICT (PR #204) — pausa individual y masiva + cierre del subflujo de placa.
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  completePlateFlow: vi.fn(),
  // La tabla consulta la config del tenant al montar (bloqueo de creación por familia y
  // "solo vehículos propios"); sin este mock el efecto revienta y tumba todo el archivo.
  getConsultationConfig: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

// Preferencias de columnas: sin mock, el put fallaba y el selector revertía la selección.
vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: {
    get: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: {} }),
    put: vi.fn().mockResolvedValue({ scope: 'tramites.columns', value: { visible: [] } }),
  },
}));

// La tabla usa useRouter para abrir cada fila; en jsdom no hay router montado.
const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramitesTable } from '@/components/operacion/TramitesTable';

/** Genera n instancias draft con placa única (P0001, P0002, …). */
function makeInstances(n: number): InstanceSummary[] {
  return Array.from({ length: n }, (_, i) => {
    const num = String(i + 1).padStart(4, '0');
    return {
      id: `inst-${num}`,
      referenceNumber: `TR-${num}`,
      modalidad: 'traspaso',
      estado: 'borrador',
      placa: `P${num}`,
      vin: `VIN-${num}`,
      vehiculoMarca: 'Toyota',
      vehiculoLinea: 'Corolla',
      compradorNombre: `Comprador ${num}`,
      compradorDocumento: `100${num}`,
      vendedorNombre: `Vendedor ${num}`,
      vendedorDocumento: `200${num}`,
      organismoTransito: null,
      pasoActual: 2,
      totalPasos: 6,
      createdAt: '2026-06-18T00:00:00Z',
      // HU #10350 — defaults: borrador no finalizado, sin validación async ni firma pendiente.
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
      // HU #11056 — columnas de seguimiento. Por defecto: sin modificar, sin gestor resuelto,
      // origen plataforma, partes sin acreditar y sin consolidado generado.
      updatedAt: null,
      gestorNombre: null,
      fuente: 'dashboard',
      firmaVendedorEstado: 'pendiente',
      firmaCompradorEstado: 'pendiente',
      consolidadoAttachmentId: null,
    } satisfies InstanceSummary;
  });
}

/**
 * Abre el menú de acciones de una fila. Desde HU #11037 las acciones de la fila viven dentro de un
 * `ActionsMenu` (dropdown) en vez de botones sueltos: hay que abrirlo antes de buscar el ítem.
 */
async function abrirAcciones(referenceNumber = 'TR-0001') {
  await userEvent.click(
    screen.getByRole('button', { name: new RegExp(`Acciones del trámite ${referenceNumber}`) }),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  // clearAllMocks borra la implementación: sin re-armarla el efecto de config recibe undefined
  // y encadena .then sobre él. Por defecto, ninguna familia bloqueada.
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
});

describe('TramitesTable — paginación', () => {
  it('no muestra controles de paginación cuando todo cabe en una página', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(10));
    render(<TramitesTable />);

    await screen.findByText('P0001');
    expect(
      screen.queryByRole('navigation', { name: 'Paginación de trámites' }),
    ).not.toBeInTheDocument();
  });

  it('pagina a 10 filas por página y navega entre páginas', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(23));
    render(<TramitesTable />);

    // Página 1: P0001..P0010 visibles, P0011 no.
    await screen.findByText('P0001');
    expect(screen.getByText('P0010')).toBeInTheDocument();
    expect(screen.queryByText('P0011')).not.toBeInTheDocument();

    const nav = screen.getByRole('navigation', { name: 'Paginación de trámites' });
    // Paginación numerada del diseño: la página activa se marca con aria-current="page".
    expect(within(nav).getByRole('button', { name: 'Página 1', current: 'page' })).toBeInTheDocument();
    expect(within(nav).getByText('Mostrando 10 de 23')).toBeInTheDocument();
    // En la primera página "Anterior" está deshabilitado.
    expect(within(nav).getByRole('button', { name: 'Página anterior' })).toBeDisabled();

    // Avanzar a página 2.
    await userEvent.click(
      within(nav).getByRole('button', { name: 'Página siguiente' }),
    );
    expect(screen.queryByText('P0010')).not.toBeInTheDocument();
    expect(screen.getByText('P0011')).toBeInTheDocument();
    expect(screen.getByText('P0020')).toBeInTheDocument();

    // Avanzar a página 3 (última, parcial: 3 filas) → "Siguiente" deshabilitado.
    await userEvent.click(
      within(nav).getByRole('button', { name: 'Página siguiente' }),
    );
    expect(screen.getByText('P0021')).toBeInTheDocument();
    expect(screen.getByText('P0023')).toBeInTheDocument();
    expect(within(nav).getByRole('button', { name: 'Página 3', current: 'page' })).toBeInTheDocument();
    expect(within(nav).getByText('Mostrando 3 de 23')).toBeInTheDocument();
    expect(
      within(nav).getByRole('button', { name: 'Página siguiente' }),
    ).toBeDisabled();
  });

  it('vuelve a la primera página al aplicar un filtro de búsqueda', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(23));
    render(<TramitesTable />);

    const nav = await screen.findByRole('navigation', {
      name: 'Paginación de trámites',
    });
    await userEvent.click(
      within(nav).getByRole('button', { name: 'Página siguiente' }),
    );
    expect(within(nav).getByRole('button', { name: 'Página 2', current: 'page' })).toBeInTheDocument();

    // Buscar "Comprador" matchea las 23 (siguen 3 páginas) pero resetea a la 1.
    // La búsqueda vive en el panel de filtros colapsable.
    await userEvent.click(screen.getByRole('button', { name: /^Filtros/ }));
    await userEvent.type(
      screen.getByRole('searchbox', { name: 'Buscar trámites' }),
      'Comprador',
    );
    expect(within(nav).getByRole('button', { name: 'Página 1', current: 'page' })).toBeInTheDocument();
    expect(screen.getByText('P0001')).toBeInTheDocument();
    expect(screen.queryByText('P0011')).not.toBeInTheDocument();
  });
});

describe('TramitesTable — validación de identidad async (HU #10350, AC3)', () => {
  const [base] = makeInstances(1);

  it('borrador finalizado en proceso muestra el chip "Pendiente validación"', async () => {
    mocks.listInstances.mockResolvedValue([
      {
        ...base,
        id: 'pending',
        placa: 'PEND01',
        estado: 'borrador',
        draftFinalizedAt: '2026-06-20T10:00:00Z',
        identityValidationStatus: 'en_proceso',
      },
    ]);
    render(<TramitesTable />);

    const row = (await screen.findByText('PEND01')).closest('[role="button"]') as HTMLElement;
    expect(within(row).getByText('Pendiente validación')).toBeInTheDocument();
    // Accesible: el chip expone su estado por aria-label.
    expect(within(row).getByLabelText('Estado: Pendiente validación')).toBeInTheDocument();
    // Aún no se puede radicar → la acción sigue siendo "Continuar". Desde HU #11037 vive dentro del
    // menú de acciones de la fila, así que hay que abrirlo.
    await abrirAcciones();
    expect(screen.getByRole('menuitem', { name: /Continuar/ })).toBeInTheDocument();
  });

  it('identidad aprobada con firma pendiente muestra "Pendiente firma"', async () => {
    mocks.listInstances.mockResolvedValue([
      {
        ...base,
        id: 'firma',
        placa: 'FIRM01',
        estado: 'borrador',
        draftFinalizedAt: '2026-06-20T10:00:00Z',
        identityValidationStatus: 'aprobado',
        signaturePending: true,
      },
    ]);
    render(<TramitesTable />);

    const row = (await screen.findByText('FIRM01')).closest('[role="button"]') as HTMLElement;
    expect(within(row).getByText('Pendiente firma')).toBeInTheDocument();
  });

  it('identidad aprobada + canSubmit muestra "Listo para radicar" y acción "Radicar"', async () => {
    mocks.listInstances.mockResolvedValue([
      {
        ...base,
        id: 'ready',
        placa: 'RDY001',
        estado: 'borrador',
        draftFinalizedAt: '2026-06-20T10:00:00Z',
        identityValidationStatus: 'aprobado',
        signaturePending: false,
        canSubmit: true,
      },
    ]);
    render(<TramitesTable />);

    const row = (await screen.findByText('RDY001')).closest('[role="button"]') as HTMLElement;
    expect(within(row).getByText('Listo para radicar')).toBeInTheDocument();
    // La acción vive dentro del menú de la fila desde HU #11037.
    await abrirAcciones();
    expect(screen.getByRole('menuitem', { name: /Radicar/ })).toBeInTheDocument();
  });
});

describe('TramitesTable — organismo de tránsito', () => {
  const [base] = makeInstances(1);

  it('muestra el organismo en la fila y permite filtrar por él', async () => {
    mocks.listInstances.mockResolvedValue([
      { ...base, id: 'a', placa: 'BOG001', organismoTransito: 'Secretaría de Movilidad Bogotá' },
      { ...base, id: 'b', placa: 'CAL001', organismoTransito: 'Cali — STTMP' },
    ]);
    render(<TramitesTable />);

    await screen.findByText('BOG001');
    expect(screen.getByText('Secretaría de Movilidad Bogotá')).toBeInTheDocument();
    expect(screen.getByText('Cali — STTMP')).toBeInTheDocument();

    // El buscador también filtra por organismo.
    await userEvent.click(screen.getByRole('button', { name: /^Filtros/ }));
    await userEvent.type(
      screen.getByRole('searchbox', { name: 'Buscar trámites' }),
      'Cali',
    );
    expect(screen.queryByText('BOG001')).not.toBeInTheDocument();
    expect(screen.getByText('CAL001')).toBeInTheDocument();
  });
});

// ── HU #10536 — trámite prioritario: estrella (toggle) + filtro "Prioritarios" ──
describe('TramitesTable — HU #10536 prioridad', () => {
  const [base] = makeInstances(1);

  it('la estrella marca prioritario: llama a setPriority y refleja el estado (optimista)', async () => {
    mocks.listInstances.mockResolvedValue([
      { ...base, id: 'p1', placa: 'PRI001', prioritario: false },
    ]);
    mocks.setPriority.mockResolvedValue({ id: 'p1', prioritario: true });
    render(<TramitesTable />);

    const star = await screen.findByRole('button', {
      name: /Marcar como prioritario el trámite/,
    });
    expect(star).toHaveAttribute('aria-pressed', 'false');

    await userEvent.click(star);

    expect(mocks.setPriority).toHaveBeenCalledWith('p1', true, undefined);
    // Optimista: la fila pasa a ofrecer "Quitar prioridad" sin esperar un refetch.
    expect(
      await screen.findByRole('button', { name: /Quitar prioridad al trámite/ }),
    ).toBeInTheDocument();
  });

  it('el filtro "Prioritarios" muestra solo los trámites prioritarios', async () => {
    mocks.listInstances.mockResolvedValue([
      { ...base, id: 'a', placa: 'PRIO01', prioritario: true },
      { ...base, id: 'b', placa: 'NORM01', prioritario: false },
    ]);
    render(<TramitesTable />);

    await screen.findByText('PRIO01');
    expect(screen.getByText('NORM01')).toBeInTheDocument();

    await userEvent.click(
      screen.getByRole('button', { name: 'Mostrar solo trámites prioritarios' }),
    );

    expect(screen.getByText('PRIO01')).toBeInTheDocument();
    expect(screen.queryByText('NORM01')).not.toBeInTheDocument();
  });
});

// ── #1 — SuperAdmin: columna + filtro Compañía + abrir con ?t= ──────────────
function superAdminToken(): string {
  const b64 = (o: unknown) =>
    btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64({ sub: 'admin', role: 'SuperAdmin' })}.`;
}

function instance(over: Partial<InstanceSummary>): InstanceSummary {
  return {
    id: 'i', referenceNumber: 'TR', modalidad: 'traspaso', estado: 'borrador',
    placa: 'P', vin: 'V', vehiculoMarca: 'M', vehiculoLinea: 'L',
    compradorNombre: 'C', compradorDocumento: '1', organismoTransito: null,
    pasoActual: 1, totalPasos: 6, createdAt: '2026-06-18T00:00:00Z',
    draftFinalizedAt: null, identityValidationStatus: null,
    signaturePending: false, canSubmit: false, prioritario: false,
    tenantId: 't', companiaNombre: null,
    subsanacionActiva: false, subsanacionCount: 0, ultimoRechazoMotivo: null,
    ...over,
  };
}

describe('TramitesTable — subsanación / motivo de rechazo', () => {
  const [base] = makeInstances(1);

  it('no muestra chips en la fila; el icono abre el popover con flags', async () => {
    mocks.listInstances.mockResolvedValue([
      {
        ...base,
        id: 'sub-1',
        placa: 'SUB001',
        estado: 'rechazado',
        subsanacionActiva: true,
        subsanacionCount: 2,
        ultimoRechazoMotivo: null,
      },
    ]);
    render(<TramitesTable />);

    const row = (await screen.findByText('SUB001')).closest('[role="button"]') as HTMLElement;
    expect(within(row).queryByText('En subsanación')).not.toBeInTheDocument();
    expect(within(row).queryByText('Subsanado ×2')).not.toBeInTheDocument();

    await userEvent.click(
      screen.getByRole('button', {
        name: /Ver detalle de rechazo \/ subsanación de TR-0001/,
      }),
    );
    const dialog = screen.getByRole('dialog', { name: /Detalle de rechazo de TR-0001/ });
    expect(within(dialog).getByText('En subsanación')).toBeInTheDocument();
    expect(within(dialog).getByText('Subsanado ×2')).toBeInTheDocument();
    expect(within(dialog).queryByText('Motivo del OT')).not.toBeInTheDocument();
    expect(routerPush).not.toHaveBeenCalled();
  });

  it('abre y cierra el popover con el motivo del OT sin abrir el trámite', async () => {
    mocks.listInstances.mockResolvedValue([
      {
        ...base,
        id: 'rej-1',
        placa: 'REJ001',
        estado: 'rechazado',
        subsanacionActiva: false,
        subsanacionCount: 1,
        ultimoRechazoMotivo: 'Falta certificado de tradición',
      },
    ]);
    render(<TramitesTable />);

    await screen.findByText('REJ001');
    expect(screen.queryByText('Falta certificado de tradición')).not.toBeInTheDocument();

    const trigger = screen.getByRole('button', {
      name: /Ver detalle de rechazo \/ subsanación de TR-0001/,
    });
    await userEvent.click(trigger);
    expect(screen.getByText('Motivo del OT')).toBeInTheDocument();
    expect(screen.getByText('Falta certificado de tradición')).toBeInTheDocument();
    expect(screen.getByText('Subsanado ×1')).toBeInTheDocument();
    expect(routerPush).not.toHaveBeenCalled();

    await userEvent.click(trigger);
    expect(screen.queryByText('Falta certificado de tradición')).not.toBeInTheDocument();
  });
});

describe('TramitesTable — SuperAdmin multi-tenant', () => {
  beforeEach(() => {
    document.cookie = `flit_token=${superAdminToken()}; path=/`;
  });
  afterEach(() => {
    document.cookie = 'flit_token=; path=/; Max-Age=0';
  });

  // HU #11057 — la columna dedicada "Compañía" desapareció: la razón social vive ahora en la
  // columna "Gestor" (empresa + persona que radica), que es la misma información con más contexto y
  // para todos los perfiles. El filtro por compañía del SuperAdmin sigue intacto.
  it('muestra la compañía en Gestor, conserva el filtro y abre con el tenant de la fila (?t=)', async () => {
    mocks.listInstances.mockResolvedValue([
      instance({ id: 'a', placa: 'AAA111', tenantId: 'ten-a', companiaNombre: 'Empresa A' }),
      instance({ id: 'b', placa: 'BBB222', tenantId: 'ten-b', companiaNombre: 'Empresa B' }),
    ]);
    render(<TramitesTable />);

    await screen.findByText('AAA111');
    // Gestor entra en el default de la pantalla principal: ya no hay que activarla a mano.
    expect(within(screen.getByRole('row')).getByText('Gestor')).toBeInTheDocument();
    expect(within(screen.getByRole('row')).queryByText('Compañía')).not.toBeInTheDocument();
    const rows = screen.getByRole('list', { name: 'Trámites en curso' });
    expect(within(rows).getByText('Empresa A')).toBeInTheDocument();
    // Filtro Compañía presente (select con label) con la opción de la empresa.
    expect(screen.getByLabelText('Compañía')).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Empresa A' })).toBeInTheDocument();

    // Abrir una fila navega con ?t=<tenant de la fila>.
    await userEvent.click(screen.getByText('AAA111'));
    expect(routerPush).toHaveBeenCalledWith('/tramites/a?t=ten-a');
  });
});

// HU #11020 — el dashboard identifica el traspaso por sus DOS actores, sin abrir el trámite.
describe('TramitesTable — actores del traspaso (HU #11020)', () => {
  it('muestra las columnas de vendedor y comprador con sus valores', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    render(<TramitesTable />);

    await screen.findByText('P0001');
    // HU #11057 renombró la cabecera a "Propietario / vendedor" (rótulo del negocio).
    // El mismo texto aparece en el formulario de filtros; basta con que exista ≥1.
    expect(screen.getAllByText('Propietario / vendedor').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Comprador').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Vendedor 0001')).toBeInTheDocument();
    expect(screen.getByText('Comprador 0001')).toBeInTheDocument();
  });

  it('en matrícula inicial (sin vendedor) la celda queda vacía sin romper la fila', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      { ...item, modalidad: 'matricula_inicial', vendedorNombre: null, vendedorDocumento: null },
    ]);
    render(<TramitesTable />);

    await screen.findByText('P0001');
    expect(screen.getByText('Comprador 0001')).toBeInTheDocument();
    expect(screen.queryByText('Vendedor 0001')).toBeNull();
  });
});

// HU #11057 — columnas del listado; el default es compacto (el resto se activa con "Columnas").
describe('TramitesTable — columnas del listado (HU #11057)', () => {
  it('muestra por defecto las columnas esenciales (sin scroll de catálogo completo)', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    render(<TramitesTable />);

    await screen.findByText('P0001');
    const header = screen.getByRole('row');
    for (const col of [
      'Radicado',
      'VIN',
      'Placa',
      'Propietario / vendedor',
      'Comprador',
      'Firmas',
      'Trámite / Estado',
      'Secretaría',
      'Gestor',
      'Fuente',
      'Acciones',
    ]) {
      expect(within(header).getByText(col)).toBeInTheDocument();
    }
    // Columnas cuyo dato viaja apilado en una celda compuesta: fuera de la cabecera por defecto,
    // disponibles en el selector para moverlo a su propia columna.
    expect(within(header).queryByText('Vehículo')).not.toBeInTheDocument();
    expect(within(header).queryByText('Paso')).not.toBeInTheDocument();
    expect(within(header).queryByText('Estado')).not.toBeInTheDocument();
    expect(within(header).queryByText('Fecha de creación')).not.toBeInTheDocument();
  });

  it('permite activar columnas opcionales desde el selector', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    // Preferencias: sin backend en test, setVisible actualiza estado local.
    render(<TramitesTable />);
    await screen.findByText('P0001');

    await userEvent.click(screen.getByRole('button', { name: /Columnas/i }));
    await userEvent.click(screen.getByRole('checkbox', { name: /^Vehículo$/i }));
    await userEvent.click(screen.getByRole('checkbox', { name: /^Paso$/i }));

    const header = screen.getByRole('row');
    expect(within(header).getByText('Vehículo')).toBeInTheDocument();
    expect(within(header).getByText('Paso')).toBeInTheDocument();
  });

  it('al activar una columna compuesta, el dato se MUEVE a su columna en vez de duplicarse', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      { ...item, vehiculoMarca: 'RENAULT', vehiculoLinea: 'STEPWAY' },
    ]);
    render(<TramitesTable />);
    await screen.findByText('P0001');

    const rows = () => screen.getByRole('list', { name: 'Trámites en curso' });
    // Por defecto el vehículo va apilado bajo la placa: aparece UNA vez.
    expect(within(rows()).getAllByText('RENAULT STEPWAY')).toHaveLength(1);

    await userEvent.click(screen.getByRole('button', { name: /Columnas/i }));
    await userEvent.click(screen.getByRole('checkbox', { name: /^Vehículo$/i }));

    // Con la columna dedicada activa sigue apareciendo UNA sola vez, ahora en su propia celda.
    expect(within(rows()).getAllByText('RENAULT STEPWAY')).toHaveLength(1);
  });

  it('proyecta gestor, fuente y fecha de actualización cuando el usuario las activa', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      {
        ...item,
        companiaNombre: 'Empresa Gestora SAS',
        gestorNombre: 'Ana Gestora',
        fuente: 'integracion',
        // Media mañana UTC: el día calendario en Bogotá (UTC-5) es el mismo, así que la aserción
        // no depende del desfase de zona que aplica `formatFecha`.
        updatedAt: '2026-07-20T15:00:00Z',
      },
    ]);
    render(<TramitesTable />);

    await screen.findByText('P0001');
    // Gestor y Fuente ya vienen en el default; solo hay que activar la fecha de actualización,
    // que por defecto va apilada dentro del radicado.
    await userEvent.click(screen.getByRole('button', { name: /Columnas/i }));
    await userEvent.click(screen.getByRole('checkbox', { name: /Fecha de actualización/i }));

    const rows = screen.getByRole('list', { name: 'Trámites en curso' });
    expect(within(rows).getByText('Empresa Gestora SAS')).toBeInTheDocument();
    expect(within(rows).getByText('Ana Gestora')).toBeInTheDocument();
    expect(within(rows).getByText('Integración')).toBeInTheDocument();
    expect(within(rows).getByText('2026/07/20')).toBeInTheDocument();
  });

  // Las firmas de las dos partes viven en UNA sola columna ("Firmas"), cada una con su rótulo:
  // apiladas sin rótulo no se sabría de quién es cada chip.
  it('en traspaso muestra la acreditación de vendedor y comprador, cada una rotulada', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      {
        ...item,
        modalidad: 'traspaso',
        firmaVendedorEstado: 'firmado',
        firmaCompradorEstado: 'rechazado',
      },
    ]);
    render(<TramitesTable />);

    await screen.findByText('P0001');
    const rows = screen.getByRole('list', { name: 'Trámites en curso' });
    expect(within(rows).getByText('Vendedor:')).toBeInTheDocument();
    expect(within(rows).getByText('Comprador:')).toBeInTheDocument();
    expect(within(rows).getByText('Firmado')).toBeInTheDocument();
    expect(within(rows).getByText('Rechazado')).toBeInTheDocument();
    // El chip ya no se repite junto al nombre del actor: existe una sola vez por parte.
    expect(within(rows).getAllByText('Firmado')).toHaveLength(1);
  });

  // La columna del vendedor se ata a la pestaña, no a la preferencia: en matrícula inicial no
  // existe vendedor, pero en "Todos" la lista mezcla ambas modalidades y el dato sí aplica.
  it('la columna Propietario / vendedor sigue a la pestaña sin alterar la preferencia guardada', async () => {
    // Una instancia de cada modalidad: si no, al cambiar de pestaña el listado queda vacío y no
    // hay cabecera que comprobar (el caso se estaría midiendo contra el estado "Sin resultados").
    const [uno, dos] = makeInstances(2);
    mocks.listInstances.mockResolvedValue([
      { ...uno, modalidad: 'matricula_inicial' },
      { ...dos, modalidad: 'traspaso' },
    ]);
    render(<TramitesTable />);
    await screen.findByText('P0001');

    const header = () => screen.getByRole('row');
    // Pestaña "Todos": presente.
    expect(within(header()).getByText('Propietario / vendedor')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Matrícula inicial' }));
    expect(within(header()).queryByText('Propietario / vendedor')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Traspaso' }));
    expect(within(header()).getByText('Propietario / vendedor')).toBeInTheDocument();

    // Y al volver a "Todos" reaparece: nunca se tocó la preferencia, solo se derivó la vista.
    await userEvent.click(screen.getByRole('tab', { name: 'Todos' }));
    expect(within(header()).getByText('Propietario / vendedor')).toBeInTheDocument();
  });

  it('en matrícula inicial solo aparece la línea del comprador (no hay vendedor)', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      {
        ...item,
        modalidad: 'matricula_inicial',
        vendedorNombre: null,
        firmaVendedorEstado: null,
        firmaCompradorEstado: null,
      },
    ]);
    render(<TramitesTable />);

    await screen.findByText('P0001');
    const rows = screen.getByRole('list', { name: 'Trámites en curso' });
    expect(within(rows).queryByText('Vendedor:')).toBeNull();
    expect(within(rows).getByText('Comprador:')).toBeInTheDocument();
    // Parte existente pero sin acreditación registrada: se dice, no se deja un guion mudo.
    expect(within(rows).getByText('Sin registrar')).toBeInTheDocument();
  });
});

// HU #11054 — documentos del expediente desde el listado; HU #11055 — consolidado en la fila.
describe('TramitesTable — documentos y consolidado desde el listado', () => {
  it('abre el panel de documentos sin navegar al wizard', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    mocks.getAttachments.mockResolvedValue([
      {
        id: 'att-1',
        tipo: 'fur',
        filename: 'fur.pdf',
        mimetype: 'application/pdf',
        sizeBytes: 1024,
        sha256: 'abc',
        source: 'system',
        uploadedAt: '2026-07-01T00:00:00Z',
      },
    ]);
    render(<TramitesTable />);

    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Ver documentos' }));

    expect(await screen.findByText('Documentos · TR-0001')).toBeInTheDocument();
    expect(await screen.findByText('FUR')).toBeInTheDocument();
    expect(mocks.getAttachments).toHaveBeenCalledWith('inst-0001', undefined);
    // La acción vive dentro de una fila clickable: no debe abrir el trámite.
    expect(routerPush).not.toHaveBeenCalled();
  });

  it('informa cuando el trámite no tiene documentos', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    mocks.getAttachments.mockResolvedValue([]);
    render(<TramitesTable />);

    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Ver documentos' }));

    expect(
      await screen.findByText(/aún no tiene documentos en el expediente/),
    ).toBeInTheDocument();
  });

  it('sin consolidado generado la fila NO ofrece la acción', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    render(<TramitesTable />);

    await screen.findByText('P0001');
    await abrirAcciones();
    // El item no existe en el menu: la accion se OMITE, no se muestra deshabilitada.
    expect(screen.queryByRole('menuitem', { name: /consolidado/i })).toBeNull();
    expect(screen.getByRole('menuitem', { name: 'Ver documentos' })).toBeInTheDocument();
  });

  it('con el consolidado generado lo abre en el visor sin navegar', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      { ...item, consolidadoAttachmentId: 'att-consolidado' },
    ]);
    mocks.fetchAttachmentPreviewUrl.mockResolvedValue({
      url: 'https://s3.local/consolidado.pdf',
      expiresAt: '2026-07-29T00:10:00Z',
    });
    render(<TramitesTable />);

    await screen.findByText('P0001');
    await abrirAcciones();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Ver consolidado' }));

    expect(await screen.findByText('Expediente consolidado')).toBeInTheDocument();
    // Se abre con el id que trae el resumen: no se consultan los adjuntos del trámite.
    expect(mocks.fetchAttachmentPreviewUrl).toHaveBeenCalledWith(
      'inst-0001',
      'att-consolidado',
      undefined,
    );
    expect(mocks.getAttachments).not.toHaveBeenCalled();
    expect(routerPush).not.toHaveBeenCalled();
  });
});

describe('TramitesTable — pausa ICT (pauseDraftProcess / starts_procedure_in_paused)', () => {
  it('muestra el badge "Pausado" con la observación en el label cuando isPaused', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      { ...item, isPaused: true, pausedObservation: 'En espera de liquidación' },
    ]);
    render(<TramitesTable />);

    await screen.findByText('P0001');
    const badge = screen.getByLabelText('Trámite pausado: En espera de liquidación');
    expect(badge).toBeInTheDocument();
    expect(badge).toHaveTextContent('Pausado');
  });

  it('no muestra el badge "Pausado" cuando el trámite no está pausado (default)', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1)); // isPaused undefined ⇒ sin badge
    render(<TramitesTable />);

    await screen.findByText('P0001');
    expect(screen.queryByText('Pausado')).toBeNull();
  });

  it('en un borrador ICT el menú de acciones ofrece "Pausar" y al elegirlo llama a pauseInstance (optimista → "Reanudar")', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      { ...item, id: 'ict1', referenceNumber: 'TR-ICT', placa: 'ICT001', origin: 'ict', estado: 'borrador', isPaused: false },
    ]);
    mocks.pauseInstance.mockResolvedValue({ id: 'ict1', isPaused: true, pausedObservation: null });
    render(<TramitesTable />);

    await userEvent.click(await screen.findByRole('button', { name: 'Acciones del trámite TR-ICT' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Pausar' }));

    expect(mocks.pauseInstance).toHaveBeenCalledWith('ict1', true, null, undefined);
    // Optimista: reabrir el menú ahora ofrece "Reanudar".
    await userEvent.click(await screen.findByRole('button', { name: 'Acciones del trámite TR-ICT' }));
    expect(await screen.findByRole('menuitem', { name: 'Reanudar' })).toBeInTheDocument();
  });

  it('un trámite de plataforma (sin origin ict) no ofrece "Pausar" en el menú ni checkbox de selección', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([{ ...item, referenceNumber: 'TR-PLT', placa: 'PLT001', estado: 'borrador' }]);
    render(<TramitesTable />);

    await screen.findByText('PLT001');
    expect(screen.queryByRole('checkbox')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Acciones del trámite TR-PLT' }));
    expect(screen.queryByRole('menuitem', { name: 'Pausar' })).toBeNull();
  });

  it('al continuar un trámite PAUSADO abre un modal FLIT (no confirm nativo): cancelar no navega, confirmar sí', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      { ...item, id: 'pz', referenceNumber: 'TR-PZ', placa: 'PZ0001', origin: 'ict', estado: 'borrador', isPaused: true },
    ]);
    render(<TramitesTable />);

    await userEvent.click(await screen.findByRole('button', { name: 'Acciones del trámite TR-PZ' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Continuar' }));

    // Modal de diseño FLIT (role=dialog), no window.confirm.
    const dialog = await screen.findByRole('dialog', { name: /Trámite pausado/i });
    expect(routerPush).not.toHaveBeenCalled();

    // Cancelar cierra sin navegar.
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    expect(routerPush).not.toHaveBeenCalled();

    // Reabrir menú → Continuar → confirmar navega.
    await userEvent.click(await screen.findByRole('button', { name: 'Acciones del trámite TR-PZ' }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Continuar' }));
    const dialog2 = await screen.findByRole('dialog', { name: /Trámite pausado/i });
    await userEvent.click(within(dialog2).getByRole('button', { name: /Continuar de todos modos/ }));
    expect(routerPush).toHaveBeenCalled();
  });
});

// El trámite puede avanzar a Terminado CON salvedades (compañía que permite continuar sin SOAT
// vigente). Enviarlo al OT en silencio en ese caso es el bug que esta suite cubre.
describe('TramitesTable — advertencias al procesar (SOAT no vigente)', () => {
  const procesable = () => {
    const [item] = makeInstances(1);
    return { ...item, id: 'proc1', referenceNumber: 'TR-PROC', placa: 'PRC001', estado: 'entregado', plateFlowStatus: 'asignado' } satisfies InstanceSummary;
  };

  it('cuando hay alerta de procesar, el menú destaca la opción Procesar', async () => {
    mocks.listInstances.mockResolvedValue([procesable()]);
    render(<TramitesTable />);

    await userEvent.click(
      await screen.findByRole('button', {
        name: /Acciones del trámite TR-PROC.*Pendiente por procesar/,
      }),
    );
    const procesar = await screen.findByRole('menuitem', { name: 'Procesar' });
    expect(procesar).toHaveClass('bg-amber-50');
  });

  async function procesar() {
    mocks.listInstances.mockResolvedValue([procesable()]);
    render(<TramitesTable />);
    // Con la fila pendiente por procesar, ActionsMenu añade el hint al nombre accesible.
    await userEvent.click(
      await screen.findByRole('button', { name: /Acciones del trámite TR-PROC/ }),
    );
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Procesar' }));
    await userEvent.click(screen.getByRole('button', { name: 'Marcar como Terminado' }));
  }

  it('muestra la advertencia del backend y deja el modal abierto', async () => {
    mocks.completePlateFlow.mockResolvedValue({
      instance: null,
      warningCode: 'soat_no_vigente_advertencia',
      warningMessage: 'El trámite se envió al OT SIN SOAT vigente: el RUNT no lo reporta vigente.',
    });

    await procesar();

    const dialog = screen.getByRole('dialog', { name: 'Procesar trámite' });
    expect(within(dialog).getByRole('alert')).toHaveTextContent(/SIN SOAT vigente/);
    // La operación fue exitosa: ya no se puede reintentar, solo cerrar.
    expect(within(dialog).queryByRole('button', { name: 'Marcar como Terminado' })).toBeNull();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Entendido' }));
    expect(screen.queryByRole('dialog', { name: 'Procesar trámite' })).toBeNull();
  });

  it('sin advertencia cierra el modal directamente', async () => {
    mocks.completePlateFlow.mockResolvedValue({
      instance: null,
      warningCode: null,
      warningMessage: null,
    });

    await procesar();

    expect(screen.queryByRole('dialog', { name: 'Procesar trámite' })).toBeNull();
  });
});

describe('TramitesTable — pausa masiva ICT (pause-unpause-massive)', () => {
  it('selecciona varios borradores ICT y los pausa en lote', async () => {
    const [b] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      { ...b, id: 'm1', placa: 'MAS001', origin: 'ict', estado: 'borrador' },
      { ...b, id: 'm2', placa: 'MAS002', origin: 'ict', estado: 'borrador' },
    ]);
    mocks.pauseInstancesMassive.mockResolvedValue({ total: 2, processed: 2, detail: [] });
    render(<TramitesTable />);

    await screen.findByText('MAS001');
    const checks = screen.getAllByRole('checkbox');
    expect(checks).toHaveLength(2);

    await userEvent.click(checks[0]);
    await userEvent.click(checks[1]);
    expect(screen.getByText('2 seleccionados')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Pausar' }));
    expect(mocks.pauseInstancesMassive).toHaveBeenCalledWith(['m1', 'm2'], true, null, undefined);
  });
});

describe('TramitesTable — filtros y ordenamiento server-side', () => {
  async function abrirFiltros() {
    await userEvent.click(screen.getByRole('button', { name: /^Filtros/i }));
  }

  it('aplica filtros de placa, actores, gestor, firmado y fechas al listado', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    render(<TramitesTable />);
    await screen.findByText('P0001');
    expect(mocks.listInstances).toHaveBeenCalledWith(undefined);

    await abrirFiltros();
    await userEvent.type(screen.getByLabelText('Filtrar por placa'), 'ABC123');
    await userEvent.type(screen.getByLabelText('Filtrar por propietario o vendedor'), 'Pérez');
    await userEvent.type(screen.getByLabelText('Filtrar por comprador'), 'García');
    await userEvent.type(screen.getByLabelText('Filtrar por gestor'), 'Ana');
    await userEvent.selectOptions(screen.getByLabelText('Filtrar por firma de compraventa'), 'true');
    await userEvent.type(screen.getByLabelText('Fecha de creación desde'), '2026-01-01');
    await userEvent.type(screen.getByLabelText('Fecha de creación hasta'), '2026-01-31');
    await userEvent.type(screen.getByLabelText('Fecha de actualización desde'), '2026-02-01');
    await userEvent.type(screen.getByLabelText('Fecha de actualización hasta'), '2026-02-28');

    await userEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }));

    await vi.waitFor(() => {
      expect(mocks.listInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({
          placa: 'ABC123',
          vendedor: 'Pérez',
          comprador: 'García',
          gestor: 'Ana',
          firmado: true,
          createdFrom: '2026-01-01',
          createdTo: '2026-01-31',
          updatedFrom: '2026-02-01',
          updatedTo: '2026-02-28',
          take: 200,
          skip: 0,
        }),
      );
    });
  });

  it('mantiene los filtros colapsados por defecto y permite limpiarlos', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    render(<TramitesTable />);
    await screen.findByText('P0001');

    expect(screen.queryByLabelText('Filtrar por placa')).toBeNull();
    expect(screen.getByRole('button', { name: 'Limpiar filtros avanzados' })).toBeDisabled();

    await abrirFiltros();
    expect(screen.getByLabelText('Filtrar por placa')).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Filtrar por placa'), 'XYZ');
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }));

    await vi.waitFor(() => {
      expect(mocks.listInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({ placa: 'XYZ' }),
      );
    });

    await userEvent.click(screen.getByRole('button', { name: 'Limpiar filtros avanzados' }));
    await vi.waitFor(() => {
      expect(mocks.listInstances).toHaveBeenLastCalledWith(undefined);
    });
  });

  it('ordena por placa al hacer clic en la cabecera', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    render(<TramitesTable />);
    await screen.findByText('P0001');

    await userEvent.click(screen.getByRole('button', { name: /Ordenar por Placa/i }));

    await vi.waitFor(() => {
      expect(mocks.listInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({
          sortBy: 'placa',
          sortDir: 'asc',
          take: 200,
        }),
      );
    });

    await userEvent.click(screen.getByRole('button', { name: /Ordenar por Placa \(ascendente\)/i }));

    await vi.waitFor(() => {
      expect(mocks.listInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({
          sortBy: 'placa',
          sortDir: 'desc',
        }),
      );
    });
  });

  it('ordena por comprador y fecha de creación desde cabeceras visibles', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    render(<TramitesTable />);
    await screen.findByText('P0001');

    await userEvent.click(screen.getByRole('button', { name: /Ordenar por Comprador/i }));
    await vi.waitFor(() => {
      expect(mocks.listInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({ sortBy: 'comprador', sortDir: 'asc' }),
      );
    });

    // La fecha de creación va apilada bajo el radicado en el default: para ordenar por ella hay
    // que sacarla a su propia columna, que es donde vive la cabecera ordenable.
    await userEvent.click(screen.getByRole('button', { name: /Columnas/i }));
    await userEvent.click(screen.getByRole('checkbox', { name: /^Fecha de creación$/i }));

    await userEvent.click(screen.getByRole('button', { name: /Ordenar por Fecha de creación/i }));
    await vi.waitFor(() => {
      expect(mocks.listInstances).toHaveBeenLastCalledWith(
        expect.objectContaining({ sortBy: 'createdAt', sortDir: 'asc' }),
      );
    });
  });
});
