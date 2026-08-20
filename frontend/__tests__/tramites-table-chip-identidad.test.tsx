// HU #11668 — el chip de identidad del listado explica de dónde puede venir la acreditación.
//
// Desde la HU #11667 la ruta de lote acredita también por firma del baúl, así que «Identidad
// validada» ya no implica que exista un certificado de validación. El resumen de la fila NO
// distingue el origen (ver AYUDA_ORIGEN en TramitesTable): lo que se verifica aquí es que el chip
// nombra las dos vías, aclara el alcance de los estados no terminales, es alcanzable por teclado y
// no habla del baúl donde no corresponde.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  setPriority: vi.fn(),
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  getInstance: vi.fn(),
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  completePlateFlow: vi.fn(),
  getConsultationConfig: vi.fn(),
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

// Uso de ejemplo: instancia({ identityValidationStatus: 'aprobado' }) → fila con chip acreditado.
function instancia(overrides: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: 'inst-0001',
    referenceNumber: 'TR-0001',
    modalidad: 'traspaso',
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
    draftFinalizedAt: '2026-06-20T10:00:00Z',
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
  } satisfies InstanceSummary;
}

async function filaConChip(item: InstanceSummary) {
  mocks.listInstances.mockResolvedValue([item]);
  render(<TramitesTable />);
  const row = (await screen.findByText(item.placa as string)).closest('tr') as HTMLElement;
  return row;
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
});

describe('TramitesTable — chip de identidad y origen de la acreditación (HU #11668)', () => {
  it('AC1 — acreditación aprobada: la ayuda nombra la firma del baúl como origen posible', async () => {
    const user = userEvent.setup();
    const row = await filaConChip(
      instancia({ identityValidationStatus: 'aprobado', canSubmit: false }),
    );

    const chip = within(row).getByLabelText('Estado: Identidad validada');
    await user.hover(chip.parentElement as HTMLElement);

    const tip = within(row).getByRole('tooltip');
    expect(tip).toHaveTextContent(/firma del baúl/i);
    expect(tip).toHaveTextContent(/no hay certificado de validación que descargar/i);
  });

  it('AC2 — la ayuda aclara que los estados no terminales salen de este trámite', async () => {
    const user = userEvent.setup();
    const row = await filaConChip(instancia({ identityValidationStatus: 'en_proceso' }));

    const chip = within(row).getByLabelText('Estado: Pendiente validación');
    await user.hover(chip.parentElement as HTMLElement);

    expect(within(row).getByRole('tooltip')).toHaveTextContent(
      /estados en curso y rechazado se calculan solo con las validaciones propias de este trámite/i,
    );
  });

  it('AC3 — el chip es alcanzable por teclado y la ayuda se anuncia como descripción', async () => {
    const row = await filaConChip(instancia({ identityValidationStatus: 'aprobado' }));

    const contenedor = within(row).getByLabelText('Estado: Identidad validada')
      .parentElement as HTMLElement;
    // Alcanzable por teclado: entra en el orden de tabulación y el foco abre la ayuda.
    expect(contenedor).toHaveAttribute('tabindex', '0');

    await act(async () => {
      contenedor.focus();
    });
    expect(contenedor).toHaveFocus();

    const tip = within(row).getByRole('tooltip');
    expect(contenedor).toHaveAttribute('aria-describedby', tip.id);
  });

  it('AC3 — el significado no depende del color: el texto del chip sigue nombrando el estado', async () => {
    const row = await filaConChip(instancia({ identityValidationStatus: 'aprobado' }));
    expect(within(row).getByText('Identidad validada')).toBeInTheDocument();
  });

  it('AC4 (negativo) — con la validación en curso el chip NO menciona el baúl', async () => {
    const user = userEvent.setup();
    const row = await filaConChip(instancia({ identityValidationStatus: 'en_proceso' }));

    const chip = within(row).getByLabelText('Estado: Pendiente validación');
    await user.hover(chip.parentElement as HTMLElement);

    expect(within(row).getByRole('tooltip')).not.toHaveTextContent(/baúl/i);
  });

  it('AC4 (negativo) — una validación rechazada tampoco habla del baúl', async () => {
    const user = userEvent.setup();
    const row = await filaConChip(instancia({ identityValidationStatus: 'rechazado' }));

    const chip = within(row).getByLabelText('Estado: Validación rechazada');
    await user.hover(chip.parentElement as HTMLElement);

    expect(within(row).getByRole('tooltip')).not.toHaveTextContent(/baúl/i);
  });

  it('contrato — el chip base de estado (trámite ya entregado) no crece un tooltip', async () => {
    const user = userEvent.setup();
    const row = await filaConChip(
      instancia({ estado: 'entregado', draftFinalizedAt: null, identityValidationStatus: 'aprobado' }),
    );

    const chip = within(row).getByLabelText(/^Estado: /);
    await user.hover(chip);
    expect(within(row).queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('contrato — la ayuda solo se pinta mientras hay foco o puntero encima', async () => {
    const user = userEvent.setup();
    const row = await filaConChip(instancia({ identityValidationStatus: 'aprobado' }));

    expect(within(row).queryByRole('tooltip')).not.toBeInTheDocument();
    const contenedor = within(row).getByLabelText('Estado: Identidad validada')
      .parentElement as HTMLElement;
    await user.hover(contenedor);
    expect(within(row).getByRole('tooltip')).toBeInTheDocument();
    await user.unhover(contenedor);
    expect(within(row).queryByRole('tooltip')).not.toBeInTheDocument();
  });
});
