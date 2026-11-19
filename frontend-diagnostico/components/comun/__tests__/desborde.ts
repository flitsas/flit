import { vi } from "vitest";

// Uso de ejemplo: const d = simularDesborde({ ancho: 720 }); render(…); d.contenido.ancho = 300; d.redimensionar();
// jsdom no mide: los contenedores con `overflow-auto` / `overflow-x-auto` miden 300 × 256 px visibles y su contenido
// mide lo que diga `contenido`. El ResizeObserver se simula y `redimensionar()` dispara sus callbacks. Restaurar con
// vi.restoreAllMocks() + vi.unstubAllGlobals() en el afterEach.

const VISIBLE = { ancho: 300, alto: 256 };
const esDesplazable = (el: HTMLElement) => /(^|\s)overflow-(x-)?auto(\s|$)/.test(el.className);

export function simularDesborde(inicial: Partial<{ ancho: number; alto: number }> = {}) {
  const contenido = { ancho: inicial.ancho ?? VISIBLE.ancho, alto: inicial.alto ?? 0 };
  const medida = (visible: number, deContenido: () => number) =>
    function (this: HTMLElement) {
      return esDesplazable(this) ? (visible < 0 ? deContenido() : visible) : 0;
    };
  vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockImplementation(medida(VISIBLE.ancho, () => 0));
  vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockImplementation(medida(VISIBLE.alto, () => 0));
  vi.spyOn(HTMLElement.prototype, "scrollWidth", "get").mockImplementation(medida(-1, () => contenido.ancho));
  vi.spyOn(HTMLElement.prototype, "scrollHeight", "get").mockImplementation(medida(-1, () => contenido.alto));
  const callbacks: Array<() => void> = [];
  vi.stubGlobal(
    "ResizeObserver",
    class {
      constructor(cb: ResizeObserverCallback) {
        callbacks.push(() => cb([], this as unknown as ResizeObserver));
      }
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
  return { contenido, redimensionar: () => callbacks.forEach((c) => c()) };
}
