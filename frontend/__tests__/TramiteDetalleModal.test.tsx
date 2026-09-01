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
    startSubsanacion: vi.fn(),
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
      firmaBaulActores: [],
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

/**
 * El modal de detalle es la vista de TODO trámite ya radicado, así que es donde el gestor se topa
 * con un rechazo. Activar la subsanación es la única salida de ese estado y por eso vive aquí,
 * dentro del aviso que explica el bloqueo; la edición sigue siendo del asistente de pasos.
 */
describe('TramiteDetalleModal — subsanación', () => {
  const RECHAZADO = {
    ...ITEM,
    estado: 'rechazado',
    ultimoRechazoMotivo: 'Rechazo de prueba: expediente completo.',
  } satisfies InstanceSummary;

  // Bloque autónomo: el `beforeEach` del describe de arriba no alcanza hasta aquí, y sin las
  // cargas del detalle el modal reventaría antes de pintar el aviso.
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(tramitesClient.getInstance).mockResolvedValue({
      statusHistory: [],
      fieldValues: [],
      actors: [],
    } as never);
    vi.mocked(tramitesClient.getAttachments).mockResolvedValue([]);
    vi.mocked(tramitesClient.listBiometricExpediente).mockResolvedValue({
      validations: [],
      firmaBaulPartes: [],
      firmaBaulActores: [],
    });
    vi.mocked(tramitesClient.startSubsanacion).mockResolvedValue({
      id: 'inst-1',
      status: 'rechazado',
      subsanacionActiva: true,
    } as never);
  });

  it('sobre un rechazado ofrece "Subsanar trámite" y salta al asistente tras activarlo', async () => {
    const user = userEvent.setup();
    const onAbrirAsistente = vi.fn();
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-1"
        item={RECHAZADO}
        onClose={() => undefined}
        onAbrirAsistente={onAbrirAsistente}
      />,
    );

    const aviso = screen.getByText('Rechazado por el Organismo de Tránsito').closest('[role="alert"]');
    expect(aviso).not.toBeNull();
    await user.click(within(aviso as HTMLElement).getByRole('button', { name: /Subsanar trámite/i }));

    expect(tramitesClient.startSubsanacion).toHaveBeenCalledWith('inst-1', undefined);
    expect(onAbrirAsistente).toHaveBeenCalledWith(RECHAZADO);
  });

  it('con la subsanación ya activa retoma sin volver a llamar al POST', async () => {
    const user = userEvent.setup();
    const onAbrirAsistente = vi.fn();
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-1"
        item={{ ...RECHAZADO, subsanacionActiva: true }}
        onClose={() => undefined}
        onAbrirAsistente={onAbrirAsistente}
      />,
    );

    expect(screen.queryByRole('button', { name: /Subsanar trámite/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Continuar la subsanación/i }));

    // Reactivar un flag ya encendido devuelve 409: la UI se lo salta y solo navega.
    expect(tramitesClient.startSubsanacion).not.toHaveBeenCalled();
    expect(onAbrirAsistente).toHaveBeenCalled();
  });

  it('lleva el tenant de la fila cuando el SuperAdmin abre un trámite de otra compañía', async () => {
    const user = userEvent.setup();
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-1"
        item={RECHAZADO}
        tenantId="22222222-2222-2222-2222-222222222222"
        onClose={() => undefined}
        onAbrirAsistente={vi.fn()}
      />,
    );

    await user.click(screen.getByRole('button', { name: /Subsanar trámite/i }));
    expect(tramitesClient.startSubsanacion).toHaveBeenCalledWith(
      'inst-1',
      '22222222-2222-2222-2222-222222222222',
    );
  });

  it('si el POST falla lo dice en el aviso y no navega', async () => {
    const user = userEvent.setup();
    const onAbrirAsistente = vi.fn();
    vi.mocked(tramitesClient.startSubsanacion).mockRejectedValue(
      new Error('Solo un trámite en estado rechazado puede iniciar subsanación.'),
    );
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-1"
        item={RECHAZADO}
        onClose={() => undefined}
        onAbrirAsistente={onAbrirAsistente}
      />,
    );

    await user.click(screen.getByRole('button', { name: /Subsanar trámite/i }));

    expect(
      await screen.findByText('Solo un trámite en estado rechazado puede iniciar subsanación.'),
    ).toBeInTheDocument();
    expect(onAbrirAsistente).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /Subsanar trámite/i })).toBeEnabled();
  });

  it('un aprobado no ofrece subsanar (el backend solo lo permite sobre rechazado)', async () => {
    render(
      <TramiteDetalleModal
        open
        instanceId="inst-1"
        item={{ ...ITEM, estado: 'aprobado' }}
        onClose={() => undefined}
        onAbrirAsistente={vi.fn()}
      />,
    );

    expect(screen.queryByRole('button', { name: /Subsanar trámite/i })).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Continuar la subsanación/i }),
    ).not.toBeInTheDocument();
  });

  it('sin `onAbrirAsistente` no ofrece la acción: activar sin poder editar deja peor', async () => {
    render(
      <TramiteDetalleModal open instanceId="inst-1" item={RECHAZADO} onClose={() => undefined} />,
    );

    expect(screen.getByText('Rechazado por el Organismo de Tránsito')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Subsanar trámite/i })).not.toBeInTheDocument();
  });
});
