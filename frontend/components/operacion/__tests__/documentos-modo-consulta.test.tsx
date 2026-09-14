import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  ChecklistView,
  InstanceSummary,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

/**
 * HU #12411 — Vista consolidada: documentos de un trámite de la red en modo consulta.
 *
 * Uso de ejemplo: la cabeza de red abre el detalle de un trámite de un cliente hijo
 * (`ConsultaModeProvider consultaMode`). La sección «Documentos del trámite» lista los adjuntos por
 * `GET /api/v1/tramites/network/instances/{id}/attachments` (contrato B5 #12410) y solo ofrece «Ver»
 * y «Descargar», ambos por `…/attachments/{attachmentId}/download`. Nunca `preview-url`, nunca una
 * escritura, nunca la ruta propia.
 *
 * AC1 — lista + Ver/Descargar por la ruta proxeada; sin cargar/reemplazar/regenerar/eliminar.
 * AC2 — ninguna petición de escritura ni `fetchAttachmentPreviewUrl`.
 * AC3 — 403 `network_documents_disabled` / 403 `network_scope_required` / 404 ⇒ mismo copy,
 *       `role="status"`, sin error técnico y sin reintento (una sola llamada).
 * AC4 — trámite propio: rutas y acciones de hoy (paridad exacta de llamadas).
 * AC5 — cliente sin jerarquía: idéntico a hoy, ninguna llamada de red.
 * AC6 — rótulo «Solo consulta» textual, nombres accesibles con el archivo, orden de tabulación.
 * AC7 — 503 `audit_unavailable` (auditoría fail-closed de la descarga, commit d06cc3e6): copy
 *       específico como `role="alert"` con «Reintentar» que repite la MISMA llamada; sin visor ni
 *       binario. El 403/404 de alcance sigue sin reintento.
 */

const mocks = vi.hoisted(() => ({
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  getNetworkAttachments: vi.fn(),
  downloadNetworkAttachment: vi.fn(),
  // Escrituras sobre documentos que existen en el cliente: ninguna puede dispararse.
  deleteAttachment: vi.fn(),
  uploadAttachment: vi.fn(),
  registerAttachment: vi.fn(),
  presignAttachment: vi.fn(),
  regenerateDocuments: vi.fn(),
}));

/** Métodos de `tramitesClient` que ESCRIBEN o abren una dirección prefirmada (AC2). */
const PROHIBIDAS_EN_CONSULTA = [
  'fetchAttachmentPreviewUrl',
  'downloadAttachment',
  'getAttachments',
  'getChecklist',
  'deleteAttachment',
  'uploadAttachment',
  'registerAttachment',
  'presignAttachment',
  'regenerateDocuments',
] as const;

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
}));

import { TramiteDetalleDocumentos } from '@/components/operacion/detalle/TramiteDetalleDocumentos';
import { ConsultaModeProvider } from '@/components/operacion/ConsultaModeContext';
import {
  COPY_DESCARGA_SIN_AUDITORIA,
  COPY_DOCUMENTOS_FUERA_DE_ALCANCE,
  ETIQUETA_SOLO_CONSULTA,
  isAuditUnavailable,
} from '@/lib/tramites/network-scope';

const CABEZA = '11111111-1111-1111-1111-111111111111';
const HIJO = '22222222-2222-2222-2222-222222222222';

function makeItem(over: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: 'inst-red',
    referenceNumber: 'TR-RED',
    modalidad: 'TRASPASO',
    estado: 'entregado',
    placa: 'BBB222',
    vin: 'VIN-RED-001',
    vehiculoMarca: 'Renault',
    vehiculoLinea: 'Logan',
    compradorNombre: 'Cliente Hijo',
    compradorDocumento: '900000000',
    organismoTransito: 'OT Norte',
    pasoActual: 6,
    totalPasos: 6,
    createdAt: '2026-06-18T00:00:00Z',
    draftFinalizedAt: null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: true,
    prioritario: false,
    tenantId: HIJO,
    companiaNombre: 'Concesionario Hijo SAS',
    ...over,
  } as InstanceSummary;
}

