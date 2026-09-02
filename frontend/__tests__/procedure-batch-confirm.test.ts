import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { useProcedureBatchUpload } from '@/hooks/useProcedureBatchUpload';
import { resetTiposOcrCache } from '@/hooks/useProcedureDocuments';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  BatchOcrPiece,
  ChecklistItemView,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getInstance: vi.fn(),
    analyzeBatch: vi.fn(),
    uploadAttachment: vi.fn(),
    deleteAttachment: vi.fn(),
    persistOcrFields: vi.fn(),
    // HU #12034 — qué tipos tienen OCR lo declara el backend.
    listOcrTipos: vi.fn(),
  },
}));

const client = vi.mocked(tramitesClient);
const INSTANCE = 'inst-1';

function pieza(over: Partial<BatchOcrPiece> = {}): BatchOcrPiece {
  return {
    tipo: 'soat',
    sourceFilename: 'expediente.pdf',
    filename: 'soat_expediente.pdf',
    mimetype: 'application/pdf',
    sizeBytes: 8,
    paginas: [5],
    totalPaginasOrigen: 16,
    confianza: 0.9,
    motivo: null,
    data: { es_valido: true, numero_poliza: 'SOAT-1' },
    analisisError: null,
    contentBase64: 'JVBERi0=',
    ...over,
  };
}

const CHECKLIST = [
  { key: 'soat', label: 'SOAT', obligatorio: true, satisfied: false, docTipo: 'soat' },
  { key: 'impronta', label: 'Impronta', obligatorio: true, satisfied: false, docTipo: 'impronta' },
] as ChecklistItemView[];

/** Analiza un lote y deja el hook en la pantalla de revisión. */
async function enRevision(piezas: BatchOcrPiece[], attachments: ProcedureAttachment[] = []) {
  client.analyzeBatch.mockResolvedValue({ piezas, noReconocidos: [], errores: [] });

  const view = renderHook(() =>
    useProcedureBatchUpload(INSTANCE, { modalidad: 'matricula_inicial' }),
  );

  await act(async () => {
    await view.result.current.analyze([new File(['x'], 'expediente.pdf')], CHECKLIST, attachments);
  });
  await waitFor(() => expect(view.result.current.state.phase).toBe('reviewing'));

  return view;
}

beforeEach(() => {
  vi.clearAllMocks();
  // La lista de tipos con OCR se cachea por módulo: sin resetear, el primer test fijaría la de
  // todos los demás.
  resetTiposOcrCache();
  client.listOcrTipos.mockResolvedValue(['soat', 'impronta', 'rtm', 'factura']);
  client.getInstance.mockResolvedValue({ fieldValues: [] } as never);
  client.uploadAttachment.mockResolvedValue(undefined as never);
  client.deleteAttachment.mockResolvedValue(undefined as never);
  client.persistOcrFields.mockResolvedValue(undefined as never);
});

