import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { ChecklistView, InstanceSummary, ProcedureAttachment } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

import { TramiteDetalleDocumentos } from '@/components/operacion/detalle/TramiteDetalleDocumentos';

const ITEM: InstanceSummary = {
  id: 'inst-1',
  referenceNumber: 'TR-001',
  modalidad: 'TRASPASO',
  estado: 'entregado',
  placa: 'ABC123',
  vin: 'VIN-XYZ-001',
  vehiculoMarca: 'Toyota',
  vehiculoLinea: 'Corolla',
  compradorNombre: 'Carlos Mendoza',
  compradorDocumento: '12345678',
  organismoTransito: 'Secretaría de Movilidad Bogotá',
  pasoActual: 6,
  totalPasos: 6,
  createdAt: '2026-06-18T00:00:00Z',
  draftFinalizedAt: null,
  identityValidationStatus: null,
  signaturePending: false,
  canSubmit: true,
  prioritario: false,
  tenantId: '11111111-1111-1111-1111-111111111111',
  companiaNombre: null,
};

function checklist(items: ChecklistView['items']): ChecklistView {
  return { items, faltanObligatorios: items.filter((i) => i.obligatorio && !i.satisfied).length, completo: items.every((i) => i.satisfied) };
}

const ATTACHMENT_SOAT: ProcedureAttachment = {
  id: 'att-soat',
  tipo: 'soat',
  filename: 'soat_vigente.pdf',
  mimetype: 'application/pdf',
  sizeBytes: 204800,
  sha256: 'abc123',
  source: 'user',
  uploadedAt: '2026-06-20T10:00:00Z',
};

const ATTACHMENT_EXTRA: ProcedureAttachment = {
  id: 'att-extra',
  tipo: 'factura',
  filename: 'factura_compra.pdf',
  mimetype: 'application/pdf',
  sizeBytes: 51200,
  sha256: 'def456',
  source: 'user',
  uploadedAt: '2026-06-21T09:30:00Z',
};

