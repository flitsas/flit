/**
 * HU #12786 — [FRONTEND] Gestor: consumir el consolidado VIGENTE en el listado y en el modal de
 * documentos. La UI deja de bajar el adjunto persistido (`consolidadoAttachmentId` del resumen o la
 * fila `consolidado` de la lista) y lo pide a `GET …/consolidado/entrega` (HU #12785), que lo
 * reconstruye solo si la bandera de vigencia está abajo.
 *
 * Uso de ejemplo:
 *   const preview = useAttachmentPreview(instanceId, tenantId);
 *   await preview.openConsolidado('expediente.pdf'); // 1 GET entrega + preview del id devuelto
 *   await preview.downloadConsolidado();             // 1 GET entrega + 1 descarga del id devuelto
 *   preview.definitivo // true si la entrega sirvió el definitivo (estado final) → aviso
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  listInstances: vi.fn(),
  searchInstances: vi.fn(),
  searchEstadoCounts: vi.fn(),
  listFilterFields: vi.fn(),
  listInstanceEstadoCounts: vi.fn(),
  setPriority: vi.fn(),
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  entregarConsolidado: vi.fn(),
  generarConsolidado: vi.fn(),
  getInstance: vi.fn(),
  getStatusHistory: vi.fn(),
  listBiometricExpediente: vi.fn(),
  pauseInstance: vi.fn(),
  pauseInstancesMassive: vi.fn(),
  enviarAlOt: vi.fn(),
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
import { TramiteDocumentosModal } from '@/components/operacion/TramiteDocumentosModal';
import { ToastProvider } from '@/components/admin/Toast';
import { esDocumentoDefinitivo } from '@/lib/tramites/consolidado-entrega';

// ── Fixtures (datos ficticios) ──────────────────────────────────────
const INSTANCE_ID = 'inst-0001';

function entrega(overrides: Record<string, unknown> = {}) {
  return {
    document: {
      attachmentId: 'att-nuevo',
      tipo: 'consolidado',
      filename: 'expediente-consolidado-TR-0001.pdf',
      sha256: 'sha-nuevo',
    },
    regenerado: false,
    incompleto: false,
    documentosFaltantes: null,
    avisosCascada: null,
    definitivoPorEstadoFinal: false,
    modo: 'vigente',
    ...overrides,
  };
}

const ADJUNTO_FUR = {
  id: 'att-fur',
  tipo: 'fur',
  filename: 'fur.pdf',
  mimetype: 'application/pdf',
  sizeBytes: 1024,
  sha256: 'a',
  source: 'system',
  uploadedAt: '2026-09-01T00:00:00Z',
};
/** El consolidado PERSISTIDO en la lista: el antiguo, que la UI ya no debe bajar directamente. */
const ADJUNTO_CONSOLIDADO_VIEJO = {
  id: 'att-viejo',
  tipo: 'consolidado',
  filename: 'expediente-consolidado.pdf',
  mimetype: 'application/pdf',
  sizeBytes: 4096,
  sha256: 'b',
  source: 'system',
  uploadedAt: '2026-09-01T00:00:00Z',
};

function instancia(overrides: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: INSTANCE_ID,
    referenceNumber: 'TR-0001',
    modalidad: 'TRASPASO',
    estado: 'radicado',
    placa: 'ABC123',
    vin: 'VIN-0001',
    vehiculoMarca: 'Marca',
    vehiculoLinea: 'Linea',
    compradorNombre: 'Comprador',
    compradorDocumento: '1000',
    vendedorNombre: 'Vendedor',
    vendedorDocumento: '2000',
    organismoTransito: null,
    pasoActual: 6,
    totalPasos: 6,
    createdAt: '2026-09-01T00:00:00Z',
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
    updatedAt: null,
    gestorNombre: null,
    fuente: 'dashboard',
    firmaVendedorEstado: 'pendiente',
    firmaCompradorEstado: 'pendiente',
    consolidadoAttachmentId: 'att-viejo',
    ...overrides,
  } as InstanceSummary;
}

