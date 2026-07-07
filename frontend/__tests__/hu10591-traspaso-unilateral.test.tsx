import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';

import type {
  ChecklistView,
  InstanceSummary,
} from '@/lib/api/types/procedure-runtime';

// HU #10591 (Feature #10584 · Traspaso unilateral, R11) — el FRONTEND expone la
// modalidad `traspaso_unilateral` en el arranque del wizard y en los listados:
//   1) la ruta /tramites/nuevo/traspaso_unilateral NO hace notFound() (guard);
//   2) el checklist específico (4 docs del unilateral) se pinta desde el backend;
//   3) el chip/label de la modalidad aparece en el listado, con su botón de
//      arranque y su chip de filtro.
// El backend (#10590) deriva la tipología; el FE solo pasa el string.

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  setPriority: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  getInstance: vi.fn(),
  analyzeDocument: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

// La creación de instancia en la ruta se aísla: el guard es lo que se ejercita.
vi.mock('@/hooks/useProcedureInstance', () => ({
  useProcedureInstance: () => ({
    state: { error: null },
    start: vi.fn().mockResolvedValue(null),
  }),
}));

// next/navigation: la tabla usa useRouter; la ruta usa useParams/useSearchParams/
// notFound. `notFound()` lanza (como en Next) para poder afirmar el 404 del guard.
const routerPush = vi.hoisted(() => vi.fn());
const notFoundSpy = vi.hoisted(() => vi.fn(() => {
  throw new Error('NEXT_NOT_FOUND');
}));
const currentModalidad = vi.hoisted(() => ({ value: 'traspaso_unilateral' }));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
  useParams: () => ({ modalidad: currentModalidad.value }),
  useSearchParams: () => ({ get: () => null }),
  notFound: notFoundSpy,
}));

import NuevoTramitePage from '@/app/tramites/nuevo/[modalidad]/page';
import { DocumentChecklist } from '@/components/operacion/DocumentChecklist';
import { TramitesTable } from '@/components/operacion/TramitesTable';

beforeEach(() => {
  vi.clearAllMocks();
});

// ── 1) Guard de ruta ────────────────────────────────────────────────
describe('HU #10591 — guard de la ruta /tramites/nuevo/[modalidad]', () => {
  it('NO hace notFound() para la modalidad traspaso_unilateral (renderiza el arranque)', () => {
    currentModalidad.value = 'traspaso_unilateral';
    render(<NuevoTramitePage />);

    expect(notFoundSpy).not.toHaveBeenCalled();
    expect(screen.getByText('Iniciando trámite…')).toBeInTheDocument();
  });

  it('hace notFound() para una modalidad desconocida (contraprueba del guard)', () => {
    currentModalidad.value = 'modalidad_invalida';
    expect(() => render(<NuevoTramitePage />)).toThrow('NEXT_NOT_FOUND');
    expect(notFoundSpy).toHaveBeenCalled();
  });
});

// ── 2) Checklist específico del unilateral (server-driven, sin cambios de UI) ──
const CHECKLIST_UNILATERAL: ChecklistView = {
  items: [
    { key: 'paz_salvo_locatario', label: 'Paz y salvo del locatario', obligatorio: true, docTipo: 'paz_salvo_locatario', satisfied: false },
    { key: 'doc_locatario', label: 'Documento del locatario', obligatorio: true, docTipo: 'doc_locatario', satisfied: false },
    { key: 'contrato_leasing', label: 'Contrato de leasing', obligatorio: true, docTipo: 'contrato_leasing', satisfied: false },
    { key: 'declaracion_arrendadora', label: 'Declaración de la arrendadora', obligatorio: true, docTipo: 'declaracion_arrendadora', satisfied: false },
  ],
  faltanObligatorios: 4,
  completo: false,
};

describe('HU #10591 — DocumentChecklist pinta los ítems del traspaso unilateral', () => {
  beforeEach(() => {
    mocks.getChecklist.mockResolvedValue(CHECKLIST_UNILATERAL);
    mocks.getAttachments.mockResolvedValue([]);
    mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  });

  it('renderiza los 4 documentos obligatorios del checklist unilateral', async () => {
    render(<DocumentChecklist instanceId="inst-uni" modalidad="traspaso_unilateral" />);

    expect(await screen.findByText('Paz y salvo del locatario')).toBeInTheDocument();
    expect(screen.getByText('Documento del locatario')).toBeInTheDocument();
    expect(screen.getByText('Contrato de leasing')).toBeInTheDocument();
    expect(screen.getByText('Declaración de la arrendadora')).toBeInTheDocument();
    // Resumen de obligatorios pendientes (los 4 son obligatorios).
    expect(screen.getByText(/Faltan 4 obligatorios/)).toBeInTheDocument();
  });
});

// ── 3) Listado: chip de modalidad + arranque + filtro ───────────────
function unilateralInstance(over: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: 'uni-1',
    referenceNumber: 'TU-0001',
    modalidad: 'traspaso_unilateral',
    estado: 'borrador',
    placa: 'UNI001',
    vin: 'VIN-UNI',
    vehiculoMarca: 'Renault',
    vehiculoLinea: 'Kwid',
    compradorNombre: 'Locatario SA',
    compradorDocumento: '900123',
    organismoTransito: null,
    pasoActual: 3,
    totalPasos: 5,
    createdAt: '2026-07-07T00:00:00Z',
    draftFinalizedAt: null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: false,
    prioritario: false,
    tenantId: '11111111-1111-1111-1111-111111111111',
    companiaNombre: null,
    ...over,
  };
}

describe('HU #10591 — la modalidad unilateral aparece en el listado', () => {
  it('muestra el chip "T. unilateral" y el paso "Arrendadora" en la fila', async () => {
    mocks.listInstances.mockResolvedValue([unilateralInstance()]);
    render(<TramitesTable />);

    const row = (await screen.findByText('UNI001')).closest('[role="button"]') as HTMLElement;
    expect(within(row).getByText('T. unilateral')).toBeInTheDocument();
    // pasoActual=3 → STEP_LABELS.traspaso_unilateral[2] = "Arrendadora".
    expect(within(row).getByText('Arrendadora')).toBeInTheDocument();
    expect(within(row).getByText('3/5')).toBeInTheDocument();
  });

  it('ofrece el botón de arranque y el chip de filtro del traspaso unilateral', async () => {
    mocks.listInstances.mockResolvedValue([unilateralInstance()]);
    render(<TramitesTable />);

    await screen.findByText('UNI001');
    // Botón de arranque (NEW_TRAMITE_ACTIONS) — expone su acción por aria-label.
    expect(
      screen.getByRole('button', { name: 'Iniciar Traspaso unilateral' }),
    ).toBeInTheDocument();
    // Chip de filtro por modalidad (MODALIDAD_CHIPS) dentro del grupo de filtros.
    const filtro = screen.getByRole('group', { name: 'Filtrar por modalidad' });
    expect(within(filtro).getByText('Traspaso unilateral')).toBeInTheDocument();
  });
});