const ATTACHMENT_SYSTEM: ProcedureAttachment = {
  id: 'att-system',
  tipo: 'fur',
  filename: 'fur_generado.pdf',
  mimetype: 'application/pdf',
  sizeBytes: 30000,
  sha256: 'ghi789',
  source: 'system',
  uploadedAt: '2026-06-22T09:30:00Z',
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe('TramiteDetalleDocumentos', () => {
  it('muestra el estado de cargando mientras llega la respuesta', () => {
    mocks.getChecklist.mockReturnValue(new Promise(() => {}));
    mocks.getAttachments.mockReturnValue(new Promise(() => {}));

    const { container } = render(<TramiteDetalleDocumentos instanceId="inst-1" item={ITEM} />);

    const cargando = container.querySelector('[aria-label="Cargando documentos del trámite"]');
    expect(cargando).toBeInTheDocument();
    expect(cargando).toHaveAttribute('aria-busy', 'true');
  });

  it('muestra error con reintento y recarga al hacer clic', async () => {
    const user = userEvent.setup();
    mocks.getChecklist.mockRejectedValueOnce(new Error('Fallo de red'));
    mocks.getAttachments.mockResolvedValue([]);

    render(<TramiteDetalleDocumentos instanceId="inst-1" item={ITEM} />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Fallo de red');

    mocks.getChecklist.mockResolvedValueOnce(checklist([]));
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByText('Este trámite no tiene requisitos ni documentos registrados.')).toBeInTheDocument();
    expect(mocks.getChecklist).toHaveBeenCalledTimes(2);
  });

  it('muestra el estado vacío cuando no hay requisitos ni adjuntos', async () => {
    mocks.getChecklist.mockResolvedValue(checklist([]));
    mocks.getAttachments.mockResolvedValue([]);

    render(<TramiteDetalleDocumentos instanceId="inst-1" item={ITEM} />);

    expect(
      await screen.findByText('Este trámite no tiene requisitos ni documentos registrados.'),
    ).toBeInTheDocument();
  });

  it('el resumen cuenta N de M requisitos cumplidos sobre el checklist', async () => {
    mocks.getChecklist.mockResolvedValue(
      checklist([
        { key: 'soat', label: 'SOAT vigente', obligatorio: true, docTipo: 'soat', satisfied: true },
        { key: 'tecno', label: 'Revisión tecnomecánica', obligatorio: true, docTipo: 'tecno', satisfied: false },
        { key: 'poder', label: 'Poder autenticado', obligatorio: false, docTipo: 'poder', satisfied: false },
      ]),
    );
    mocks.getAttachments.mockResolvedValue([]);

    render(<TramiteDetalleDocumentos instanceId="inst-1" item={ITEM} />);

    expect(await screen.findByText('1 de 3 requisitos cumplidos')).toBeInTheDocument();
  });

  it('el estado de un requisito sale de satisfied, no de si hay un adjunto cargado', async () => {
    // El SOAT tiene un adjunto cargado, pero el checklist todavía NO lo marca satisfecho
    // (p. ej. está pendiente de validación). El badge debe leer "Pendiente", no "Cumplido".
    mocks.getChecklist.mockResolvedValue(
      checklist([
        { key: 'soat', label: 'SOAT vigente', obligatorio: true, docTipo: 'soat', satisfied: false },
      ]),
    );
    mocks.getAttachments.mockResolvedValue([ATTACHMENT_SOAT]);

    render(<TramiteDetalleDocumentos instanceId="inst-1" item={ITEM} />);

    const fila = await screen.findByText('SOAT vigente');
    const li = fila.closest('li');
    expect(li).not.toBeNull();
    expect(within(li as HTMLElement).getByText('Pendiente')).toBeInTheDocument();
    expect(within(li as HTMLElement).queryByText('Cumplido')).not.toBeInTheDocument();
    // El adjunto SÍ está cargado: el botón de descarga se ofrece igual, aunque el requisito no
    // esté satisfecho todavía.
    expect(
      within(li as HTMLElement).getByRole('button', { name: 'Descargar soat_vigente.pdf' }),
    ).toBeInTheDocument();
  });

  it('distingue un requisito opcional de uno obligatorio', async () => {
    mocks.getChecklist.mockResolvedValue(
      checklist([
        { key: 'soat', label: 'SOAT vigente', obligatorio: true, docTipo: 'soat', satisfied: true },
        { key: 'poder', label: 'Poder autenticado', obligatorio: false, docTipo: 'poder', satisfied: false },
      ]),
    );
    mocks.getAttachments.mockResolvedValue([]);

    render(<TramiteDetalleDocumentos instanceId="inst-1" item={ITEM} />);

    const filaObligatoria = (await screen.findByText('SOAT vigente')).closest('li') as HTMLElement;
    const filaOpcional = screen.getByText('Poder autenticado').closest('li') as HTMLElement;

    expect(within(filaObligatoria).queryByText('Opcional')).not.toBeInTheDocument();
    expect(within(filaOpcional).getByText('Opcional')).toBeInTheDocument();
  });

  it('lleno: pinta requisitos y una tarjeta separada de otros adjuntos, excluyendo los de origen system', async () => {
    mocks.getChecklist.mockResolvedValue(
      checklist([
        { key: 'soat', label: 'SOAT vigente', obligatorio: true, docTipo: 'soat', satisfied: true },
      ]),
    );
    mocks.getAttachments.mockResolvedValue([ATTACHMENT_SOAT, ATTACHMENT_EXTRA, ATTACHMENT_SYSTEM]);

    render(<TramiteDetalleDocumentos instanceId="inst-1" item={ITEM} />);

    await screen.findByText('1 de 1 requisitos cumplidos');

    const otros = screen.getByRole('region', { name: 'Otros adjuntos' });
    expect(within(otros).getByText(/factura_compra\.pdf/)).toBeInTheDocument();
    // El adjunto que casa con el requisito (soat) no se repite aquí.
    expect(within(otros).queryByText(/soat_vigente\.pdf/)).not.toBeInTheDocument();
    // El adjunto de origen 'system' no se repite aquí (lo muestra la sección de trazabilidad).
    expect(within(otros).queryByText(/fur_generado\.pdf/)).not.toBeInTheDocument();
  });

  it('no pinta la tarjeta de otros adjuntos si todos los adjuntos casan con un requisito o son de sistema', async () => {
    mocks.getChecklist.mockResolvedValue(
      checklist([
        { key: 'soat', label: 'SOAT vigente', obligatorio: true, docTipo: 'soat', satisfied: true },
      ]),
    );
    mocks.getAttachments.mockResolvedValue([ATTACHMENT_SOAT, ATTACHMENT_SYSTEM]);

    render(<TramiteDetalleDocumentos instanceId="inst-1" item={ITEM} />);

    await screen.findByText('1 de 1 requisitos cumplidos');
    expect(screen.queryByRole('region', { name: 'Otros adjuntos' })).not.toBeInTheDocument();
  });
});