const createObjectURL = vi.fn(() => 'blob:consolidado');
const revokeObjectURL = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  mocks.searchInstances.mockImplementation(async () => {
    const items: InstanceSummary[] = (await mocks.listInstances()) ?? [];
    return { items, total: items.length };
  });
  mocks.searchEstadoCounts.mockResolvedValue({});
  mocks.listInstanceEstadoCounts.mockResolvedValue({});
  mocks.listFilterFields.mockResolvedValue([]);
  mocks.getStatusHistory.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 });
  mocks.getConsultationConfig.mockResolvedValue({
    vehiclePlate: 'kyverum_runt',
    onlyOwnVehicles: false,
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  });
  mocks.fetchAttachmentPreviewUrl.mockResolvedValue({
    url: 'https://s3.local/consolidado.pdf',
    expiresAt: '2026-09-23T00:10:00Z',
  });
  mocks.downloadAttachment.mockResolvedValue({
    blob: new Blob(['%PDF'], { type: 'application/pdf' }),
    filename: 'expediente-consolidado-TR-0001.pdf',
  });
  // Solo la URL prefirmada del PDF responde; el resto (p. ej. /security/modules) falla como sin red.
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) =>
      String(input).startsWith('https://s3.local/')
        ? new Response(new Blob(['%PDF'], { type: 'application/pdf' }), { status: 200 })
        : Promise.reject(new TypeError('network')),
    ),
  );
  Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true });
  Object.defineProperty(URL, 'revokeObjectURL', { value: revokeObjectURL, configurable: true });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

async function abrirConsolidadoDesdeListado() {
  render(
    <ToastProvider>
      <TramitesTable />
    </ToastProvider>,
  );
  await screen.findByText('ABC123');
  await userEvent.click(screen.getByRole('button', { name: /Acciones del trámite TR-0001/ }));
  await userEvent.click(screen.getByRole('menuitem', { name: 'Ver consolidado' }));
  return screen.findByRole('dialog', { name: 'Consolidado' });
}

function renderModal() {
  return render(
    <TramiteDocumentosModal
      open
      onClose={() => {}}
      instanceId={INSTANCE_ID}
      referenceNumber="TR-0001"
    />,
  );
}

// ── AC1 ─────────────────────────────────────────────────────────────
describe('HU #12786 AC1 — descarga desde el listado de un consolidado desactualizado', () => {
  it('pide la ruta de entrega y previsualiza/descarga el PDF reconstruido, no el id del resumen', async () => {
    mocks.listInstances.mockResolvedValue([instancia()]);
    mocks.entregarConsolidado.mockResolvedValue(entrega({ regenerado: true, modo: 'regenerado' }));

    const dialog = await abrirConsolidadoDesdeListado();

    expect(mocks.entregarConsolidado).toHaveBeenCalledTimes(1);
    expect(mocks.entregarConsolidado).toHaveBeenCalledWith(INSTANCE_ID, {}, undefined);
    await waitFor(() =>
      expect(mocks.fetchAttachmentPreviewUrl).toHaveBeenCalledWith(INSTANCE_ID, 'att-nuevo', undefined),
    );
    expect(mocks.fetchAttachmentPreviewUrl).not.toHaveBeenCalledWith(
      INSTANCE_ID,
      'att-viejo',
      expect.anything(),
    );

    await userEvent.click(await within(dialog).findByRole('button', { name: 'Descargar documento' }));
    await waitFor(() =>
      expect(mocks.downloadAttachment).toHaveBeenCalledWith(INSTANCE_ID, 'att-nuevo', undefined),
    );
    // Descargar desde el visor reutiliza la entrega resuelta: no hay una segunda petición.
    expect(mocks.entregarConsolidado).toHaveBeenCalledTimes(1);
    expect(mocks.generarConsolidado).not.toHaveBeenCalled();
  });

  it('si la entrega falla, el visor muestra el error y NO cae al adjunto antiguo', async () => {
    mocks.listInstances.mockResolvedValue([instancia()]);
    mocks.entregarConsolidado.mockRejectedValue(new Error('fur_requerido'));

    const dialog = await abrirConsolidadoDesdeListado();

    // Security B2 (Épica #12760): el código del backend se traduce; nunca se pinta crudo.
    const alerta = await within(dialog).findByRole('alert');
    expect(alerta).toHaveTextContent('El trámite aún no tiene el FUR generado');
    expect(alerta.textContent).not.toMatch(/fur_requerido/);
    // Sin adjunto resuelto no se ofrece «Descargar» (no hay qué bajar).
    expect(within(dialog).queryByRole('button', { name: 'Descargar documento' })).toBeNull();
    expect(mocks.fetchAttachmentPreviewUrl).not.toHaveBeenCalled();
    expect(mocks.downloadAttachment).not.toHaveBeenCalled();
  });

  it('contrato: la entrega se pide por el trámite de la fila y sin force ni soloLectura', async () => {
    mocks.listInstances.mockResolvedValue([instancia()]);
    mocks.entregarConsolidado.mockResolvedValue(entrega());

    await abrirConsolidadoDesdeListado();

    const [id, params] = mocks.entregarConsolidado.mock.calls[0];
    expect(id).toBe(INSTANCE_ID);
    // Sin `force` ni `soloLectura`: el camino normal (reconstruye solo si la bandera está abajo).
    expect(params).toEqual({});
  });
});

