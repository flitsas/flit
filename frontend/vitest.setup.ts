// Setup global de pruebas (HU #10194): matchers de jest-dom y limpieza del DOM
// entre tests para aislar cada caso.
import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(() => {
  cleanup();
});
