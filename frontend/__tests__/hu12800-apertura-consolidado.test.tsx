/**
 * HU #12800 (Épica #12760) — abrir un consolidado que se está reconstruyendo sin bloquear la UI.
 *
 * Uso de ejemplo:
 *   <ExpedienteVisor instanceId="inst-1" attachments={[consolidado]}
 *                    consolidadoWizard={{ estado: 'desactualizado', ... }} />
 *   · clic en «Ver expediente consolidado (PDF)» → panel role=status «Reconstrucción en curso» al
 *     instante, pestaña de carga abierta con el gesto; al terminar se carga el PDF nuevo en ella.
 *   · si vence TIMEOUT_RECONSTRUCCION_MS → se abre el PDF anterior + advertencia «sigue en proceso».
 *
 *   const apertura = useAperturaConsolidado({ instanceId, vigencia, consolidadoPrevio, abrirAdjunto });
 *   apertura.fase / apertura.resultado / apertura.avisos / apertura.regenerado  (consumo HU #12799)
 */
import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, renderHook, screen, within } from '@testing-library/react';
import type {
  ChecklistItemView,
  ConsolidadoVigencia,
  GenerarConsolidadoResult,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  generarConsolidado: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
  openLoadingDocumentTab: vi.fn(),
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
import {
  COPY_TIMEOUT_CON_ANTERIOR,
  COPY_TIMEOUT_SIN_ANTERIOR,
  TIMEOUT_RECONSTRUCCION_MS,
  consolidadoDeResultado,
  requiereReconstruccion,
  useAperturaConsolidado,
} from '@/lib/tramites/useAperturaConsolidado';

const INSTANCE = 'inst-12800';
const VER = { name: 'Ver expediente consolidado (PDF)' };
const REGENERAR = { name: 'Re-generar expediente consolidado' };

const CONSOLIDADO_PREVIO: ProcedureAttachment = {
  id: 'att-cons-v1',
  tipo: 'consolidado',
  filename: 'consolidado-v1.pdf',
  mimetype: 'application/pdf',
} as ProcedureAttachment;

const FUR: ProcedureAttachment = {
  id: 'att-fur',
  tipo: 'fur',
  filename: 'fur.pdf',
  mimetype: 'application/pdf',
  sha256: 'abc',
} as ProcedureAttachment;

const CHECKLIST: ChecklistItemView[] = [
  { key: 'fur', label: 'FUR', docTipo: 'fur', obligatorio: true, satisfied: true } as ChecklistItemView,
];

function vigencia(estado: ConsolidadoVigencia['estado'], definitivo = false): ConsolidadoVigencia {
  return {
    estado,
    generadoEn: estado === 'inexistente' ? null : '2026-09-20T10:00:00Z',
    origen: estado === 'inexistente' ? null : 'system',
    definitivo,
    modo: null,
  };
}

function resultado(attachmentId: string, extra: Partial<GenerarConsolidadoResult> = {}): GenerarConsolidadoResult {
  return {
    document: { attachmentId, tipo: 'consolidado', filename: `${attachmentId}.pdf`, sha256: 'h' },
    incompleto: false,
    documentosFaltantes: [],
    avisosCascada: [],
    regenerado: true,
    ...extra,
  };
}

/** Promesa controlable: simula la reconstrucción en vuelo. */
function diferida<T>() {
  let resolve: (v: T) => void = () => undefined;
  let reject: (e: unknown) => void = () => undefined;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** Vacía la cola de microtareas (cadena preview → download → object URL) dentro de act. */
async function flush() {
  await act(async () => {
    for (let i = 0; i < 20; i += 1) await Promise.resolve();
  });
}

let tab: { closed: boolean; close: ReturnType<typeof vi.fn> };
let objectUrlSeq = 0;

beforeEach(() => {
  vi.clearAllMocks();
  objectUrlSeq = 0;
  tab = { closed: false, close: vi.fn() };
  mocks.openLoadingDocumentTab.mockImplementation(() => tab as unknown as Window);
  mocks.fetchAttachmentPreviewUrl.mockRejectedValue(new Error('preview_url_empty'));
  mocks.downloadAttachment.mockImplementation(async (_i: string, attachmentId: string) => ({
    blob: new Blob([`%PDF-${attachmentId}`], { type: 'application/pdf' }),
    filename: `${attachmentId}.pdf`,
    mimetype: 'application/pdf',
  }));
  vi.stubGlobal(
    'URL',
    Object.assign(URL, {
      createObjectURL: vi.fn(() => `blob:mock-${++objectUrlSeq}`),
      revokeObjectURL: vi.fn(),
    }),
  );
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function renderVisor(props: Partial<React.ComponentProps<typeof ExpedienteVisor>> = {}) {
  return render(
    <ExpedienteVisor
      instanceId={INSTANCE}
      attachments={[CONSOLIDADO_PREVIO, FUR]}
      checklist={CHECKLIST}
      consolidadoWizard={vigencia('desactualizado')}
      {...props}
    />,
  );
}

function clicVer() {
  act(() => {
    screen.getByRole('button', VER).click();
  });
}

describe('HU #12800 AC1 — indicador de progreso inmediato al abrir un consolidado desactualizado', () => {
  it('desactualizado: el panel «Reconstrucción en curso» aparece en el mismo clic, antes de que responda el backend', () => {
    mocks.generarConsolidado.mockReturnValue(diferida<GenerarConsolidadoResult>().promise);
    renderVisor();

    clicVer();

    const panel = screen.getByTestId('consolidado-reconstruccion');
    expect(panel).toHaveAttribute('role', 'status');
    expect(panel).toHaveAttribute('aria-live', 'polite');
    expect(panel).toHaveTextContent('Reconstrucción en curso');
    expect(within(panel).getByRole('progressbar', { name: /Progreso de la reconstrucción/ })).toBeInTheDocument();
    // La pestaña se abre dentro del gesto del usuario (no 30 s después, cuando el navegador la bloquearía).
    expect(mocks.openLoadingDocumentTab).toHaveBeenCalledTimes(1);
  });

  it('inexistente (primera generación) también muestra el panel', () => {
    mocks.generarConsolidado.mockReturnValue(diferida<GenerarConsolidadoResult>().promise);
    renderVisor({ attachments: [], consolidadoWizard: vigencia('inexistente') });

    clicVer();

    expect(screen.getByTestId('consolidado-reconstruccion')).toBeInTheDocument();
  });

  it('vigente: apertura directa, sin panel de reconstrucción y sin force', async () => {
    mocks.generarConsolidado.mockResolvedValue(resultado('att-cons-v1', { regenerado: false }));
    renderVisor({ consolidadoWizard: vigencia('vigente') });

    clicVer();

    expect(screen.queryByTestId('consolidado-reconstruccion')).toBeNull();
    await flush();
    expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE);
    expect(mocks.openObjectUrlInWindow).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId('consolidado-reconstruccion')).toBeNull();
  });

  it('contrato requiereReconstruccion: solo desactualizado/inexistente y nunca en definitivo o vigencia desconocida', () => {
    expect(requiereReconstruccion(vigencia('desactualizado'))).toBe(true);
    expect(requiereReconstruccion(vigencia('inexistente'))).toBe(true);
    expect(requiereReconstruccion(vigencia('vigente'))).toBe(false);
    expect(requiereReconstruccion(vigencia('desactualizado', true))).toBe(false);
    expect(requiereReconstruccion(null)).toBe(false);
    expect(requiereReconstruccion(undefined)).toBe(false);
  });
});

describe('HU #12800 AC2 — la interfaz sigue utilizable durante la reconstrucción', () => {
  it('sin overlay modal: los demás documentos se pueden abrir mientras reconstruye', async () => {
    mocks.generarConsolidado.mockReturnValue(diferida<GenerarConsolidadoResult>().promise);
    renderVisor();

    clicVer();
    expect(screen.getByTestId('consolidado-reconstruccion')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();

    const verFur = screen.getByRole('button', { name: /Ver PDF de/ });
    expect(verFur).toBeEnabled();
    act(() => verFur.click());
    await flush();

    expect(mocks.downloadAttachment).toHaveBeenCalledWith(INSTANCE, 'att-fur', undefined, 'fur.pdf');
    // La reconstrucción sigue en curso: el panel continúa y no se disparó otra petición.
    expect(screen.getByTestId('consolidado-reconstruccion')).toBeInTheDocument();
    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1);
  });

  it('las acciones del propio consolidado quedan protegidas: un clic en «Re-generar» no lanza otra reconstrucción', async () => {
    mocks.generarConsolidado.mockReturnValue(diferida<GenerarConsolidadoResult>().promise);
    renderVisor();

    clicVer();
    const regenerar = screen.getByRole('button', REGENERAR);
    expect(regenerar).toBeDisabled();
    act(() => regenerar.click());
    await flush();

    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1);
    expect(mocks.generarConsolidado).toHaveBeenCalledWith(INSTANCE);
  });

  it('bajo StrictMode un doble clic en «Ver expediente» envía una sola petición', async () => {
    mocks.generarConsolidado.mockReturnValue(diferida<GenerarConsolidadoResult>().promise);
    render(
      <StrictMode>
        <ExpedienteVisor
          instanceId={INSTANCE}
          attachments={[CONSOLIDADO_PREVIO]}
          consolidadoWizard={vigencia('desactualizado')}
        />
      </StrictMode>,
    );
    const boton = screen.getByRole('button', VER);
    act(() => {
      boton.click();
      boton.click();
    });
    await flush();
    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1);
    expect(mocks.openLoadingDocumentTab).toHaveBeenCalledTimes(1);
  });
});

