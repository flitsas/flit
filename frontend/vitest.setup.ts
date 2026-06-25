// Setup global de pruebas (HU #10194): matchers de jest-dom y limpieza del DOM
// entre tests para aislar cada caso.
import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, beforeEach } from "vitest";

// jsdom (origen about:blank) NO expone localStorage/sessionStorage. Se provee un stub en
// memoria para los componentes que leen tema o sesión (p. ej. Shell). Se reinicia entre
// tests para aislarlos.
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

beforeEach(() => {
  localStorageStub.clear();
});

afterEach(() => {
  cleanup();
});
