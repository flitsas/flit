import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useUiPreferences } from '../useUiPreferences';
import { uiPreferencesClient } from '@/lib/api/ui-preferences';

vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: { get: vi.fn(), put: vi.fn() },
}));

const DEFAULTS = ['a', 'b', 'c'];
const OK = { scope: 'x', value: {} };

describe('useUiPreferences — widths (HU #14741)', () => {
  afterEach(() => vi.resetAllMocks());

  it('AC1: setWidth guarda el ancho dentro del mismo valor de la preferencia', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.columns', value: {} });
    vi.mocked(uiPreferencesClient.put).mockResolvedValue(OK);
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    act(() => result.current.setWidth('b', 222.4));
    expect(result.current.widths).toEqual({ b: 222 });
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalledTimes(1));
    expect(uiPreferencesClient.put).toHaveBeenCalledWith(
      'tramites.columns',
      expect.objectContaining({ visible: DEFAULTS, widths: { b: 222 } }),
    );
  });

  it('carga los anchos guardados y descarta valores no numéricos', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({
      scope: 'tramites.columns',
      value: { visible: ['a', 'b'], widths: { a: 150, b: 'x', c: -5 } },
    });
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(result.current.widths).toEqual({ a: 150 });
  });

  it('AC2: resetWidth quita solo esa columna y guarda el resto', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({
      scope: 'tramites.columns',
      value: { visible: ['a', 'b'], widths: { a: 150, b: 200 } },
    });
    vi.mocked(uiPreferencesClient.put).mockResolvedValue(OK);
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    act(() => result.current.resetWidth('a'));
    expect(result.current.widths).toEqual({ b: 200 });
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalled());
    expect(vi.mocked(uiPreferencesClient.put).mock.calls[0][1]).toMatchObject({ widths: { b: 200 } });
  });

  it('«Por defecto» (reset) limpia los anchos y no los envía', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({
      scope: 'tramites.columns',
      value: { visible: ['a', 'b'], widths: { a: 150 } },
    });
    vi.mocked(uiPreferencesClient.put).mockResolvedValue(OK);
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    act(() => result.current.reset());
    expect(result.current.widths).toEqual({});
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalled());
    expect(vi.mocked(uiPreferencesClient.put).mock.calls[0][1]).not.toHaveProperty('widths');
  });

  it('cambiar la visibilidad conserva los anchos guardados', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({
      scope: 'tramites.columns',
      value: { visible: ['a', 'b'], widths: { a: 150 } },
    });
    vi.mocked(uiPreferencesClient.put).mockResolvedValue(OK);
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    act(() => result.current.setVisible(['a']));
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalled());
    expect(vi.mocked(uiPreferencesClient.put).mock.calls[0][1]).toMatchObject({ widths: { a: 150 } });
  });

  it('si no se puede guardar, revierte el ancho y avisa', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.columns', value: {} });
    vi.mocked(uiPreferencesClient.put).mockRejectedValue(new Error('falla'));
    const aviso = vi.fn();
    window.addEventListener('flit:ui-preferences-save-error', aviso);
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    act(() => result.current.setWidth('a', 200));
    await waitFor(() => expect(aviso).toHaveBeenCalled());
    expect(result.current.widths).toEqual({});
    window.removeEventListener('flit:ui-preferences-save-error', aviso);
  });

  it('AC5: cada tabla (scope) guarda y lee sus propios anchos', async () => {
    vi.mocked(uiPreferencesClient.get).mockImplementation(async (scope) => ({
      scope,
      value: scope === 'tramites.columns' ? { widths: { a: 111 } } : { widths: { a: 222 } },
    }));
    vi.mocked(uiPreferencesClient.put).mockResolvedValue(OK);
    const t = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    const o = renderHook(() => useUiPreferences('ot.procedures.columns', DEFAULTS));
    await waitFor(() => expect(t.result.current.status).toBe('ready'));
    await waitFor(() => expect(o.result.current.status).toBe('ready'));
    expect(t.result.current.widths).toEqual({ a: 111 });
    expect(o.result.current.widths).toEqual({ a: 222 });
    act(() => o.result.current.setWidth('a', 300));
    await waitFor(() => expect(uiPreferencesClient.put).toHaveBeenCalledTimes(1));
    expect(uiPreferencesClient.put).toHaveBeenCalledWith('ot.procedures.columns', expect.anything());
    expect(t.result.current.widths).toEqual({ a: 111 });
  });
});
