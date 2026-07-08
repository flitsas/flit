// HU #10626 — useCooldown: deshabilitado temporal genérico, reutilizado por
// ResendInvitationButton para el cooldown visual (AC1) y el cooldown real del backend (AC2).
import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useCooldown } from "../useCooldown";

describe("useCooldown", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("empieza inactivo, sin cooldown", () => {
    const { result } = renderHook(() => useCooldown());
    expect(result.current.active).toBe(false);
    expect(result.current.remainingSeconds).toBe(0);
  });

  it("start() activa el cooldown y cuenta hacia atrás cada segundo", () => {
    const { result } = renderHook(() => useCooldown());

    act(() => result.current.start(3));
    expect(result.current.active).toBe(true);
    expect(result.current.remainingSeconds).toBe(3);

    act(() => vi.advanceTimersByTime(1000));
    expect(result.current.remainingSeconds).toBe(2);

    act(() => vi.advanceTimersByTime(1000));
    expect(result.current.remainingSeconds).toBe(1);

    act(() => vi.advanceTimersByTime(1000));
    expect(result.current.remainingSeconds).toBe(0);
    expect(result.current.active).toBe(false);
  });

  it("start() llamado de nuevo mientras está activo REEMPLAZA el cooldown (no lo suma)", () => {
    const { result } = renderHook(() => useCooldown());

    act(() => result.current.start(10));
    expect(result.current.remainingSeconds).toBe(10);

    // Llega el 429 real del backend con un cooldown mayor al optimista del cliente.
    act(() => result.current.start(90));
    expect(result.current.remainingSeconds).toBe(90);

    act(() => vi.advanceTimersByTime(1000));
    expect(result.current.remainingSeconds).toBe(89);
  });

  it("clear() cancela el cooldown inmediatamente", () => {
    const { result } = renderHook(() => useCooldown());

    act(() => result.current.start(60));
    expect(result.current.active).toBe(true);

    act(() => result.current.clear());
    expect(result.current.active).toBe(false);
    expect(result.current.remainingSeconds).toBe(0);
  });

  it("start(0) o negativo no activa cooldown", () => {
    const { result } = renderHook(() => useCooldown());

    act(() => result.current.start(0));
    expect(result.current.active).toBe(false);

    act(() => result.current.start(-5));
    expect(result.current.active).toBe(false);
  });
});