describe('useProcedureBatchUpload — análisis', () => {
  it('sólo pide clasificar los tipos que el checklist muestra y tienen OCR', async () => {
    await enRevision([pieza()]);

    // HU #12034 — el orden ya no lo fija una lista hardcodeada sino el propio checklist, que viene
    // ordenado por `default_sort_order` del trámite. Es igual de determinista y además significativo.
    expect(client.analyzeBatch).toHaveBeenCalledWith(
      ['soat', 'impronta'],
      expect.any(Array),
      undefined,
    );
  });

  it('descarta del lote los tipos visibles que NO tienen OCR', async () => {
    client.listOcrTipos.mockResolvedValue(['soat']);
    resetTiposOcrCache();

    await enRevision([pieza()]);

    expect(client.analyzeBatch).toHaveBeenCalledWith(['soat'], expect.any(Array), undefined);
  });

  it('si no se puede consultar qué tipos tienen OCR, propone todos los visibles', async () => {
    // Falla ABIERTO: el backend descarta los que no tengan prompt. Mandar una lista corta en
    // silencio dejaría documentos sin analizar sin que nadie se entere.
    client.listOcrTipos.mockRejectedValue(new Error('backend caído'));
    resetTiposOcrCache();

    await enRevision([pieza()]);

    expect(client.analyzeBatch).toHaveBeenCalledWith(
      ['soat', 'impronta'],
      expect.any(Array),
      undefined,
    );
  });

  it('un fallo del lote deja el error visible y no entra en revisión', async () => {
    client.analyzeBatch.mockRejectedValue(new Error('Servicio no disponible.'));
    const { result } = renderHook(() => useProcedureBatchUpload(INSTANCE));

    await act(async () => {
      await result.current.analyze([new File(['x'], 'a.pdf')], CHECKLIST, []);
    });

    expect(result.current.state.phase).toBe('idle');
    expect(result.current.state.error).toBe('Servicio no disponible.');
  });

  it('manda un archivo por petición, no el lote entero', async () => {
    // El lote completo en una sola petición acumulaba ~90 s de silencio con 4 expedientes y el
    // proxy lo cortaba con un 504, perdiéndolo todo.
    client.analyzeBatch.mockResolvedValue({ piezas: [], noReconocidos: [], errores: [] });
    const { result } = renderHook(() => useProcedureBatchUpload(INSTANCE));
    const files = [
      new File(['a'], 'uno.pdf'),
      new File(['b'], 'dos.pdf'),
      new File(['c'], 'tres.pdf'),
    ];

    await act(async () => {
      await result.current.analyze(files, CHECKLIST, []);
    });

    expect(client.analyzeBatch).toHaveBeenCalledTimes(3);
    for (const file of files) {
      expect(client.analyzeBatch).toHaveBeenCalledWith(['soat', 'impronta'], [file], undefined);
    }
  });

  it('un archivo que falla no se lleva por delante a los demás', async () => {
    client.analyzeBatch
      .mockResolvedValueOnce({ piezas: [pieza({ sourceFilename: 'uno.pdf' })], noReconocidos: [], errores: [] })
      .mockRejectedValueOnce(new Error('El archivo está dañado.'))
      .mockResolvedValueOnce({ piezas: [pieza({ tipo: 'impronta', sourceFilename: 'tres.pdf' })], noReconocidos: [], errores: [] });

    const { result } = renderHook(() => useProcedureBatchUpload(INSTANCE));
    await act(async () => {
      await result.current.analyze(
        [new File(['a'], 'uno.pdf'), new File(['b'], 'dos.pdf'), new File(['c'], 'tres.pdf')],
        CHECKLIST,
        [],
      );
    });

    expect(result.current.state.phase).toBe('reviewing');
    expect(result.current.state.items).toHaveLength(2);
    // El que falló baja a la lista de errores por archivo, con su nombre.
    expect(result.current.state.errores).toEqual([
      { filename: 'dos.pdf', motivo: 'El archivo está dañado.' },
    ]);
  });

  it('la mejor confianza por tipo se decide sobre el lote entero, no por archivo', async () => {
    // La factura del primer archivo llega antes, pero la del último es mejor: aplicar la regla por
    // trozo dejaría marcada la peor.
    client.analyzeBatch
      .mockResolvedValueOnce({ piezas: [pieza({ sourceFilename: 'uno.pdf', confianza: 0.6 })], noReconocidos: [], errores: [] })
      .mockResolvedValueOnce({ piezas: [pieza({ sourceFilename: 'dos.pdf', confianza: 0.95 })], noReconocidos: [], errores: [] });

    const { result } = renderHook(() => useProcedureBatchUpload(INSTANCE));
    await act(async () => {
      await result.current.analyze(
        [new File(['a'], 'uno.pdf'), new File(['b'], 'dos.pdf')],
        CHECKLIST,
        [],
      );
    });

    const marcadas = result.current.state.items.filter((i) => i.decision === 'accept');
    expect(marcadas).toHaveLength(1);
    expect(marcadas[0].piece.sourceFilename).toBe('dos.pdf');
  });

  it('si TODOS los archivos fallan, no entra en revisión', async () => {
    client.analyzeBatch.mockRejectedValue(new Error('Servicio no disponible.'));
    const { result } = renderHook(() => useProcedureBatchUpload(INSTANCE));

    await act(async () => {
      await result.current.analyze(
        [new File(['a'], 'uno.pdf'), new File(['b'], 'dos.pdf')],
        CHECKLIST,
        [],
      );
    });

    expect(result.current.state.phase).toBe('idle');
    expect(result.current.state.error).toBe('Servicio no disponible.');
  });

  it('rechaza el lote antes de llamar si excede los topes', async () => {
    const { result } = renderHook(() => useProcedureBatchUpload(INSTANCE));
    const files = Array.from({ length: 25 }, (_, i) => new File(['x'], `f${i}.pdf`));

    await act(async () => {
      await result.current.analyze(files, CHECKLIST, []);
    });

    expect(client.analyzeBatch).not.toHaveBeenCalled();
    expect(result.current.state.error).toContain('20');
  });
});

describe('useProcedureBatchUpload — selección', () => {
  it('marcar una pieza libera la casilla de la otra del mismo tipo', async () => {
    const { result } = await enRevision([
      pieza({ confianza: 0.95, paginas: [5] }),
      pieza({ confianza: 0.6, paginas: [9] }),
    ]);

    const perdedora = result.current.state.items.find((i) => i.decision === 'skip')!;
    act(() => result.current.setDecision(perdedora.id, 'accept'));

    expect(result.current.aceptadas).toHaveLength(1);
    expect(result.current.aceptadas[0].id).toBe(perdedora.id);
  });
});

