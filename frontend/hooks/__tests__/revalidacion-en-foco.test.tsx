import { renderHook, waitFor, act } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ChecklistView, WizardState } from '@/lib/api/types/procedure-runtime';

const mocks = vi.hoisted(() => ({
  getWizardState: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  getInstance: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getWizardState: mocks.getWizardState,
    getChecklist: mocks.getChecklist,
    getAttachments: mocks.getAttachments,
    getInstance: mocks.getInstance,
  },
}));

import { useWizard } from '../useWizard';
import { useProcedureDocuments } from '../useProcedureDocuments';

const INSTANCIA = '11111111-1111-1111-1111-111111111111';

/** Vuelve a la pestaña: se emiten los dos eventos, como haría el navegador. */
async function volverALaPestana() {
  await act(async () => {
    document.dispatchEvent(new Event('visibilitychange'));
    window.dispatchEvent(new Event('focus'));
  });
}

/** Checklist con la forma real del contrato: sin castear, para que un cambio de forma rompa aquí. */
function checklist(claves: string[]): ChecklistView {
  return {
    items: claves.map((key) => ({
      key,
      label: key,
      obligatorio: true,
      docTipo: key,
      satisfied: false,
    })),
    faltanObligatorios: claves.length,
    completo: claves.length === 0,
  };
}

function wizard(blockers: string[]): WizardState {
  return {
    modalidad: 'matricula_inicial',
    tipologiaCodigo: 'MATRICULA_NUEVA',
    totalSteps: 1,
    steps: [
      {
        index: 1,
        key: 'documentos',
        label: 'Documentos',
        status: blockers.length > 0 ? 'incomplete' : 'complete',
        reasons: blockers,
      },
    ],
    canSubmit: blockers.length === 0,
    blockers,
    status: 'borrador',
    allowedTransitions: [],
  } as unknown as WizardState;
}

/**
 * Un documento OBLIGATORIO dado de alta en el módulo Documental mientras el gestor está parado en el
 * paso de requisitos no llegaba a la pantalla abierta: se quedaba sin casilla donde cargarlo y sin
 * frenar el paso. Reaparecía al reabrir el trámite, porque eso vuelve a montar los hooks — el dato
 * del servidor estaba bien, faltaba pedirlo otra vez. Al volver a la pestaña se revalida.
 */
describe('revalidación al recuperar el foco', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // El hook lee el VIN de la instancia para cruzarlo con el OCR; es best-effort y no debe teñir
    // lo que aquí se prueba.
    mocks.getInstance.mockResolvedValue({ id: INSTANCIA, fieldValues: [] });
    mocks.getAttachments.mockResolvedValue([]);
  });

  it('el checklist recoge el documento obligatorio dado de alta con la pantalla abierta', async () => {
    mocks.getChecklist
      .mockResolvedValueOnce(checklist([]))
      .mockResolvedValue(checklist(['contrato_leasing']));

    const { result } = renderHook(() => useProcedureDocuments(INSTANCIA));
    await waitFor(() => expect(result.current.state.checklist).not.toBeNull());
    expect(result.current.state.checklist?.items).toHaveLength(0);

    await volverALaPestana();

    await waitFor(() =>
      expect(result.current.state.checklist?.items?.[0]?.key).toBe('contrato_leasing'),
    );
  });

  it('el paso vuelve a bloquear porque los bloqueos se releen', async () => {
    mocks.getWizardState
      .mockResolvedValueOnce(wizard([]))
      .mockResolvedValue(wizard(['DOCUMENT_CONTRATO_LEASING_REQUIRED']));

    const { result } = renderHook(() => useWizard(INSTANCIA));
    await waitFor(() => expect(result.current.canSubmit).toBe(true));

    await volverALaPestana();

    await waitFor(() => expect(result.current.canSubmit).toBe(false));
    expect(result.current.blockers).toContain('DOCUMENT_CONTRATO_LEASING_REQUIRED');
  });

  it('la revalidación es silenciosa: no pone la vista en «cargando»', async () => {
    mocks.getChecklist.mockResolvedValue(checklist([]));

    const { result } = renderHook(() => useProcedureDocuments(INSTANCIA));
    await waitFor(() => expect(result.current.state.loading).toBe(false));

    await volverALaPestana();

    // Se dispara sin que el gestor la pida; un parpadeo a media captura es peor que el dato viejo.
    expect(result.current.state.loading).toBe(false);
  });

  it('si la revalidación falla, se conserva lo que ya estaba en pantalla', async () => {
    mocks.getChecklist
      .mockResolvedValueOnce(checklist(['soat']))
      .mockRejectedValue(new Error('red caída'));

    const { result } = renderHook(() => useProcedureDocuments(INSTANCIA));
    await waitFor(() => expect(result.current.state.checklist?.items).toHaveLength(1));

    await volverALaPestana();

    await waitFor(() => expect(mocks.getChecklist).toHaveBeenCalledTimes(2));
    expect(result.current.state.checklist?.items?.[0]?.key).toBe('soat');
    expect(result.current.state.error).toBeNull();
  });

  it('con la pestaña oculta NO se revalida', async () => {
    mocks.getChecklist.mockResolvedValue(checklist([]));

    const { result } = renderHook(() => useProcedureDocuments(INSTANCIA));
    await waitFor(() => expect(result.current.state.checklist).not.toBeNull());
    const llamadas = mocks.getChecklist.mock.calls.length;

    const spy = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    await volverALaPestana();
    spy.mockRestore();

    expect(mocks.getChecklist).toHaveBeenCalledTimes(llamadas);
  });

  it('sin instancia el hook no pide nada al volver a la pestaña', async () => {
    renderHook(() => useProcedureDocuments(null));

    await volverALaPestana();

    expect(mocks.getChecklist).not.toHaveBeenCalled();
  });
});
