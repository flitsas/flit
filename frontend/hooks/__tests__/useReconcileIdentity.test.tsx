/**
 * «Actualizar estado» consultando a Kyverum Verify — lógica compartida entre el wizard
 * (`ReconcileAction`) y el detalle de persona del módulo Validaciones.
 *
 * Uso de ejemplo:
 *   const { submitting, message, run, runMany } = useReconcileIdentity();
 *   await run(() => tramitesClient.reconcileBiometric(instanceId, validationId));
 *   await runMany(['v-1', 'v-2'], (id) => tramitesClient.reconcileBiometricValidation(id));
 */
import { describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';

import {
  MAX_RECONCILIAR_POR_CLIC,
  MENSAJE_ERROR_RECONCILIAR,
  MENSAJE_NO_RESUELVE,
  useReconcileIdentity,
} from '@/hooks/useReconcileIdentity';
import type { ReconcileIdentityResult } from '@/lib/api/types/procedure-runtime';

describe('useReconcileIdentity — run (una validación, wizard)', () => {
  it('updated=true: sin mensaje y devuelve el resultado', async () => {
    const { result } = renderHook(() => useReconcileIdentity());
    let res: unknown;
    await act(async () => {
      res = await result.current.run(() => Promise.resolve({ status: 'aprobado', updated: true }));
    });
    expect(res).toEqual({ status: 'aprobado', updated: true });
    expect(result.current.message).toBeNull();
    expect(result.current.submitting).toBe(false);
  });

  it('updated=false: mensaje informativo «aún no resuelve»', async () => {
    const { result } = renderHook(() => useReconcileIdentity());
    await act(async () => {
      await result.current.run(() => Promise.resolve({ status: 'en_proceso', updated: false }));
    });
    expect(result.current.message).toEqual({ tone: 'info', text: MENSAJE_NO_RESUELVE });
  });

  it('error con mensaje: muestra el detail; sin Error: texto por defecto', async () => {
    const { result } = renderHook(() => useReconcileIdentity());
    let res: unknown = 'sin-tocar';
    await act(async () => {
      res = await result.current.run(() => Promise.reject(new Error('Proveedor no disponible.')));
    });
    expect(res).toBeNull();
    expect(result.current.message).toEqual({ tone: 'error', text: 'Proveedor no disponible.' });

    await act(async () => {
      await result.current.run(() => Promise.reject('boom'));
    });
    expect(result.current.message).toEqual({ tone: 'error', text: MENSAJE_ERROR_RECONCILIAR });
  });

  it('submitting=true mientras corre', async () => {
    const { result } = renderHook(() => useReconcileIdentity());
    let resolver: (v: ReconcileIdentityResult) => void = () => {};
    let pending: Promise<unknown> = Promise.resolve();
    act(() => {
      pending = result.current.run(() => new Promise((r) => { resolver = r; }));
    });
    expect(result.current.submitting).toBe(true);
    await act(async () => {
      resolver({ status: 'en_proceso', updated: false });
      await pending;
    });
    expect(result.current.submitting).toBe(false);
  });
});

describe('useReconcileIdentity — runMany (varias validaciones, detalle de persona)', () => {
  it('reconcilia en secuencia y agrega: alguna actualizada → «Estado actualizado.»', async () => {
    const orden: string[] = [];
    const fn = vi.fn(async (id: string): Promise<ReconcileIdentityResult> => {
      orden.push(id);
      return { status: id === 'b' ? 'aprobado' : 'enviado', updated: id === 'b' };
    });
    const { result } = renderHook(() => useReconcileIdentity());
    let resumen: unknown;
    await act(async () => {
      resumen = await result.current.runMany(['a', 'b'], fn);
    });
    expect(orden).toEqual(['a', 'b']);
    expect(resumen).toEqual({ updated: 1, pending: 1, failed: 0 });
    expect(result.current.message).toEqual({ tone: 'success', text: 'Estado actualizado.' });
  });

  it('ninguna cambió → «aún no resuelve»', async () => {
    const { result } = renderHook(() => useReconcileIdentity());
    await act(async () => {
      await result.current.runMany(['a'], async () => ({ status: 'enviado', updated: false }));
    });
    expect(result.current.message).toEqual({ tone: 'info', text: MENSAJE_NO_RESUELVE });
  });

  it('todas fallan → error del primero que falló', async () => {
    const { result } = renderHook(() => useReconcileIdentity());
    await act(async () => {
      await result.current.runMany(['a', 'b'], async (id) => {
        throw new Error(`falló ${id}`);
      });
    });
    expect(result.current.message).toEqual({ tone: 'error', text: 'falló a' });
  });

  it('mezcla éxito + fallo → prioriza el éxito y avisa que alguna no se pudo consultar', async () => {
    const { result } = renderHook(() => useReconcileIdentity());
    await act(async () => {
      await result.current.runMany(['a', 'b'], async (id) => {
        if (id === 'a') throw new Error('x');
        return { status: 'aprobado', updated: true };
      });
    });
    expect(result.current.message?.tone).toBe('success');
    expect(result.current.message?.text).toBe(
      'Estado actualizado. Alguna validación no se pudo consultar al proveedor.',
    );
  });

  it(`respeta el tope de ${MAX_RECONCILIAR_POR_CLIC} validaciones por clic`, async () => {
    const fn = vi.fn(async (): Promise<ReconcileIdentityResult> => ({ status: 'enviado', updated: false }));
    const { result } = renderHook(() => useReconcileIdentity());
    await act(async () => {
      await result.current.runMany(['1', '2', '3', '4', '5', '6', '7'], fn);
    });
    expect(MAX_RECONCILIAR_POR_CLIC).toBe(5);
    expect(fn).toHaveBeenCalledTimes(5);
  });
});
