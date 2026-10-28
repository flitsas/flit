// Matchers de jest-dom, almacenamiento en memoria (jsdom no lo trae con origen about:blank), ResizeObserver y
// las gráficas y limpieza del DOM entre pruebas. Mismo arreglo que frontend/vitest.setup.ts.
import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, beforeEach, vi } from "vitest";
import { limpiarContexto } from "./lib/tramite/contexto";
import { routerPush } from "./__tests__/router";

// La landing navega con el router de Next, que no existe fuera de la app: se simula y las pruebas miran `routerPush`.
vi.mock("next/navigation", async () => {
  const { routerPush } = await import("./__tests__/router");
  return { useRouter: () => ({ push: routerPush, replace: routerPush, back: vi.fn(), prefetch: vi.fn(), refresh: vi.fn() }) };
});

class MemoryStorage implements Storage {
  private store = new Map<string, string>();
  get length(): number {
    return this.store.size;
  }
  key(index: number): string | null {
    return Array.from(this.store.keys())[index] ?? null;
  }
  getItem(key: string): string | null {
    return this.store.has(key) ? (this.store.get(key) as string) : null;
  }
  setItem(key: string, value: string): void {
    this.store.set(key, String(value));
  }
  removeItem(key: string): void {
    this.store.delete(key);
  }
  clear(): void {
    this.store.clear();
  }
}

const localStorageStub = new MemoryStorage();
Object.defineProperty(globalThis, "localStorage", { value: localStorageStub, configurable: true });
Object.defineProperty(globalThis, "sessionStorage", { value: new MemoryStorage(), configurable: true });

if (!("ResizeObserver" in globalThis)) {
  class ResizeObserverStub {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  }
  Object.defineProperty(globalThis, "ResizeObserver", { value: ResizeObserverStub, configurable: true });
}

beforeEach(() => {
  localStorageStub.clear();
});

afterEach(() => {
  cleanup();
  limpiarContexto();
  routerPush.mockClear();
});