describe('useProcedureBatchUpload — confirmar', () => {
  it('sube sólo lo marcado, por el flujo de adjuntos de siempre', async () => {
    const { result } = await enRevision([
      pieza(),
      pieza({ tipo: 'impronta', filename: 'impronta_expediente.pdf', paginas: [14] }),
    ]);

    const impronta = result.current.state.items.find((i) => i.piece.tipo === 'impronta')!;
    act(() => result.current.setDecision(impronta.id, 'skip'));

    await act(async () => {
      await result.current.confirm();
    });

    expect(client.uploadAttachment).toHaveBeenCalledTimes(1);
    const [instanceId, tipo, file] = client.uploadAttachment.mock.calls[0];
    expect(instanceId).toBe(INSTANCE);
    expect(tipo).toBe('soat');
    expect((file as File).name).toBe('soat_expediente.pdf');
    expect((file as File).type).toBe('application/pdf');
  });

  it('reemplazar borra primero el adjunto anterior', async () => {
    const adjunto = { id: 'att-viejo', tipo: 'soat', filename: 'viejo.pdf' } as ProcedureAttachment;
    const { result } = await enRevision([pieza()], [adjunto]);

    // Llega desmarcada por el conflicto: el operador la marca aceptando el aviso de reemplazo.
    const item = result.current.state.items[0];
    expect(item.decision).toBe('skip');
    act(() => result.current.setDecision(item.id, 'accept'));

    await act(async () => {
      await result.current.confirm();
    });

    expect(client.deleteAttachment).toHaveBeenCalledWith(INSTANCE, 'att-viejo', undefined);
    expect(client.uploadAttachment).toHaveBeenCalledTimes(1);
  });

  it('persiste los campos de SOAT verificado, igual que el cargue campo a campo', async () => {
    const { result } = await enRevision([pieza()]);

    await act(async () => {
      await result.current.confirm();
    });

    expect(client.persistOcrFields).toHaveBeenCalledWith(
      INSTANCE,
      'soat',
      expect.objectContaining({ numero_poliza: 'SOAT-1' }),
      undefined,
    );
  });

  it('no persiste campos de un documento rechazado', async () => {
    // VIN que no cruza: el documento puede adjuntarse, pero de él no se toma ningún dato.
    client.getInstance.mockResolvedValue({
      fieldValues: [{ fieldKey: 'vin', valueText: 'BBBBBBBBBBBBBBBBB' }],
    } as never);
    const { result } = await enRevision([
      pieza({ data: { es_valido: true, vehiculo_vin: 'AAAAAAAAAAAAAAAAA' } }),
    ]);

    const item = result.current.state.items[0];
    expect(item.evaluation.rechazado).toBe(true);
    act(() => result.current.setDecision(item.id, 'accept'));

    await act(async () => {
      await result.current.confirm();
    });

    expect(client.uploadAttachment).toHaveBeenCalledTimes(1);
    expect(client.persistOcrFields).not.toHaveBeenCalled();
  });

  it('no persiste tipos fuera de la whitelist', async () => {
    const { result } = await enRevision([
      pieza({ tipo: 'impronta', filename: 'impronta_expediente.pdf' }),
    ]);

    await act(async () => {
      await result.current.confirm();
    });

    expect(client.uploadAttachment).toHaveBeenCalledTimes(1);
    expect(client.persistOcrFields).not.toHaveBeenCalled();
  });

  it('un fallo de persistencia no le cuesta el adjunto al operador', async () => {
    client.persistOcrFields.mockRejectedValue(new Error('boom'));
    const { result } = await enRevision([pieza()]);

    let ok = false;
    await act(async () => {
      ok = await result.current.confirm();
    });

    expect(ok).toBe(true);
    expect(result.current.state.phase).toBe('idle');
  });

  it('si una pieza falla, las que entraron no quedan para reintentar', async () => {
    const { result } = await enRevision([
      pieza(),
      pieza({ tipo: 'impronta', filename: 'impronta_expediente.pdf', paginas: [14] }),
    ]);

    client.uploadAttachment
      .mockResolvedValueOnce(undefined as never)
      .mockRejectedValueOnce(new Error('S3 no responde'));

    let ok = true;
    await act(async () => {
      ok = await result.current.confirm();
    });

    expect(ok).toBe(false);
    expect(result.current.state.phase).toBe('reviewing');
    // Sólo queda la fallida: reintentar no puede duplicar la que ya se subió.
    expect(result.current.state.items).toHaveLength(1);
    expect(result.current.state.errores).toHaveLength(1);
    expect(result.current.state.errores[0].motivo).toBe('S3 no responde');
  });

  it('sin nada marcado no hace nada', async () => {
    const adjunto = { id: 'att-1', tipo: 'soat', filename: 'viejo.pdf' } as ProcedureAttachment;
    const { result } = await enRevision([pieza()], [adjunto]);

    let ok = true;
    await act(async () => {
      ok = await result.current.confirm();
    });

    expect(ok).toBe(false);
    expect(client.uploadAttachment).not.toHaveBeenCalled();
  });

  it('descartar la revisión no sube nada', async () => {
    const { result } = await enRevision([pieza()]);

    act(() => result.current.reset());

    expect(result.current.state.phase).toBe('idle');
    expect(client.uploadAttachment).not.toHaveBeenCalled();
  });
});
