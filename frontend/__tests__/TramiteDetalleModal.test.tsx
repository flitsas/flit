import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { TramiteDetalleModal } from '@/components/operacion/TramiteDetalleModal';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getInstance: vi.fn(),
    getAttachments: vi.fn(),
    listBiometricExpediente: vi.fn(),
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
  plateFlowStatus: null,
  ultimoRechazoMotivo: null,
  isPaused: false,
  pausedObservation: null,
  firmaVendedorEstado: 'firmado',
  firmaCompradorEstado: 'pendiente',
  consolidadoAttachmentId: null,
} satisfies InstanceSummary;

describe('TramiteDetalleModal', () => {
  beforeEach(() => {
    vi.mocked(tramitesClient.getInstance).mockResolvedValue({
      statusHistory: [
        { fromStatus: null, toStatus: 'borrador', changedAt: '2026-04-18T08:00:00Z', reason: null },
        { fromStatus: 'borrador', toStatus: 'entregado', changedAt: '2026-04-20T11:00:00Z', reason: null },
      ],
      fieldValues: [],
      actors: [],
    } as never);
    vi.mocked(tramitesClient.getAttachments).mockResolvedValue([]);
    vi.mocked(tramitesClient.listBiometricExpediente).mockResolvedValue({
      validations: [],
      firmaBaulPartes: ['vendedor'],
    });
  });

  it('muestra header mockup con título, chip y toggles', async () => {
    render(
      <TramiteDetalleModal open instanceId="inst-1" item={ITEM} onClose={() => undefined} />,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('heading', { name: /Detalle de traspaso/i })).toBeInTheDocument();
    expect(within(dialog).getByText('Entregado')).toBeInTheDocument();
    expect(dialog).toHaveTextContent('RAD-001');
    expect(dialog).toHaveTextContent('ABC123');
    expect(within(dialog).getByRole('button', { name: /Trazabilidad de Identidad/i })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /Línea de Tiempo del Trámite/i })).toBeInTheDocument();
  });

  it('toggle línea de tiempo reemplaza el body del grid', async () => {
    const user = userEvent.setup();
    render(
      <TramiteDetalleModal open instanceId="inst-1" item={ITEM} onClose={() => undefined} />,
    );
    await user.click(screen.getByRole('button', { name: /Línea de Tiempo del Trámite/i }));
    expect(await screen.findByText('Línea de tiempo del trámite')).toBeInTheDocument();
    expect(screen.queryByLabelText('Datos del vehículo')).not.toBeInTheDocument();
  });

  it('toggle identidad muestra firma del baúl en nodos', async () => {
    const user = userEvent.setup();
    render(
      <TramiteDetalleModal open instanceId="inst-1" item={ITEM} onClose={() => undefined} />,
    );
    await user.click(screen.getByRole('button', { name: /Trazabilidad de Identidad/i }));
    expect(await screen.findByText('Trazabilidad de identidad')).toBeInTheDocument();
    expect(await screen.findByText(/Firma del baúl/i)).toBeInTheDocument();
  });

  it('click en step cierra panel de tracking', async () => {
    const user = userEvent.setup();
    render(
      <TramiteDetalleModal open instanceId="inst-1" item={ITEM} onClose={() => undefined} />,
    );
    await user.click(screen.getByRole('button', { name: /Línea de Tiempo del Trámite/i }));
    expect(await screen.findByText('Línea de tiempo del trámite')).toBeInTheDocument();
    const tablist = screen.getByRole('tablist', { name: /Pasos del trámite/i });
    await user.click(within(tablist).getByRole('tab', { name: /Trámite y vehículo/i }));
    expect(screen.queryByText('Línea de tiempo del trámite')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Datos del vehículo')).toBeInTheDocument();
  });
});