const ATT_SOAT: ProcedureAttachment = {
  id: 'att-soat',
  tipo: 'soat',
  filename: 'soat.pdf',
  mimetype: 'application/pdf',
  sizeBytes: 204800,
  sha256: 'aaa',
  source: 'user',
  uploadedAt: '2026-06-20T10:00:00Z',
};

const ATT_FUR: ProcedureAttachment = {
  id: 'att-fur',
  tipo: 'fur',
  filename: 'FUR.pdf',
  mimetype: 'application/pdf',
  sha256: 'bbb',
  sizeBytes: 1024,
  source: 'system',
  uploadedAt: '2026-06-21T10:00:00Z',
};

function apiError(status: number, error: string): Error & { status: number } {
  return Object.assign(new Error(`${status} ${error}`), {
    status,
    problem: { error },
  });
}

function renderConsulta(item = makeItem()) {
  return render(
    <ConsultaModeProvider consultaMode>
      <TramiteDetalleDocumentos instanceId={item.id} item={item} />
    </ConsultaModeProvider>,
  );
}

function llamadasHechas(): string[] {
  return (Object.keys(mocks) as (keyof typeof mocks)[])
    .filter((k) => mocks[k].mock.calls.length > 0)
    .sort();
}

function prohibidasDisparadas(): string[] {
  return PROHIBIDAS_EN_CONSULTA.filter((k) => mocks[k].mock.calls.length > 0);
}

const createObjectURL = vi.fn(() => 'blob:mock-url');
const revokeObjectURL = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getNetworkAttachments.mockResolvedValue([ATT_SOAT, ATT_FUR]);
  mocks.getAttachments.mockResolvedValue([ATT_SOAT, ATT_FUR]);
  mocks.getChecklist.mockResolvedValue({
    items: [{ key: 'soat', label: 'SOAT', docTipo: 'soat', obligatorio: true, satisfied: true }],
    faltanObligatorios: 0,
    completo: true,
  } satisfies ChecklistView);
  mocks.downloadNetworkAttachment.mockResolvedValue({
    blob: new Blob(['%PDF'], { type: 'application/pdf' }),
    filename: 'soat.pdf',
    mimetype: 'application/pdf',
  });
  mocks.downloadAttachment.mockResolvedValue({
    blob: new Blob(['%PDF'], { type: 'application/pdf' }),
    filename: 'soat.pdf',
    mimetype: 'application/pdf',
  });
  // jsdom no implementa objectURL.
  Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true });
  Object.defineProperty(URL, 'revokeObjectURL', { value: revokeObjectURL, configurable: true });
});