describe('HU #12800 AC3 — al terminar, el visor carga el PDF sin recargar la página', () => {
  it('carga el PDF nuevo en la pestaña abierta con el clic y retira el panel', async () => {
    const d = diferida<GenerarConsolidadoResult>();
    mocks.generarConsolidado.mockReturnValue(d.promise);
    const onAttachmentsChange = vi.fn();
    renderVisor({ onAttachmentsChange });

    clicVer();
    await act(async () => d.resolve(resultado('att-cons-v2')));
    await flush();

    expect(mocks.downloadAttachment).toHaveBeenCalledWith(INSTANCE, 'att-cons-v2', undefined, 'att-cons-v2.pdf');
    // Misma pestaña (la pre-abierta), sin navegación de la página del expediente.
    expect(mocks.openObjectUrlInWindow).toHaveBeenCalledWith('blob:mock-1', tab);
    expect(screen.queryByTestId('consolidado-reconstruccion')).toBeNull();
    expect(onAttachmentsChange).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', VER)).toBeEnabled();
  });

  it('el hook expone resultado, avisos y regenerado de la entrega (consumo HU #12799)', async () => {
    const abrirAdjunto = vi.fn(async () => undefined);
    mocks.generarConsolidado.mockResolvedValue(
      resultado('att-cons-v2', { avisosCascada: ['impronta: provider_unavailable'], regenerado: true }),
    );
    const { result } = renderHook(() =>
      useAperturaConsolidado({
        instanceId: INSTANCE,
        vigencia: vigencia('desactualizado'),
        consolidadoPrevio: CONSOLIDADO_PREVIO,
        abrirAdjunto,
      }),
    );

    await act(async () => {
      await result.current.abrir();
    });

    expect(result.current.resultado?.document.attachmentId).toBe('att-cons-v2');
    expect(result.current.avisos).toEqual(['impronta: provider_unavailable']);
    expect(result.current.regenerado).toBe(true);
    expect(result.current.fase).toBe('inactivo');
    expect(result.current.enVuelo).toBe(false);
    expect(abrirAdjunto).toHaveBeenCalledWith(
      INSTANCE,
      expect.objectContaining({ id: 'att-cons-v2', tipo: 'consolidado' }),
      tab,
    );
  });

  it('al desmontar con la reconstrucción en vuelo, el resultado tardío se descarta y la pestaña de carga se cierra', async () => {
    const d = diferida<GenerarConsolidadoResult>();
    mocks.generarConsolidado.mockReturnValue(d.promise);
    const { unmount } = renderVisor();

    clicVer();
    unmount();
    expect(tab.close).toHaveBeenCalledTimes(1);

    await act(async () => d.resolve(resultado('att-cons-v2')));
    await flush();
    expect(mocks.downloadAttachment).not.toHaveBeenCalled();
    expect(mocks.openObjectUrlInWindow).not.toHaveBeenCalled();
  });

  it('si la reconstrucción falla, muestra el error accesible y cierra la pestaña de carga', async () => {
    const d = diferida<GenerarConsolidadoResult>();
    mocks.generarConsolidado.mockReturnValue(d.promise);
    renderVisor();

    clicVer();
    await act(async () => d.reject(new Error('')));
    await flush();

    expect(screen.getByRole('alert')).toHaveTextContent(/No se pudo generar el consolidado/);
    expect(screen.queryByTestId('consolidado-reconstruccion')).toBeNull();
    expect(tab.close).toHaveBeenCalledTimes(1);
  });

  it('reacciona a la vigencia re-leída por el padre (#12792): vigente → apertura directa; desactualizado de nuevo → panel', async () => {
    const d1 = diferida<GenerarConsolidadoResult>();
    mocks.generarConsolidado.mockReturnValueOnce(d1.promise);
    const { rerender } = renderVisor();

    clicVer();
    expect(screen.getByTestId('consolidado-reconstruccion')).toBeInTheDocument();
    await act(async () => d1.resolve(resultado('att-cons-v2')));
    await flush();

    // El padre re-lee getInstance tras onAttachmentsChange: la vigencia vuelve «vigente».
    const vigenteNueva = { ...vigencia('vigente'), generadoEn: '2026-09-23T15:00:00Z' };
    rerender(
      <ExpedienteVisor
        instanceId={INSTANCE}
        attachments={[CONSOLIDADO_PREVIO, FUR]}
        checklist={CHECKLIST}
        consolidadoWizard={vigenteNueva}
      />,
    );
    mocks.generarConsolidado.mockResolvedValueOnce(resultado('att-cons-v2', { regenerado: false }));
    clicVer();
    expect(screen.queryByTestId('consolidado-reconstruccion')).toBeNull();
    await flush();

    // El gestor edita un dato: el backend baja la bandera y el padre re-lee «desactualizado».
    rerender(
      <ExpedienteVisor
        instanceId={INSTANCE}
        attachments={[CONSOLIDADO_PREVIO, FUR]}
        checklist={CHECKLIST}
        consolidadoWizard={{ ...vigenteNueva, estado: 'desactualizado' }}
      />,
    );
    mocks.generarConsolidado.mockReturnValueOnce(diferida<GenerarConsolidadoResult>().promise);
    clicVer();
    expect(screen.getByTestId('consolidado-reconstruccion')).toBeInTheDocument();
    await flush();
    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(3);
  });

  it('contrato consolidadoDeResultado: respuesta anidada, plana y nula', () => {
    expect(consolidadoDeResultado(resultado('a1'))).toEqual({ id: 'a1', filename: 'a1.pdf' });
    expect(
      consolidadoDeResultado({ attachmentId: 'a2' } as unknown as GenerarConsolidadoResult),
    ).toEqual({ id: 'a2', filename: 'consolidado.pdf' });
    expect(consolidadoDeResultado(null)).toBeNull();
  });
});

