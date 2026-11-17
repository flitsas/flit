import { render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TramiteDetalleModal } from '@/components/operacion/TramiteDetalleModal';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

/**
 * HU #14322 (Feature #14316, Épica #14296) — el detalle del trámite muestra el canal (y el OT del
 * punto) en solo lectura. Uso de ejemplo: un gestor abre un trámite que llegó por el punto de un OT y
 * ve «Canal: Persona Natural OT» y «OT del punto: <nombre>», sin ningún control para cambiarlos.
 */

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getCamaraComercioRequirements: vi.fn(() => Promise.resolve([])),
    getInstance: vi.fn(),
    getAttachments: vi.fn(),
    getStatusHistory: vi.fn(),
    listBiometricExpediente: vi.fn(),
    startSubsanacion: vi.fn(),
    requestRevocation: vi.fn(),
  },
}));

import { tramitesClient } from '@/lib/api/tramites-client';

const ITEM = {
  id: 'inst-1',
  referenceNumber: 'RAD-001',
  placa: 'ABC123',
  vin: 'VIN123',
  estado: 'entregado',
  modalidad: 'TRASPASO',
  tipoNombre: null,
  gestorNombre: 'Gestor Test',
  vehiculoMarca: 'Toyota',
  vehiculoLinea: 'Corolla',
  compradorNombre: 'Comprador Test',
  compradorDocumento: '1001',
  vendedorNombre: 'Vendedor Test',
  vendedorDocumento: '2001',
  organismoTransito: 'Secretaría Test',
  pasoActual: 3,
  totalPasos: 5,
  createdAt: '2026-04-18T08:00:00Z',
  updatedAt: '2026-04-20T11:00:00Z',
  draftFinalizedAt: null,
  identityValidationStatus: null,
  signaturePending: false,
  canSubmit: false,
  prioritario: false,
  tenantId: '11111111-1111-1111-1111-111111111111',
  companiaNombre: null,
  subsanacionActiva: false,
  ultimoRechazoMotivo: null,
  isPaused: false,
  pausedObservation: null,
  firmaVendedorEstado: 'firmado',
  firmaCompradorEstado: 'pendiente',
  consolidadoAttachmentId: null,
} satisfies InstanceSummary;

function detalle(extra: Record<string, unknown>) {
  vi.mocked(tramitesClient.getInstance).mockResolvedValue({
    statusHistory: [],
    fieldValues: [],
    actors: [],
    ...extra,
  } as never);
}

async function abrir() {
  render(<TramiteDetalleModal open instanceId="inst-1" item={ITEM} onClose={() => undefined} />);
  return screen.getByRole('dialog');
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(tramitesClient.getAttachments).mockResolvedValue([]);
  vi.mocked(tramitesClient.getStatusHistory).mockResolvedValue({
    items: [],
    total: 0,
    page: 1,
    pageSize: 20,
    modificaciones: [],
    modificacionesTotal: 0,
  });
  vi.mocked(tramitesClient.listBiometricExpediente).mockResolvedValue({
    validations: [],
    firmaBaulPartes: [],
    firmaBaulActores: [],
  });
});

// Rediseño (Epic #14471): Samuel pidió ocultar el campo «Canal» del detalle. Se conserva el OT del punto.
describe('HU #14322 — el canal no se muestra en el detalle', () => {
  it.each(['tramites', 'pn_web', 'integracion'] as const)('canal «%s»: sin campo Canal y sin OT', async (codigo) => {
    detalle({ channel: codigo, channelTransitOfficeId: null, channelTransitOfficeName: null });
    const dialog = await abrir();
    await waitFor(() => expect(tramitesClient.getInstance).toHaveBeenCalled());
    expect(within(dialog).queryByTestId('detalle-canal')).not.toBeInTheDocument();
    expect(within(dialog).queryByText(/^Canal/)).not.toBeInTheDocument();
    expect(within(dialog).queryByTestId('detalle-canal-ot')).not.toBeInTheDocument();
  });
});

describe('HU #14322 AC2 — Persona Natural OT', () => {
  it('muestra el OT del punto, sin el campo Canal', async () => {
    detalle({
      channel: 'pn_punto',
      channelTransitOfficeId: 'ot-1',
      channelTransitOfficeName: 'Tránsito de Envigado',
    });
    const dialog = await abrir();
    await waitFor(() =>
      expect(within(dialog).getByTestId('detalle-canal-ot')).toHaveTextContent('OT del punto: Tránsito de Envigado'),
    );
    expect(within(dialog).queryByTestId('detalle-canal')).not.toBeInTheDocument();
  });
});

describe('HU #14322 AC4 — sin edición', () => {
  it('el OT del punto es texto: ningún control (input, select, botón) lo envuelve ni lo edita', async () => {
    detalle({ channel: 'pn_punto', channelTransitOfficeName: 'Tránsito de Envigado' });
    const dialog = await abrir();
    const ot = await within(dialog).findByTestId('detalle-canal-ot');
    expect(ot.querySelector('input, select, textarea, button, [contenteditable]')).toBeNull();
    expect(ot.closest('button, a, label')).toBeNull();
    expect(within(dialog).queryByRole('combobox', { name: /canal/i })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: /canal/i })).not.toBeInTheDocument();
  });
});
