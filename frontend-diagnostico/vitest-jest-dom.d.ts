// Matchers de jest-dom sobre el vitest de ESTA app. `@testing-library/jest-dom/vitest` aumenta el módulo "vitest" que
// resuelve desde su propia carpeta del store (el vitest izado de la raíz, otra versión): sin este archivo, tsc no ve
// toBeInTheDocument & co. en las aserciones del vitest que usa frontend-diagnostico.
import "vitest";
import type { TestingLibraryMatchers } from "@testing-library/jest-dom/matchers";

declare module "vitest" {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any, @typescript-eslint/no-empty-object-type
  interface Assertion<T = any> extends TestingLibraryMatchers<any, T> {}
  // eslint-disable-next-line @typescript-eslint/no-explicit-any, @typescript-eslint/no-empty-object-type
  interface AsymmetricMatchersContaining extends TestingLibraryMatchers<any, any> {}
}
