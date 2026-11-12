import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ChecklistView, ProcedureAttachment } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  getInstance: vi.fn(),
  analyzeDocument: vi.fn(),
  persistOcrFields: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  listOcrTipos: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getChecklist: mocks.getChecklist,
    getAttachments: mocks.getAttachments,
    getInstance: mocks.getInstance,
    analyzeDocument: mocks.analyzeDocument,
    persistOcrFields: mocks.persistOcrFields,
    uploadAttachment: mocks.uploadAttachment,
    deleteAttachment: mocks.deleteAttachment,
    listOcrTipos: mocks.listOcrTipos,
  },
}));

import {
  OCR_MAX_BYTES,
  deriveOcrFromAttachments,
  ocrResultForTipo,
  resetTiposOcrCache,
  useProcedureDocuments,
} from '../useProcedureDocuments';

/**
 * Bug #14559 — el veredicto OCR («No coincide») se perdía al cambiar de paso del wizard y volver:
 * vivía solo en el estado del hook y el remontaje solo releía checklist + adjuntos. Ahora viaja en
 * el `register` (código cerrado) y se rehidrata desde el adjunto.
 *
 * Uso de ejemplo:
 *   deriveOcrFromAttachments([{ tipo: 'factura', ocrStatus: 'rejected', ocrReason: 'vin_mismatch', … }])
 *   → { factura: { status: 'rejected', motivo: 'El VIN del documento no coincide…', data: null } }
 */
const INSTANCIA = '22222222-2222-2222-2222-222222222222';

const CHECKLIST: ChecklistView = {
  items: [{ key: 'factura', label: 'Factura', obligatorio: true, docTipo: 'FACTURA', satisfied: true }],
  faltanObligatorios: 0,
  completo: true,
};

function adjunto(over: Partial<ProcedureAttachment> = {}): ProcedureAttachment {
  return {
    id: 'att-1',
    tipo: 'factura',
    filename: 'factura.pdf',
    mimetype: 'application/pdf',
    sizeBytes: 100,
    sha256: 'abc',
    source: 'user',
    uploadedAt: '2026-11-12T10:00:00Z',
    ...over,
  };
}

function archivo(size?: number): File {
  const bytes = size ? new Uint8Array(size) : new Uint8Array([1, 2, 3]);
  return new File([bytes], 'doc.pdf', { type: 'application/pdf' });
}

async function montarListo() {
  const hook = renderHook(() => useProcedureDocuments(INSTANCIA));
  await waitFor(() => expect(hook.result.current.state.checklist).not.toBeNull());
  // El VIN del trámite se lee aparte (best-effort): se deja resolver antes de subir.
  await act(async () => {});
  return hook;
}

beforeEach(() => {
  vi.clearAllMocks();
  resetTiposOcrCache();
  mocks.listOcrTipos.mockResolvedValue(['factura', 'soat', 'otro']);
  mocks.getChecklist.mockResolvedValue(CHECKLIST);
  mocks.getAttachments.mockResolvedValue([]);
  mocks.getInstance.mockResolvedValue({
    id: INSTANCIA,
    fieldValues: [{ fieldKey: 'vin', valueText: 'AAA111' }],
  });
  mocks.analyzeDocument.mockResolvedValue({ ok: true, tipo: 'factura', data: { es_factura_valida: true } });
  mocks.persistOcrFields.mockResolvedValue(undefined);
  mocks.uploadAttachment.mockResolvedValue({ id: 'att-1' });
});

