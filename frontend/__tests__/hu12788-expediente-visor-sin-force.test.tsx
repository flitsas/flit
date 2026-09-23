/**
 * HU #12788 — el ExpedienteVisor deja de forzar la regeneración del consolidado al abrirlo.
 *
 * Uso de ejemplo:
 *   <ExpedienteVisor instanceId="inst-1" attachments={[consolidado]} />
 *   · «Ver expediente consolidado (PDF)»   → POST /instances/inst-1/consolidado          (sin force)
 *   · «Re-generar expediente consolidado»  → POST /instances/inst-1/consolidado?force=true
 *
 * El backend decide con `consolidado_wizard_vigente`: vigente → PDF en caché; no vigente →
 * reconstruye una vez. El cliente no cachea el PDF entre aperturas.
 */
import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { GenerarConsolidadoResult, ProcedureAttachment } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  generarConsolidado: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  openLoadingDocumentTab: vi.fn(() => ({}) as Window),
  openObjectUrlInWindow: vi.fn(),
  showDocumentTabError: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    generarConsolidado: mocks.generarConsolidado,
    fetchAttachmentPreviewUrl: mocks.fetchAttachmentPreviewUrl,
    downloadAttachment: mocks.downloadAttachment,
  },
}));

vi.mock('@/lib/documents/open-document-tab', () => ({
  openLoadingDocumentTab: mocks.openLoadingDocumentTab,
  openObjectUrlInWindow: mocks.openObjectUrlInWindow,
  showDocumentTabError: mocks.showDocumentTabError,
}));

import ExpedienteVisor from '@/components/operacion/ExpedienteVisor';

const INSTANCE = 'inst-12788';
const VER = { name: 'Ver expediente consolidado (PDF)' };
const REGENERAR = { name: 'Re-generar expediente consolidado' };

const CONSOLIDADO_PREVIO: ProcedureAttachment = {
  id: 'att-cons-v1',
  tipo: 'consolidado',
  filename: 'consolidado.pdf',
  mimetype: 'application/pdf',
} as ProcedureAttachment;

function resultado(attachmentId: string): GenerarConsolidadoResult {
  return {
    document: { attachmentId, tipo: 'consolidado', filename: `${attachmentId}.pdf`, sha256: 'h' },
    incompleto: false,
    documentosFaltantes: [],
    avisosCascada: [],
  };
}

let objectUrlSeq = 0;

beforeEach(() => {
  vi.clearAllMocks();
  objectUrlSeq = 0;
  // Sin URL prefirmada → cae a /download: el binario viene del backend en cada apertura.
  mocks.fetchAttachmentPreviewUrl.mockRejectedValue(new Error('preview_url_empty'));
  mocks.downloadAttachment.mockImplementation(async (_i: string, attachmentId: string) => ({
    blob: new Blob([`%PDF-${attachmentId}`], { type: 'application/pdf' }),
    filename: `${attachmentId}.pdf`,
    mimetype: 'application/pdf',
  }));
  vi.stubGlobal('URL', Object.assign(URL, {
    createObjectURL: vi.fn(() => `blob:mock-${++objectUrlSeq}`),
    revokeObjectURL: vi.fn(),
  }));
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('HU #12788 AC1 — bandera vigente: abrir el visor va sin force y muestra el PDF en caché', () => {
  it('«Ver expediente» llama generarConsolidado(instanceId) sin force', async () => {
    mocks.generarConsolidado.mockResolvedValue(resultado('att-cons-v1'));
    const user = userEvent.setup();
    render(<ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO_PREVIO]} />);

    await user.click(screen.getByRole('button', VER));

    await waitFor(() => expect(mocks.openObjectUrlInWindow).toHaveBeenCalledTimes(1));
    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1);
    expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE);
    // Contrato: ningún argumento force=true viaja en la apertura.
    expect(mocks.generarConsolidado.mock.calls[0]).not.toContain(true);
  });

  it('abre el adjunto cacheado que devuelve el backend (mismo attachmentId previo)', async () => {
    mocks.generarConsolidado.mockResolvedValue(resultado('att-cons-v1'));
    const user = userEvent.setup();
    render(<ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO_PREVIO]} />);

    await user.click(screen.getByRole('button', VER));

    await waitFor(() =>
      expect(mocks.downloadAttachment).toHaveBeenCalledWith(
        INSTANCE,
        'att-cons-v1',
        undefined,
        'att-cons-v1.pdf',
      ),
    );
    expect(mocks.openObjectUrlInWindow).toHaveBeenCalledWith('blob:mock-1', expect.anything());
  });

  it('sin consolidado previo (primera apertura) también va sin force', async () => {
    mocks.generarConsolidado.mockResolvedValue(resultado('att-cons-nuevo'));
    const user = userEvent.setup();
    render(<ExpedienteVisor instanceId={INSTANCE} attachments={[]} />);

    expect(screen.queryByRole('button', REGENERAR)).toBeNull();
    await user.click(screen.getByRole('button', VER));

    await waitFor(() => expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE));
  });
});

