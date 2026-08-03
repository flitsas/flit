import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { createRef } from "react";
import { useDockScrollCondense } from "../useDockScrollCondense";

describe("useDockScrollCondense", () => {
  let now = 0;

  beforeEach(() => {
    now = 0;
    vi.spyOn(performance, "now").mockImplementation(() => now);
    vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
      cb(now);
      return 1;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("arranca expandido y condensa al bajar el scroll del contenedor", () => {
    const el = document.createElement("div");
    Object.defineProperty(el, "scrollTop", { writable: true, value: 0 });
    const scrollRef = createRef<HTMLElement | null>();
    (scrollRef as { current: HTMLElement | null }).current = el;

    const { result } = renderHook(() => useDockScrollCondense(scrollRef));
    expect(result.current).toBe(false);

    act(() => {
      now = 100;
      el.scrollTop = 200;
      el.dispatchEvent(new Event("scroll"));
    });
    expect(result.current).toBe(true);
  });

  it("vuelve a expandir cerca del inicio (EXPAND_ZONE)", () => {
    const el = document.createElement("div");
    Object.defineProperty(el, "scrollTop", { writable: true, value: 0 });
    const scrollRef = createRef<HTMLElement | null>();
    (scrollRef as { current: HTMLElement | null }).current = el;

    const { result } = renderHook(() => useDockScrollCondense(scrollRef));

    act(() => {
      now = 100;
      el.scrollTop = 200;
      el.dispatchEvent(new Event("scroll"));
    });
    expect(result.current).toBe(true);

    act(() => {
      now = 1000; // > lockout 250 ms
      el.scrollTop = 40;
      el.dispatchEvent(new Event("scroll"));
    });
    expect(result.current).toBe(false);
  });
});