// ── AC2 ─────────────────────────────────────────────────────────────
describe('HU #12786 AC2 — TramiteDocumentosModal sirve el consolidado vigente', () => {
  it('previsualizar el consolidado usa la entrega y abre el adjunto que ella devuelve', async () => {
    mocks.getAttachments.mockResolvedValue([ADJUNTO_FUR, ADJUNTO_CONSOLIDADO_VIEJO]);
    mocks.entregarConsolidado.mockResolvedValue(entrega({ regenerado: true, modo: 'regenerado' }));
    renderModal();

    await userEvent.click(await screen.findByRole('button', { name: 'Previsualizar Consolidado' }));

    expect(mocks.entregarConsolidado).toHaveBeenCalledWith(INSTANCE_ID, {}, undefined);
    await waitFor(() =>
      expect(mocks.fetchAttachmentPreviewUrl).toHaveBeenCalledWith(INSTANCE_ID, 'att-nuevo', undefined),
    );
    expect(mocks.fetchAttachmentPreviewUrl).not.toHaveBeenCalledWith(
      INSTANCE_ID,
      'att-viejo',
      expect.anything(),
    );
  });

  it('descargar el consolidado (fila y «Descargar todo») baja el vigente, no el antiguo', async () => {
    mocks.getAttachments.mockResolvedValue([ADJUNTO_FUR, ADJUNTO_CONSOLIDADO_VIEJO]);
    mocks.entregarConsolidado.mockResolvedValue(entrega({ regenerado: true, modo: 'regenerado' }));
    renderModal();

    await userEvent.click(await screen.findByRole('button', { name: 'Descargar Consolidado' }));
    await waitFor(() =>
      expect(mocks.downloadAttachment).toHaveBeenCalledWith(INSTANCE_ID, 'att-nuevo', undefined),
    );

    await userEvent.click(
      screen.getByRole('button', { name: 'Descargar todo · Expediente consolidado (PDF)' }),
    );
    await waitFor(() => expect(mocks.downloadAttachment).toHaveBeenCalledTimes(2));
    expect(mocks.downloadAttachment).not.toHaveBeenCalledWith(
      INSTANCE_ID,
      'att-viejo',
      expect.anything(),
    );
  });

  it('los demás documentos siguen bajando su adjunto directo (sin tocar la entrega)', async () => {
    mocks.getAttachments.mockResolvedValue([ADJUNTO_FUR, ADJUNTO_CONSOLIDADO_VIEJO]);
    renderModal();

    await userEvent.click(await screen.findByRole('button', { name: 'Previsualizar FUR' }));
    await waitFor(() =>
      expect(mocks.fetchAttachmentPreviewUrl).toHaveBeenCalledWith(INSTANCE_ID, 'att-fur', undefined),
    );
    expect(mocks.entregarConsolidado).not.toHaveBeenCalled();
  });
});

