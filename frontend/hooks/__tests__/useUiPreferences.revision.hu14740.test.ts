import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useUiPreferences } from '../useUiPreferences';
import { uiPreferencesClient } from '@/lib/api/ui-preferences';

// Uso de ejemplo: const { visible, setVisible } = useUiPreferences('tramites.columns', DEFAULTS, { catalog });
// Hallazgos de la revisión de la Épica #14503: marcar/desmarcar no debe deshacer el orden elegido, y un
// ancho guardado enorme se topa al cargar.

vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: { get: vi.fn(), put: vi.fn() },
}));

const CATALOG = ['a', 'b', 'c', 'd'];
const OK = { scope: 'tramites.columns', value: {} };

describe('useUiPreferences — revisión de la Épica #14503', () => {
  afterEach(() => vi.resetAllMocks());

  it('marcar una columna conserva el orden del usuario aunque llegue en orden de catálogo', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({
      scope: 'tramites.columns',
      value: { visible: ['c', 'a'], order: ['c', 'a', 'b', 'd'], known: CATALOG },
    });
    vi.mocked(uiPreferencesClient.put).mockResolvedValue(OK);
    const { result } = renderHook(() =>
      useUiPreferences('tramites.columns', ['a', 'b'], { catalog: CATALOG }),
    );
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(result.current.visible).toEqual(['c', 'a']);

    // El selector de la tabla entrega la selección en orden de catálogo (a, b, c).
    act(() => result.current.setVisible(['a', 'b', 'c']));

    expect(result.current.visible).toEqual(['c', 'a', 'b']);
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalledTimes(1));
    expect(uiPreferencesClient.put).toHaveBeenCalledWith(
      'tramites.columns',
      expect.objectContaining({ visible: ['c', 'a', 'b'] }),
    );
  });

  it('desmarcar una columna no cambia el orden de las demás', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({
      scope: 'tramites.columns',
      value: { visible: ['d', 'b', 'a'], order: ['d', 'b', 'a', 'c'], known: CATALOG },
    });
    vi.mocked(uiPreferencesClient.put).mockResolvedValue(OK);
    const { result } = renderHook(() =>
      useUiPreferences('tramites.columns', ['a', 'b'], { catalog: CATALOG }),
    );
    await waitFor(() => expect(result.current.status).toBe('ready'));
    act(() => result.current.setVisible(['a', 'd']));
    expect(result.current.visible).toEqual(['d', 'a']);
  });

  it('un ancho guardado enorme se topa al cargar', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({
      scope: 'tramites.columns',
      value: { visible: ['a', 'b'], widths: { a: 1e9, b: 200 } },
    });
    const { result } = renderHook(() =>
      useUiPreferences('tramites.columns', ['a', 'b'], { catalog: CATALOG }),
    );
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(result.current.widths).toEqual({ a: 640, b: 200 });
  });
});
