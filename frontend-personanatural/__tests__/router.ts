import { vi } from "vitest";

/** Espía del router de Next que usa la landing (vitest.setup.ts lo conecta a `next/navigation`). */
export const routerPush = vi.fn();