afterEach(() => {
  vi.restoreAllMocks();
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12411 — AC1: sección de documentos en modo consulta', () => {
  it('lista los adjuntos con metadatos por la ruta de red y ofrece Ver y Descargar por archivo', async () => {
    renderConsulta();
    expect(await screen.findByText(/soat\.pdf/)).toBeInTheDocument();
    expect(screen.getByText(/FUR\.pdf/)).toBeInTheDocument();
    // Metadatos: tamaño, fecha y origen.
    expect(screen.getByText(/200 KB/)).toBeInTheDocument();
    expect(screen.getByText(/Generado por FLIT/)).toBeInTheDocument();
    // Ver + Descargar por cada archivo, y nada más.
    expect(screen.getByRole('button', { name: 'Ver soat.pdf' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descargar soat.pdf' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ver FUR.pdf' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descargar FUR.pdf' })).toBeInTheDocument();
    expect(screen.getAllByRole('button')).toHaveLength(4);
    // Lectura por la ruta proxeada, no por la propia ni por el checklist.
    expect(mocks.getNetworkAttachments).toHaveBeenCalledWith('inst-red');
    expect(llamadasHechas()).toEqual(['getNetworkAttachments']);
  });

  it('no ofrece cargar, reemplazar, regenerar ni eliminar: ni botones, ni menús, ni zona de arrastre', async () => {
    const { container } = renderConsulta();
    await screen.findByText(/soat\.pdf/);
    // Con inicio de palabra para que «Descargar» no cuente como «cargar».
    for (const re of [/(^|\s)cargar/i, /(^|\s)subir/i, /reemplazar/i, /regenerar/i, /eliminar/i, /borrar/i]) {
      expect(screen.queryByRole('button', { name: re })).not.toBeInTheDocument();
      expect(screen.queryByRole('menuitem', { name: re })).not.toBeInTheDocument();
    }
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(container.querySelector('input[type="file"]')).toBeNull();
    expect(container.querySelector('[data-dropzone], [aria-dropeffect]')).toBeNull();
  });

  it('«Descargar» usa la ruta de red y dispara la descarga del navegador desde un blob', async () => {
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await userEvent.click(screen.getByRole('button', { name: 'Descargar soat.pdf' }));
    await waitFor(() =>
      expect(mocks.downloadNetworkAttachment).toHaveBeenCalledWith('inst-red', 'att-soat', 'soat.pdf'),
    );
    await waitFor(() => expect(click).toHaveBeenCalled());
    expect(createObjectURL).toHaveBeenCalled();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
    // No se abre el visor al descargar directo.
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('«Ver» descarga el binario por la ruta de red y lo muestra desde un objectURL local', async () => {
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    await userEvent.click(screen.getByRole('button', { name: 'Ver soat.pdf' }));
    await waitFor(() =>
      expect(mocks.downloadNetworkAttachment).toHaveBeenCalledWith('inst-red', 'att-soat', 'soat.pdf'),
    );
    const dialog = await screen.findByRole('dialog');
    const iframe = await within(dialog).findByTestId('preview-iframe');
    expect(iframe).toHaveAttribute('src', 'blob:mock-url');
    expect(createObjectURL).toHaveBeenCalled();
  });

  it('estados: cargando y vacío', async () => {
    let resolve: (v: ProcedureAttachment[]) => void = () => undefined;
    mocks.getNetworkAttachments.mockImplementation(
      () => new Promise<ProcedureAttachment[]>((r) => (resolve = r)),
    );
    renderConsulta();
    expect(screen.getByRole('status', { name: /Cargando documentos/ })).toBeInTheDocument();
    resolve([]);
    expect(await screen.findByText(/no tiene documentos registrados/)).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12411 — AC2: nada dispara una escritura ni abre una dirección prefirmada', () => {
  it('interactuar con todo (Ver, Descargar, teclado) no llama a ninguna escritura ni a preview-url', async () => {
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    const user = userEvent.setup();
    for (const btn of screen.getAllByRole('button')) {
      await user.click(btn);
    }
    // Cerrar el visor si quedó abierto y recorrer con teclado.
    const cerrar = screen.queryByRole('button', { name: 'Cerrar' });
    if (cerrar) await user.click(cerrar);
    await user.tab();
    await user.keyboard('{Enter}');
    await user.tab();
    await user.keyboard(' ');
    await waitFor(() => expect(mocks.downloadNetworkAttachment).toHaveBeenCalled());
    expect(prohibidasDisparadas()).toEqual([]);
    expect(mocks.fetchAttachmentPreviewUrl).not.toHaveBeenCalled();
  });

  it('contrato: la descarga de red no lleva X-Tenant-Id ni consulta preview-url (solo firma de llamada)', async () => {
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    await userEvent.click(screen.getByRole('button', { name: 'Descargar FUR.pdf' }));
    await waitFor(() => expect(mocks.downloadNetworkAttachment).toHaveBeenCalledTimes(1));
    // (instanceId, attachmentId, fallbackFilename) — sin tenant.
    expect(mocks.downloadNetworkAttachment.mock.calls[0]).toEqual(['inst-red', 'att-fur', 'FUR.pdf']);
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12411 — AC3: servidor sin habilitación para esa cabeza', () => {
  it.each([
    ['403 network_documents_disabled', apiError(403, 'network_documents_disabled')],
    ['403 network_scope_required', apiError(403, 'network_scope_required')],
    ['404 not_found (anti-enumeración)', apiError(404, 'not_found')],
  ])('%s ⇒ copy de fuera de alcance como estado, sin error técnico ni reintento', async (_n, err) => {
    mocks.getNetworkAttachments.mockRejectedValue(err);
    renderConsulta();
    const copy = await screen.findByText(COPY_DOCUMENTOS_FUERA_DE_ALCANCE);
    expect(copy).toHaveAttribute('role', 'status');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Reintentar/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/403|404|Forbidden|not_found|network_/)).not.toBeInTheDocument();
    // Sin reintento automático: una sola petición.
    await new Promise((r) => setTimeout(r, 30));
    expect(mocks.getNetworkAttachments).toHaveBeenCalledTimes(1);
    expect(prohibidasDisparadas()).toEqual([]);
  });

  it('un fallo técnico distinto (500) sí es un error con reintento (no se confunde con alcance)', async () => {
    mocks.getNetworkAttachments.mockRejectedValueOnce(apiError(500, 'boom'));
    renderConsulta();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Reintentar los documentos del trámite/ }));
    expect(await screen.findByText(/soat\.pdf/)).toBeInTheDocument();
    expect(mocks.getNetworkAttachments).toHaveBeenCalledTimes(2);
  });

  it('un 403 al descargar se comunica con el mismo copy, sin error técnico', async () => {
    mocks.downloadNetworkAttachment.mockRejectedValue(apiError(403, 'network_documents_disabled'));
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    await userEvent.click(screen.getByRole('button', { name: 'Descargar soat.pdf' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(COPY_DOCUMENTOS_FUERA_DE_ALCANCE);
    expect(screen.queryByText(/network_documents_disabled/)).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12411 — AC4: el trámite propio no cambia', () => {
  it('sin modo consulta usa checklist + adjuntos propios y descarga por la ruta propia; ninguna llamada de red', async () => {
    const propio = makeItem({ id: 'inst-propio', tenantId: CABEZA, referenceNumber: 'TR-PROPIO' });
    render(<TramiteDetalleDocumentos instanceId="inst-propio" item={propio} />);
    expect(await screen.findByText(/SOAT/)).toBeInTheDocument();
    expect(mocks.getChecklist).toHaveBeenCalledWith('inst-propio', undefined);
    expect(mocks.getAttachments).toHaveBeenCalledWith('inst-propio', undefined);
    expect(llamadasHechas()).toEqual(['getAttachments', 'getChecklist']);
    expect(screen.queryByText(ETIQUETA_SOLO_CONSULTA)).not.toBeInTheDocument();
    // Sin botón «Ver» en el propio (no existía): solo la descarga de hoy.
    expect(screen.queryByRole('button', { name: /^Ver / })).not.toBeInTheDocument();

    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await userEvent.click(screen.getByRole('button', { name: 'Descargar soat.pdf' }));
    await waitFor(() =>
      expect(mocks.downloadAttachment).toHaveBeenCalledWith('inst-propio', 'att-soat', undefined),
    );
    expect(mocks.downloadNetworkAttachment).not.toHaveBeenCalled();
    expect(mocks.getNetworkAttachments).not.toHaveBeenCalled();
  });

  it('el 403 en el propio sigue siendo un error técnico con reintento (paridad)', async () => {
    mocks.getChecklist.mockRejectedValue(apiError(403, 'Forbidden'));
    render(<TramiteDetalleDocumentos instanceId="inst-propio" item={makeItem({ id: 'inst-propio', tenantId: CABEZA })} />);
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Reintentar/ })).toBeInTheDocument();
    expect(screen.queryByText(COPY_DOCUMENTOS_FUERA_DE_ALCANCE)).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12411 — AC5: un cliente sin jerarquía no percibe cambios', () => {
  it('sin proveedor de consulta (valor por defecto) las llamadas son exactamente las de hoy', async () => {
    render(<TramiteDetalleDocumentos instanceId="inst-propio" tenantId="t-1" item={makeItem({ id: 'inst-propio', tenantId: CABEZA })} />);
    await screen.findByText(/SOAT/);
    expect(mocks.getChecklist).toHaveBeenCalledWith('inst-propio', 't-1');
    expect(mocks.getAttachments).toHaveBeenCalledWith('inst-propio', 't-1');
    expect(llamadasHechas()).toEqual(['getAttachments', 'getChecklist']);
    expect(screen.queryByText(ETIQUETA_SOLO_CONSULTA)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descargar soat.pdf' })).toBeInTheDocument();
  });

  it('con el proveedor en false (tenant sin hijos) tampoco cambia nada', async () => {
    render(
      <ConsultaModeProvider consultaMode={false}>
        <TramiteDetalleDocumentos instanceId="inst-propio" item={makeItem({ id: 'inst-propio', tenantId: CABEZA })} />
      </ConsultaModeProvider>,
    );
    await screen.findByText(/SOAT/);
    expect(llamadasHechas()).toEqual(['getAttachments', 'getChecklist']);
    expect(mocks.getNetworkAttachments).not.toHaveBeenCalled();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12411 — AC6: accesibilidad y sistema de diseño', () => {
  it('el estado de solo lectura es textual (rótulo «Solo consulta»), no solo color', async () => {
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    expect(screen.getByText(ETIQUETA_SOLO_CONSULTA)).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Documentos del trámite' })).toBeInTheDocument();
    expect(screen.getByRole('list', { name: /Documentos del trámite \(solo consulta\)/ })).toBeInTheDocument();
  });

  it('todos los botones tienen nombre accesible con el archivo y foco visible', async () => {
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    for (const btn of screen.getAllByRole('button')) {
      expect(btn.getAttribute('aria-label')).toMatch(/^(Ver|Descargar) .+\.pdf$/);
      expect(btn.className).toContain('focus-visible:ring-2');
      expect(btn.getAttribute('type')).toBe('button');
    }
  });

  it('el orden de tabulación es Ver → Descargar por cada archivo y Enter activa el botón', async () => {
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    const user = userEvent.setup();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Ver soat.pdf' })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Descargar soat.pdf' })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Ver FUR.pdf' })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Descargar FUR.pdf' })).toHaveFocus();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await user.keyboard('{Enter}');
    await waitFor(() =>
      expect(mocks.downloadNetworkAttachment).toHaveBeenCalledWith('inst-red', 'att-fur', 'FUR.pdf'),
    );
  });

  it('el copy de fuera de alcance se anuncia como status y no se pinta ningún botón', async () => {
    mocks.getNetworkAttachments.mockRejectedValue(apiError(403, 'network_scope_required'));
    renderConsulta();
    const copy = await screen.findByText(COPY_DOCUMENTOS_FUERA_DE_ALCANCE);
    expect(copy).toHaveAttribute('role', 'status');
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByText(ETIQUETA_SOLO_CONSULTA)).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
describe('HU #12411 — AC7: 503 audit_unavailable en la descarga de red (auditoría fail-closed)', () => {
  const AUDIT = () => apiError(503, 'audit_unavailable');

  it('«Descargar» con 503 audit_unavailable ⇒ copy específico como alerta (no el de alcance) y nada descargado', async () => {
    mocks.downloadNetworkAttachment.mockRejectedValueOnce(AUDIT());
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await userEvent.click(screen.getByRole('button', { name: 'Descargar soat.pdf' }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(COPY_DESCARGA_SIN_AUDITORIA);
    expect(alert).not.toHaveTextContent(COPY_DOCUMENTOS_FUERA_DE_ALCANCE);
    expect(screen.queryByText(/503|audit_unavailable/)).not.toBeInTheDocument();
    expect(click).not.toHaveBeenCalled();
    expect(createObjectURL).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(within(alert).getByRole('button', { name: /Reintentar/ })).toBeInTheDocument();
    expect(prohibidasDisparadas()).toEqual([]);
  });

  it('«Reintentar» repite la descarga con la MISMA firma (2 llamadas) y al ir bien el aviso desaparece', async () => {
    mocks.downloadNetworkAttachment.mockRejectedValueOnce(AUDIT());
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await userEvent.click(screen.getByRole('button', { name: 'Descargar soat.pdf' }));
    await screen.findByText(COPY_DESCARGA_SIN_AUDITORIA);
    expect(mocks.downloadNetworkAttachment).toHaveBeenCalledTimes(1);

    await userEvent.click(screen.getByRole('button', { name: /Reintentar/ }));
    await waitFor(() => expect(mocks.downloadNetworkAttachment).toHaveBeenCalledTimes(2));
    expect(mocks.downloadNetworkAttachment.mock.calls[1]).toEqual(['inst-red', 'att-soat', 'soat.pdf']);
    await waitFor(() => expect(click).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
    expect(prohibidasDisparadas()).toEqual([]);
  });

  it('«Ver» con 503 audit_unavailable ⇒ no abre el visor; «Reintentar» vuelve a llamar y entonces sí lo abre', async () => {
    mocks.downloadNetworkAttachment.mockRejectedValueOnce(AUDIT());
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    await userEvent.click(screen.getByRole('button', { name: 'Ver soat.pdf' }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(COPY_DESCARGA_SIN_AUDITORIA);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(createObjectURL).not.toHaveBeenCalled();

    await userEvent.click(within(alert).getByRole('button', { name: /Reintentar/ }));
    await waitFor(() => expect(mocks.downloadNetworkAttachment).toHaveBeenCalledTimes(2));
    expect(mocks.downloadNetworkAttachment.mock.calls[1]).toEqual(['inst-red', 'att-soat', 'soat.pdf']);
    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByTestId('preview-iframe')).toHaveAttribute('src', 'blob:mock-url');
    expect(mocks.fetchAttachmentPreviewUrl).not.toHaveBeenCalled();
  });

  it('si el reintento vuelve a fallar por auditoría, el aviso sigue siendo reintentable', async () => {
    mocks.downloadNetworkAttachment.mockRejectedValue(AUDIT());
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await userEvent.click(screen.getByRole('button', { name: 'Descargar FUR.pdf' }));
    await screen.findByText(COPY_DESCARGA_SIN_AUDITORIA);
    await userEvent.click(screen.getByRole('button', { name: /Reintentar/ }));
    await waitFor(() => expect(mocks.downloadNetworkAttachment).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole('alert')).toHaveTextContent(COPY_DESCARGA_SIN_AUDITORIA);
    expect(screen.getByRole('button', { name: /Reintentar/ })).toBeInTheDocument();
  });

  it.each([
    ['403 network_documents_disabled', apiError(403, 'network_documents_disabled')],
    ['404 not_found', apiError(404, 'not_found')],
  ])('el %s al descargar sigue SIN reintento y con el copy de alcance', async (_n, err) => {
    mocks.downloadNetworkAttachment.mockRejectedValue(err);
    renderConsulta();
    await screen.findByText(/soat\.pdf/);
    await userEvent.click(screen.getByRole('button', { name: 'Descargar soat.pdf' }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(COPY_DOCUMENTOS_FUERA_DE_ALCANCE);
    expect(alert).not.toHaveTextContent(COPY_DESCARGA_SIN_AUDITORIA);
    expect(screen.queryByRole('button', { name: /Reintentar/ })).not.toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 30));
    expect(mocks.downloadNetworkAttachment).toHaveBeenCalledTimes(1);
  });

  it('contrato: isAuditUnavailable solo acepta 503 + { error: "audit_unavailable" }', () => {
    expect(isAuditUnavailable(apiError(503, 'audit_unavailable'))).toBe(true);
    expect(isAuditUnavailable(apiError(503, 'other'))).toBe(false);
    expect(isAuditUnavailable(apiError(403, 'audit_unavailable'))).toBe(false);
    expect(isAuditUnavailable(Object.assign(new Error('503'), { status: 503, problem: null }))).toBe(false);
    expect(isAuditUnavailable(new Error('boom'))).toBe(false);
    expect(isAuditUnavailable(null)).toBe(false);
    expect(isAuditUnavailable(undefined)).toBe(false);
  });
});
