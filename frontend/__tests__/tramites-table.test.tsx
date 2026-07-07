import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
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
      organismoTransito: null,
      pasoActual: 2,
      totalPasos: 6,
      createdAt: '2026-06-18T00:00:00Z',
      // HU #10350 — defaults: borrador no finalizado, sin validación async ni firma pendiente.
      draftFinalizedAt: null,
      identityValidationStatus: null,
      signaturePending: false,
      canSubmit: false,
      tenantId: '11111111-1111-1111-1111-111111111111',
      companiaNombre: null,
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
    signaturePending: false, canSubmit: false,
    tenantId: 't', companiaNombre: null, ...over,
  };
}

describe('TramitesTable — SuperAdmin multi-tenant', () => {
  beforeEach(() => {
    document.cookie = `flit_token=${superAdminToken()}; path=/`;
  });
  afterEach(() => {
    document.cookie = 'flit_token=; path=/; Max-Age=0';
  });

  it('muestra la columna Compañía, el filtro y abre con el tenant de la fila (?t=)', async () => {
    mocks.listInstances.mockResolvedValue([
      instance({ id: 'a', placa: 'AAA111', tenantId: 'ten-a', companiaNombre: 'Empresa A' }),
      instance({ id: 'b', placa: 'BBB222', tenantId: 'ten-b', companiaNombre: 'Empresa B' }),
    ]);
    render(<TramitesTable />);

    await screen.findByText('AAA111');
    // Columna Compañía (header de la grilla, role="row") + nombre por fila (dentro de la lista).
    expect(within(screen.getByRole('row')).getByText('Compañía')).toBeInTheDocument();
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
