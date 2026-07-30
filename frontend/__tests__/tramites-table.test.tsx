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
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
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

beforeEach(() => {
  vi.clearAllMocks();
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
    expect(within(nav).getByText('1 / 3')).toBeInTheDocument();
    expect(within(nav).getByText('1–10 de 23')).toBeInTheDocument();
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
    expect(within(nav).getByText('3 / 3')).toBeInTheDocument();
    expect(within(nav).getByText('21–23 de 23')).toBeInTheDocument();
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
    expect(within(nav).getByText('2 / 3')).toBeInTheDocument();

    // Buscar "Comprador" matchea las 23 (siguen 3 páginas) pero resetea a la 1.
    // La búsqueda está oculta tras el botón "Buscar" (paridad con el diseño).
    await userEvent.click(screen.getByRole('button', { name: /Buscar por placa o VIN/i }));
    await userEvent.type(
      screen.getByRole('searchbox', { name: 'Buscar trámites' }),
      'Comprador',
    );
    expect(within(nav).getByText('1 / 3')).toBeInTheDocument();
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
    // Aún no se puede radicar → la acción sigue siendo "Continuar".
    expect(within(row).getByRole('button', { name: /Continuar/ })).toBeInTheDocument();
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
    expect(within(row).getByRole('button', { name: /Radicar trámite/ })).toBeInTheDocument();
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
    await userEvent.click(screen.getByRole('button', { name: /Buscar por placa o VIN/i }));
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

    await userEvent.click(screen.getByRole('button', { name: 'Prioritarios' }));

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
    expect(screen.getByText('Propietario / vendedor')).toBeInTheDocument();
    expect(screen.getByText('Comprador')).toBeInTheDocument();
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

// HU #11057 — columnas acordadas con el negocio.
describe('TramitesTable — columnas del listado (HU #11057)', () => {
  it('muestra las columnas acordadas con el negocio', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    render(<TramitesTable />);

    await screen.findByText('P0001');
    const header = screen.getByRole('row');
    for (const col of [
      'Radicado',
      'VIN',
      'Placa',
      'Trámite / Modalidad',
      'Propietario / vendedor',
      'Comprador',
      'Fecha de creación',
      'Fecha de actualización',
      'Secretaría',
      'Gestor',
      'Fuente',
      'Acciones',
    ]) {
      expect(within(header).getByText(col)).toBeInTheDocument();
    }
    // Dos columnas "Firmado" (vendedor y comprador), distinguidas por su sufijo accesible.
    expect(within(header).getAllByText(/^Firmado/)).toHaveLength(2);
    expect(within(header).getByText('(vendedor)')).toBeInTheDocument();
    expect(within(header).getByText('(comprador)')).toBeInTheDocument();
  });

  it('proyecta gestor, fuente y fecha de actualización de la fila', async () => {
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
    const rows = screen.getByRole('list', { name: 'Trámites en curso' });
    expect(within(rows).getByText('Empresa Gestora SAS')).toBeInTheDocument();
    expect(within(rows).getByText('Ana Gestora')).toBeInTheDocument();
    expect(within(rows).getByText('Integración')).toBeInTheDocument();
    expect(within(rows).getByText('2026/07/20')).toBeInTheDocument();
  });

  it('muestra el estado de acreditación de cada parte por separado', async () => {
    const [item] = makeInstances(1);
    mocks.listInstances.mockResolvedValue([
      { ...item, firmaVendedorEstado: 'firmado', firmaCompradorEstado: 'rechazado' },
    ]);
    render(<TramitesTable />);

    await screen.findByText('P0001');
    const rows = screen.getByRole('list', { name: 'Trámites en curso' });
    expect(within(rows).getByText('Firmado')).toBeInTheDocument();
    expect(within(rows).getByText('Rechazado')).toBeInTheDocument();
  });

  it('en matrícula inicial la columna del vendedor se presenta como no aplicable', async () => {
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
    // Sin chip de acreditación: el vendedor no existe y el comprador viene sin estado.
    expect(within(rows).queryByText('Pendiente')).toBeNull();
    expect(within(rows).getAllByTitle(/No aplica: este trámite no tiene/)).toHaveLength(2);
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
    await userEvent.click(
      screen.getByRole('button', { name: 'Ver documentos del trámite TR-0001' }),
    );

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
    await userEvent.click(
      screen.getByRole('button', { name: 'Ver documentos del trámite TR-0001' }),
    );

    expect(
      await screen.findByText(/aún no tiene documentos en el expediente/),
    ).toBeInTheDocument();
  });

  it('sin consolidado generado la fila NO ofrece la acción', async () => {
    mocks.listInstances.mockResolvedValue(makeInstances(1));
    render(<TramitesTable />);

    await screen.findByText('P0001');
    expect(
      screen.queryByRole('button', { name: /expediente consolidado/i }),
    ).toBeNull();
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
    await userEvent.click(
      screen.getByRole('button', {
        name: 'Ver expediente consolidado del trámite TR-0001',
      }),
    );

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
