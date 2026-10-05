import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api/types";
import { openPdfBlobInNewTab } from "@/lib/documents/open-document-tab";

// HU #13175b — el helper conserva el error original en `cause`.
describe("openPdfBlobInNewTab", () => {
  afterEach(() => vi.restoreAllMocks());

  it("si falla la obtención relanza document_preview_failed con el error original en cause y cierra con mensaje la pestaña", async () => {
    const win = { document: { open: vi.fn(), write: vi.fn(), close: vi.fn() }, closed: false, opener: null };
    vi.spyOn(window, "open").mockReturnValue(win as unknown as Window);
    const original = new ApiError(400, "plantilla_variable_invalida", { error: "plantilla_variable_invalida" });

    const err = await openPdfBlobInNewTab(() => Promise.reject(original)).catch((e: unknown) => e);

    expect(err).toBeInstanceOf(Error);
    expect((err as Error).message).toBe("document_preview_failed");
    expect((err as Error).cause).toBe(original);
    expect(win.document.write).toHaveBeenCalledTimes(2); // cargando + error
  });
});