describe('Bug #14559 — rehidratación del veredicto OCR desde los adjuntos', () => {
  it('a. al remontar con un adjunto rejected/vin_mismatch el tipo queda rechazado y el motivo habla del VIN', async () => {
    mocks.getAttachments.mockResolvedValue([adjunto({ ocrStatus: 'rejected', ocrReason: 'vin_mismatch' })]);

    const primero = await montarListo();
    primero.unmount();

    const { result } = await montarListo();
    const ocr = ocrResultForTipo(result.current.state.ocrResults, 'FACTURA');
    expect(ocr?.status).toBe('rejected');
    expect(ocr?.motivo).toMatch(/VIN/);
    expect(ocr?.data).toBeNull();
  });

  it('b. ocrStatus null o ausente no crea entrada (sin backfill ⇒ «Cargado»)', async () => {
    mocks.getAttachments.mockResolvedValue([
      adjunto({ id: 'a1', tipo: 'factura', ocrStatus: null }),
      adjunto({ id: 'a2', tipo: 'soat' }),
    ]);
    const { result } = await montarListo();
    expect(result.current.state.ocrResults).toEqual({});
  });

  it('c. en un tipo múltiple gana el adjunto con uploadedAt más reciente', () => {
    const derivado = deriveOcrFromAttachments([
      adjunto({ id: 'n', tipo: 'otro', uploadedAt: '2026-11-12T12:00:00Z', ocrStatus: 'verified' }),
      adjunto({
        id: 'v',
        tipo: 'otro',
        uploadedAt: '2026-11-12T09:00:00Z',
        ocrStatus: 'rejected',
        ocrReason: 'tipo_invalido',
      }),
    ]);
    expect(derivado.otro?.status).toBe('verified');

    const alReves = deriveOcrFromAttachments([
      adjunto({ id: 'v', tipo: 'otro', uploadedAt: '2026-11-12T09:00:00Z', ocrStatus: 'verified' }),
      adjunto({
        id: 'n',
        tipo: 'otro',
        uploadedAt: '2026-11-12T12:00:00Z',
        ocrStatus: 'skipped',
        ocrReason: 'excede_tamano',
      }),
    ]);
    expect(alReves.otro).toEqual({
      status: 'skipped',
      motivo: 'Archivo mayor a 10 MB: subido sin análisis automático.',
      data: null,
    });
  });

  it('c2. el más reciente sin veredicto no hereda el veredicto de uno anterior', () => {
    const derivado = deriveOcrFromAttachments([
      adjunto({ id: 'v', tipo: 'otro', uploadedAt: '2026-11-12T09:00:00Z', ocrStatus: 'rejected' }),
      adjunto({ id: 'n', tipo: 'otro', uploadedAt: '2026-11-12T12:00:00Z', ocrStatus: null }),
    ]);
    expect(derivado).toEqual({});
  });

  it('d. lo de la sesión (con data y motivo rico) gana sobre lo derivado tras el refresh', async () => {
    mocks.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'factura',
      data: { es_factura_valida: true, vehiculo_vin: 'BBB222' },
    });
    mocks.uploadAttachment.mockImplementation(async () => {
      mocks.getAttachments.mockResolvedValue([adjunto({ ocrStatus: 'rejected', ocrReason: 'vin_mismatch' })]);
      return { id: 'att-1' };
    });

    const { result } = await montarListo();
    await act(async () => {
      await result.current.upload('FACTURA', archivo());
    });

    const ocr = ocrResultForTipo(result.current.state.ocrResults, 'factura');
    expect(ocr?.status).toBe('rejected');
    expect(ocr?.data).toEqual({ es_factura_valida: true, vehiculo_vin: 'BBB222' });
    expect(ocr?.motivo).toContain('BBB222');
  });
});

describe('Bug #14559 — el upload declara el veredicto al registrar', () => {
  it('e1. VIN que no cruza ⇒ {rejected, vin_mismatch}, sin texto libre ni VIN', async () => {
    mocks.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'factura',
      data: { es_factura_valida: true, vehiculo_vin: 'BBB222', observaciones: 'texto libre' },
    });
    const { result } = await montarListo();
    await act(async () => {
      await result.current.upload('FACTURA', archivo());
    });
    expect(mocks.uploadAttachment).toHaveBeenCalledTimes(1);
    const ocrArg = mocks.uploadAttachment.mock.calls[0][4];
    expect(ocrArg).toEqual({ status: 'rejected', reason: 'vin_mismatch' });
    expect(JSON.stringify(ocrArg)).not.toContain('BBB222');
  });

  it('e2. tipo inválido ⇒ {rejected, tipo_invalido}', async () => {
    mocks.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'factura',
      data: { es_factura_valida: false, observaciones: 'Es un recibo.' },
    });
    const { result } = await montarListo();
    await act(async () => {
      await result.current.upload('FACTURA', archivo());
    });
    expect(mocks.uploadAttachment.mock.calls[0][4]).toEqual({ status: 'rejected', reason: 'tipo_invalido' });
  });

  it('e3. documento verificado ⇒ {verified} sin motivo', async () => {
    const { result } = await montarListo();
    await act(async () => {
      await result.current.upload('FACTURA', archivo());
    });
    expect(mocks.uploadAttachment.mock.calls[0][4]).toEqual({ status: 'verified' });
  });

  it('e4. archivo > 10 MB ⇒ {skipped, excede_tamano}', async () => {
    const { result } = await montarListo();
    await act(async () => {
      await result.current.upload('FACTURA', archivo(OCR_MAX_BYTES + 1));
    });
    expect(mocks.analyzeDocument).not.toHaveBeenCalled();
    expect(mocks.uploadAttachment.mock.calls[0][4]).toEqual({ status: 'skipped', reason: 'excede_tamano' });
  });

  it('e5. fallo del análisis ⇒ {skipped, analisis_fallido}', async () => {
    mocks.analyzeDocument.mockRejectedValue(new Error('503'));
    const { result } = await montarListo();
    await act(async () => {
      await result.current.upload('FACTURA', archivo());
    });
    expect(mocks.uploadAttachment.mock.calls[0][4]).toEqual({ status: 'skipped', reason: 'analisis_fallido' });
  });

  it('e6. tipo sin OCR ⇒ sin 5º argumento', async () => {
    const { result } = await montarListo();
    await act(async () => {
      await result.current.upload('CEDULA', archivo());
    });
    expect(mocks.analyzeDocument).not.toHaveBeenCalled();
    expect(mocks.uploadAttachment.mock.calls[0]).toHaveLength(4);
  });
});
