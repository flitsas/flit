// HU #13918 — paginación estándar de servidor en el historial de transiciones de IA:
// «Filas por página» con 10/20/30/50 y el cambio de tamaño pide la página 1 con el nuevo pageSize.
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AiTransitionsTable } from "../AiTransitionsTable";

const listAiTransitions = vi.fn();

vi.mock("@/lib/api/admin-plataforma-ai", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-plataforma-ai")>(
    "@/lib/api/admin-plataforma-ai",
  );
  return { ...actual, listAiTransitions: (...a: unknown[]) => listAiTransitions(...a) };
});

function pagina(page: number, pageSize: number, totalCount: number) {
  const desde = (page - 1) * pageSize;
  const n = Math.max(0, Math.min(pageSize, totalCount - desde));
  return {
    page,
    pageSize,
    totalCount,
    items: Array.from({ length: n }, (_, i) => ({
      id: `t-${desde + i}`,
      toolCode: "ocr",
      fromProvider: null,
      toProvider: "claude",
      reason: "manual",
      detail: `detalle ${desde + i}`,
      actorUserId: null,
      occurredAt: "2026-09-01T15:00:00Z",
    })),
  };
}

describe("AiTransitionsTable — paginación estándar (HU #13918)", () => {
  beforeEach(() => {
    sessionStorage.clear();
    listAiTransitions.mockReset().mockImplementation((_tool: string, page: number, size: number) =>
      Promise.resolve(pagina(page, size, 45)),
    );
  });

  it("abre en 10 filas por página y ofrece 10/20/30/50", async () => {
    render(<AiTransitionsTable toolCode="ocr" toolLabel="OCR" />);
    const selector = await screen.findByRole("combobox", { name: /filas por página/i });
    expect(selector).toHaveValue("10");
    expect(within(selector).getAllByRole("option").map((o) => o.textContent)).toEqual(["10", "20", "30", "50"]);
    expect(listAiTransitions).toHaveBeenLastCalledWith("ocr", 1, 10, expect.anything());
  });

  it("al cambiar el tamaño vuelve a la página 1 y pide el nuevo pageSize al servidor", async () => {
    render(<AiTransitionsTable toolCode="ocr" toolLabel="OCR" />);
    await screen.findByRole("combobox", { name: /filas por página/i });
    fireEvent.click(screen.getByRole("button", { name: /página siguiente|siguiente/i }));
    await waitFor(() => expect(listAiTransitions).toHaveBeenLastCalledWith("ocr", 2, 10, expect.anything()));

    fireEvent.change(await screen.findByRole("combobox", { name: /filas por página/i }), { target: { value: "30" } });
    await waitFor(() => expect(listAiTransitions).toHaveBeenLastCalledWith("ocr", 1, 30, expect.anything()));
    expect(await screen.findByText(/Mostrando 1–30 de 45/)).toBeInTheDocument();
  });
});