describe('HU #12800 AC4 — timeout controlado', () => {
  it(`al superar ${TIMEOUT_RECONSTRUCCION_MS} ms abre el PDF anterior con advertencia y, al terminar, carga el nuevo`, async () => {
    vi.useFakeTimers();
    const d = diferida<GenerarConsolidadoResult>();
    mocks.generarConsolidado.mockReturnValue(d.promise);
    renderVisor();

    clicVer();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(TIMEOUT_RECONSTRUCCION_MS - 1);
    });
    expect(screen.queryByTestId('consolidado-timeout')).toBeNull();
    expect(mocks.downloadAttachment).not.toHaveBeenCalled();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });
    await flush();

    const aviso = screen.getByTestId('consolidado-timeout');
    expect(aviso).toHaveAttribute('role', 'status');
    expect(aviso).toHaveTextContent(COPY_TIMEOUT_CON_ANTERIOR);
    expect(mocks.downloadAttachment).toHaveBeenCalledWith(
      INSTANCE,
      'att-cons-v1',
      undefined,
      'consolidado-v1.pdf',
    );
    expect(mocks.openObjectUrlInWindow).toHaveBeenNthCalledWith(1, 'blob:mock-1', tab);

    // La petición original sigue en vuelo; cuando termina, se carga el PDF nuevo.
    await act(async () => d.resolve(resultado('att-cons-v2')));
    await flush();
    expect(mocks.downloadAttachment).toHaveBeenLastCalledWith(
      INSTANCE,
      'att-cons-v2',
      undefined,
      'att-cons-v2.pdf',
    );
    expect(mocks.openObjectUrlInWindow).toHaveBeenNthCalledWith(2, 'blob:mock-2', tab);
    expect(screen.queryByTestId('consolidado-timeout')).toBeNull();
    expect(screen.getByTestId('consolidado-actualizado')).toBeInTheDocument();
    expect(mocks.generarConsolidado).toHaveBeenCalledTimes(1);
  });

  it('sin PDF anterior: advierte que sigue en proceso y no abre nada', async () => {
    vi.useFakeTimers();
    mocks.generarConsolidado.mockReturnValue(diferida<GenerarConsolidadoResult>().promise);
    renderVisor({ attachments: [FUR], consolidadoWizard: vigencia('inexistente') });

    clicVer();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(TIMEOUT_RECONSTRUCCION_MS);
    });
    await flush();

    expect(screen.getByTestId('consolidado-timeout')).toHaveTextContent(COPY_TIMEOUT_SIN_ANTERIOR);
    expect(mocks.downloadAttachment).not.toHaveBeenCalled();
    expect(mocks.openObjectUrlInWindow).not.toHaveBeenCalled();
  });

  it('si la reconstrucción termina antes del máximo, el timeout no dispara ni abre el anterior', async () => {
    vi.useFakeTimers();
    const d = diferida<GenerarConsolidadoResult>();
    mocks.generarConsolidado.mockReturnValue(d.promise);
    renderVisor();

    clicVer();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });
    await act(async () => d.resolve(resultado('att-cons-v2')));
    await flush();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(TIMEOUT_RECONSTRUCCION_MS * 2);
    });

    expect(screen.queryByTestId('consolidado-timeout')).toBeNull();
    expect(mocks.downloadAttachment.mock.calls.map((c) => c[1])).toEqual(['att-cons-v2']);
  });

  it('el máximo de espera es configurable en el hook (timeoutMs)', async () => {
    vi.useFakeTimers();
    const abrirAdjunto = vi.fn(async () => undefined);
    mocks.generarConsolidado.mockReturnValue(diferida<GenerarConsolidadoResult>().promise);
    const { result } = renderHook(() =>
      useAperturaConsolidado({
        instanceId: INSTANCE,
        vigencia: vigencia('desactualizado'),
        consolidadoPrevio: CONSOLIDADO_PREVIO,
        abrirAdjunto,
        timeoutMs: 1_000,
      }),
    );

    act(() => {
      void result.current.abrir();
    });
    expect(result.current.fase).toBe('reconstruyendo');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1_000);
    });

    expect(result.current.fase).toBe('timeout');
    expect(result.current.sirvioAnterior).toBe(true);
    expect(result.current.enVuelo).toBe(true);
    expect(abrirAdjunto).toHaveBeenCalledWith(INSTANCE, CONSOLIDADO_PREVIO, tab);
  });
});