describe('HU #12788 AC2 — bandera en false: se reconstruye una sola vez (una única petición)', () => {
  it('bajo StrictMode, montar el visor no dispara ninguna petición de consolidado', () => {
    render(
      <StrictMode>
        <ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO_PREVIO]} />
      </StrictMode>,
    );
    expect(mocks.generarConsolidado).not.toHaveBeenCalled();
  });

  it('un doble clic rápido en «Ver expediente» envía una sola petición', async () => {
    let resolver: (r: GenerarConsolidadoResult) => void = () => undefined;
    mocks.generarConsolidado.mockImplementation(
      () => new Promise<GenerarConsolidadoResult>((res) => (resolver = res)),
    );
    render(
      <StrictMode>
        <ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO_PREVIO]} />
      </StrictMode>,
    );
    const boton = screen.getByRole('button', VER);

    // Dos clics en el mismo lote de act: React aún no re-renderizó, `disabled` no protege; el
    // candado síncrono (ref) es lo único que impide la segunda petición.
    act(() => {
      boton.click();
      boton.click();
    });

    await waitFor(() => expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1));
    await act(async () => {
      resolver(resultado('att-cons-v2'));
    });
    await waitFor(() => expect(mocks.openObjectUrlInWindow).toHaveBeenCalledTimes(1));
    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1);
    expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE);
  });

  it('muestra el PDF reconstruido (attachmentId nuevo que devuelve el backend)', async () => {
    mocks.generarConsolidado.mockResolvedValue(resultado('att-cons-v2'));
    const user = userEvent.setup();
    render(<ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO_PREVIO]} />);

    await user.click(screen.getByRole('button', VER));

    await waitFor(() =>
      expect(mocks.downloadAttachment).toHaveBeenCalledWith(
        INSTANCE,
        'att-cons-v2',
        undefined,
        'att-cons-v2.pdf',
      ),
    );
    expect(mocks.downloadAttachment).not.toHaveBeenCalledWith(
      INSTANCE,
      'att-cons-v1',
      expect.anything(),
      expect.anything(),
    );
  });
});

describe('HU #12788 AC3 — la acción explícita de regenerar sigue enviando force=true', () => {
  it('«Re-generar expediente consolidado» llama generarConsolidado(instanceId, undefined, true)', async () => {
    mocks.generarConsolidado.mockResolvedValue(resultado('att-cons-v3'));
    const onAttachmentsChange = vi.fn();
    const user = userEvent.setup();
    render(
      <ExpedienteVisor
        instanceId={INSTANCE}
        attachments={[CONSOLIDADO_PREVIO]}
        onAttachmentsChange={onAttachmentsChange}
      />,
    );

    await user.click(screen.getByRole('button', REGENERAR));

    await waitFor(() => expect(onAttachmentsChange).toHaveBeenCalledTimes(1));
    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1);
    expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE, undefined, true);
  });

  it('mantiene el aviso de FUR no regenerado de la HU #11642 tras forzar', async () => {
    mocks.generarConsolidado.mockResolvedValue({
      ...resultado('att-cons-v3'),
      avisosCascada: ['fur: provider_unavailable'],
    });
    const user = userEvent.setup();
    render(<ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO_PREVIO]} />);

    await user.click(screen.getByRole('button', REGENERAR));

    expect(await screen.findByRole('alert')).toHaveTextContent(/No se pudo regenerar el FUR/);
    expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE, undefined, true);
  });

  it('en estado final no se ofrece regenerar (sin botón force)', () => {
    render(
      <ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO_PREVIO]} status="aprobado" />,
    );
    expect(screen.queryByRole('button', REGENERAR)).toBeNull();
    expect(screen.getByRole('button', VER)).toBeEnabled();
  });
});

describe('HU #12788 AC4 — tras editar, reabrir el visor no reutiliza un PDF viejo del cliente', () => {
  it('cada apertura pide el consolidado al backend y abre un object URL nuevo', async () => {
    mocks.generarConsolidado
      .mockResolvedValueOnce(resultado('att-cons-v1'))
      // El gestor editó un campo: el backend invalidó la vigencia y devuelve un consolidado nuevo.
      .mockResolvedValueOnce(resultado('att-cons-v2'));
    const onBefore = vi.fn(async () => undefined);
    const user = userEvent.setup();
    render(
      <ExpedienteVisor
        instanceId={INSTANCE}
        attachments={[CONSOLIDADO_PREVIO]}
        onBeforeGenerateConsolidado={onBefore}
      />,
    );

    await user.click(screen.getByRole('button', VER));
    await waitFor(() => expect(mocks.openObjectUrlInWindow).toHaveBeenCalledTimes(1));
    await user.click(screen.getByRole('button', VER));
    await waitFor(() => expect(mocks.openObjectUrlInWindow).toHaveBeenCalledTimes(2));

    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(2);
    expect(mocks.generarConsolidado.mock.calls).toEqual([[INSTANCE], [INSTANCE]]);
    expect(onBefore).toHaveBeenCalledTimes(2);
    expect(mocks.downloadAttachment.mock.calls.map((c) => c[1])).toEqual([
      'att-cons-v1',
      'att-cons-v2',
    ]);
    const urls = mocks.openObjectUrlInWindow.mock.calls.map((c) => c[0]);
    expect(urls).toEqual(['blob:mock-1', 'blob:mock-2']);
    expect(new Set(urls).size).toBe(2);
  });

  it('el botón tiene nombre accesible y se habilita de nuevo tras abrir', async () => {
    mocks.generarConsolidado.mockResolvedValue(resultado('att-cons-v1'));
    const user = userEvent.setup();
    render(<ExpedienteVisor instanceId={INSTANCE} attachments={[CONSOLIDADO_PREVIO]} />);
    const boton = screen.getByRole('button', VER);

    await user.click(boton);

    await waitFor(() => expect(boton).toBeEnabled());
    screen.getAllByRole('button').forEach((b) => {
      expect(b.getAttribute('aria-label') || b.textContent?.trim()).toBeTruthy();
    });
  });
});
