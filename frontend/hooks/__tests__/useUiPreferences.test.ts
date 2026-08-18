import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useUiPreferences } from '../useUiPreferences';
import { uiPreferencesClient } from '@/lib/api/ui-preferences';

vi.mock('@/lib/api/ui-preferences', () => ({
  uiPreferencesClient: { get: vi.fn(), put: vi.fn() },
}));

const DEFAULTS = ['a', 'b', 'c'];

describe('useUiPreferences', () => {
  afterEach(() => {
    vi.resetAllMocks();
  });

  it('arranca con las columnas por defecto — nunca en blanco mientras carga', () => {
    vi.mocked(uiPreferencesClient.get).mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    expect(result.current.status).toBe('loading');
    expect(result.current.visible).toEqual(DEFAULTS);
  });

  it('al cargar una preferencia guardada, adopta esas columnas', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({
      scope: 'tramites.columns',
      value: { visible: ['a', 'c'] },
    });
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(result.current.visible).toEqual(['a', 'c']);
  });

  // Una columna añadida al catálogo DESPUÉS de que el usuario guardara su preferencia no puede
  // quedar invisible para siempre: desde el selector se ve desmarcada y parece un dato que falta.
  describe('columnas nuevas frente a una preferencia ya guardada', () => {
    it('con `known` registrado, deduce las nuevas y respeta lo que el usuario ocultó', async () => {
      vi.mocked(uiPreferencesClient.get).mockResolvedValue({
        scope: 'tramites.columns',
        // Conocía a/b/c y ocultó 'b' a propósito. 'd' se añadió después.
        value: { visible: ['a', 'c'], known: ['a', 'b', 'c'] },
      });
      const { result } = renderHook(() =>
        useUiPreferences('tramites.columns', ['a', 'c', 'd'], {
          catalog: ['a', 'b', 'c', 'd'],
        }),
      );
      await waitFor(() => expect(result.current.status).toBe('ready'));
      // Entra 'd' (nueva y visible por defecto); 'b' sigue oculta porque fue decisión suya.
      expect(result.current.visible).toEqual(['a', 'c', 'd']);
    });

    it('en formato antiguo (sin `known`) solo añade las declaradas en addedSinceLegacy', async () => {
      vi.mocked(uiPreferencesClient.get).mockResolvedValue({
        scope: 'tramites.columns',
        value: { visible: ['a', 'c'] },
      });
      const { result } = renderHook(() =>
        useUiPreferences('tramites.columns', ['a', 'b', 'c', 'd'], {
          catalog: ['a', 'b', 'c', 'd'],
          addedSinceLegacy: ['d'],
        }),
      );
      await waitFor(() => expect(result.current.status).toBe('ready'));
      // 'd' entra; 'b' NO, aunque esté en el default: pudo haberla ocultado el usuario.
      expect(result.current.visible).toEqual(['a', 'c', 'd']);
    });

    it('al guardar deja constancia del catálogo vigente en `known`', async () => {
      vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.columns', value: {} });
      vi.mocked(uiPreferencesClient.put).mockResolvedValue({
        scope: 'tramites.columns',
        value: {},
      });
      const { result } = renderHook(() =>
        useUiPreferences('tramites.columns', ['a'], { catalog: ['a', 'b', 'c'] }),
      );
      await waitFor(() => expect(result.current.status).toBe('ready'));
      act(() => result.current.setVisible(['a', 'b']));
      await waitFor(() =>
        expect(uiPreferencesClient.put).toHaveBeenCalledWith('tramites.columns', {
          visible: ['a', 'b'],
          known: ['a', 'b', 'c'],
        }),
      );
    });
  });

  it('sin preferencia guardada (value: {}) conserva las columnas por defecto', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.columns', value: {} });
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));
    expect(result.current.visible).toEqual(DEFAULTS);
  });

  it('degrada con elegancia: si el GET falla, se queda con las columnas por defecto (nunca en blanco)', async () => {
    vi.mocked(uiPreferencesClient.get).mockRejectedValue(new Error('network down'));
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('error'));
    expect(result.current.visible).toEqual(DEFAULTS);
  });

  it('setVisible guarda de forma optimista y revierte si falla el PUT', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.columns', value: {} });
    vi.mocked(uiPreferencesClient.put).mockRejectedValue(new Error('boom'));
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));

    act(() => result.current.setVisible(['a']));
    expect(result.current.visible).toEqual(['a']);

    await waitFor(() => expect(result.current.saving).toBe(false));
    expect(result.current.visible).toEqual(DEFAULTS);
  });

  it('setVisible guarda y se mantiene si el PUT tiene éxito', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.columns', value: {} });
    vi.mocked(uiPreferencesClient.put).mockResolvedValue({
      scope: 'tramites.columns',
      value: { visible: ['b'] },
    });
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));

    act(() => result.current.setVisible(['b']));
    await waitFor(() => expect(result.current.saving).toBe(false));
    expect(result.current.visible).toEqual(['b']);
    expect(uiPreferencesClient.put).toHaveBeenCalledWith('tramites.columns', { visible: ['b'] });
  });

  it('ignora un intento de dejar cero columnas visibles', async () => {
    vi.mocked(uiPreferencesClient.get).mockResolvedValue({ scope: 'tramites.columns', value: {} });
    const { result } = renderHook(() => useUiPreferences('tramites.columns', DEFAULTS));
    await waitFor(() => expect(result.current.status).toBe('ready'));

    act(() => result.current.setVisible([]));
    expect(result.current.visible).toEqual(DEFAULTS);
    expect(uiPreferencesClient.put).not.toHaveBeenCalled();
  });
});