// ── AC3 ─────────────────────────────────────────────────────────────
describe('HU #12786 AC3 — consolidado vigente descargado dos veces seguidas', () => {
  it('cada clic hace UNA petición de entrega y UNA descarga; nunca fuerza ni genera', async () => {
    mocks.getAttachments.mockResolvedValue([ADJUNTO_FUR, ADJUNTO_CONSOLIDADO_VIEJO]);
    mocks.entregarConsolidado.mockResolvedValue(
      entrega({ document: { ...entrega().document, attachmentId: 'att-viejo' } }),
    );
    renderModal();

    const boton = await screen.findByRole('button', {
      name: 'Descargar todo · Expediente consolidado (PDF)',
    });
    await userEvent.click(boton);
    await waitFor(() => expect(mocks.downloadAttachment).toHaveBeenCalledTimes(1));
    expect(mocks.entregarConsolidado).toHaveBeenCalledTimes(1);

    await userEvent.click(boton);
    await waitFor(() => expect(mocks.downloadAttachment).toHaveBeenCalledTimes(2));
    expect(mocks.entregarConsolidado).toHaveBeenCalledTimes(2);

    for (const call of mocks.entregarConsolidado.mock.calls) {
      expect(call[1]).toEqual({}); // sin force
    }
    // `modo: "vigente"` → se baja el mismo adjunto cacheado, sin regeneración desde la UI.
    expect(mocks.downloadAttachment).toHaveBeenNthCalledWith(2, INSTANCE_ID, 'att-viejo', undefined);
    expect(mocks.generarConsolidado).not.toHaveBeenCalled();
    // Vigente no es definitivo: sin aviso de documento final.
    expect(screen.queryByTestId('aviso-documento-final')).toBeNull();
  });
});

// ── AC4 ─────────────────────────────────────────────────────────────
describe('HU #12786 AC4 — trámite aprobado: documento definitivo con aviso', () => {
  it('el visor del listado muestra el aviso de documento final', async () => {
    mocks.listInstances.mockResolvedValue([instancia({ estado: 'entregado' })]);
    mocks.entregarConsolidado.mockResolvedValue(
      entrega({ definitivoPorEstadoFinal: true, modo: 'definitivo_estado_final' }),
    );

    const dialog = await abrirConsolidadoDesdeListado();

    const aviso = await within(dialog).findByTestId('aviso-documento-final');
    expect(aviso).toHaveAttribute('role', 'status');
    expect(aviso).toHaveTextContent('Documento final.');
    expect(aviso).toHaveTextContent(/expediente definitivo y ya no se regenera/);
    expect(mocks.generarConsolidado).not.toHaveBeenCalled();
  });

  it('la descarga directa desde el modal también avisa que es el documento final', async () => {
    mocks.getAttachments.mockResolvedValue([ADJUNTO_FUR, ADJUNTO_CONSOLIDADO_VIEJO]);
    mocks.entregarConsolidado.mockResolvedValue(
      entrega({ definitivoPorEstadoFinal: true, modo: 'definitivo_estado_final' }),
    );
    renderModal();

    await userEvent.click(
      await screen.findByRole('button', { name: 'Descargar todo · Expediente consolidado (PDF)' }),
    );

    expect(await screen.findByTestId('aviso-documento-final')).toBeInTheDocument();
    expect(mocks.downloadAttachment).toHaveBeenCalledWith(INSTANCE_ID, 'att-nuevo', undefined);
  });

  it('contrato: esDocumentoDefinitivo acepta la bandera o los modos de estado final', () => {
    expect(esDocumentoDefinitivo(null)).toBe(false);
    expect(esDocumentoDefinitivo(undefined)).toBe(false);
    expect(esDocumentoDefinitivo({ definitivoPorEstadoFinal: true, modo: null })).toBe(true);
    expect(esDocumentoDefinitivo({ modo: 'definitivo_estado_final' })).toBe(true);
    expect(esDocumentoDefinitivo({ modo: 'migrado_solo_lectura' })).toBe(true);
    expect(esDocumentoDefinitivo({ definitivoPorEstadoFinal: false, modo: 'vigente' })).toBe(false);
    expect(esDocumentoDefinitivo({ modo: 'regenerado' })).toBe(false);
    expect(esDocumentoDefinitivo({ modo: 'solo_lectura' })).toBe(false);
  });
});
