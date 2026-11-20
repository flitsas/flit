import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../errores";
import { useBloqueo } from "../useBloqueo";

describe("useBloqueo", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("deshabilita la acción durante el Retry-After y la libera al terminar", () => {
    const { result } = renderHook(() => useBloqueo(Date.now));
    expect(result.current.bloqueado).toBe(false);

    let bloqueo = false;
    act(() => {
      bloqueo = result.current.registrar(new ApiError("rate_limited", "x", 429, 3));
    });
    expect(bloqueo).toBe(true);
    expect(result.current.bloqueado).toBe(true);
    expect(result.current.segundosRestantes).toBe(3);

    act(() => void vi.advanceTimersByTime(2000));
    expect(result.current.segundosRestantes).toBe(1);

    act(() => void vi.advanceTimersByTime(1500));
    expect(result.current.bloqueado).toBe(false);
    expect(result.current.segundosRestantes).toBe(0);
  });

  it("no bloquea ante errores que no piden esperar", () => {
    const { result } = renderHook(() => useBloqueo(Date.now));

    let bloqueo = true;
    act(() => {
      bloqueo = result.current.registrar(new ApiError("captcha_required", "x", 400));
    });

    expect(bloqueo).toBe(false);
    expect(result.current.bloqueado).toBe(false);
  });
});
