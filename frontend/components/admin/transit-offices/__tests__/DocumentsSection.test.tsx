// HU #10224 — Prelación documental DnD y CRUD etiquetas.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { DocumentsSection } from "../DocumentsSection";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtDocumentPrecedence: vi.fn(),
  updateOtDocumentPrecedence: vi.fn(),
  fetchOtDocumentTags: vi.fn(),
  createOtDocumentTag: vi.fn(),
  deleteOtDocumentTag: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([
      { id: "pt-1", name: "Matrícula inicial", code: "MAT" },
    ]),
  },
}));

import {
  createOtDocumentTag,
  fetchOtDocumentPrecedence,
  fetchOtDocumentTags,
} from "@/lib/api/admin-ot";

function renderSection() {
  return render(
    <ToastProvider>
      <DocumentsSection />
    </ToastProvider>,
  );
}

describe("DocumentsSection — HU #10224", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtDocumentPrecedence).mockResolvedValue({
      data: [
        {
          document_type_id: "doc-1",
          document_name: "SOAT",
          sort_order: 1,
        },
      ],
    });
    vi.mocked(fetchOtDocumentTags).mockResolvedValue({ data: [] });
    vi.mocked(createOtDocumentTag).mockResolvedValue({
      id: "tag-1",
      code: "URGENTE",
      name: "Urgente",
      color: "#FF0000",
    });
  });

  it("AC1 muestra prelación con drag handle", async () => {
    renderSection();
    expect(await screen.findByText("SOAT")).toBeInTheDocument();
    expect(screen.getByLabelText(/Reordenar SOAT/i)).toBeInTheDocument();
  });

  it("AC4 crear etiqueta en pestaña Etiquetas", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("SOAT");
    await user.click(screen.getByRole("tab", { name: "Etiquetas" }));
    await user.click(await screen.findByRole("button", { name: /Nueva etiqueta/i }));
    await user.type(screen.getByLabelText("Código"), "URGENTE");
    await user.type(screen.getByLabelText("Nombre"), "Urgente");
    await user.click(screen.getByRole("button", { name: /^Guardar$/i }));
    await waitFor(() => expect(createOtDocumentTag).toHaveBeenCalled());
  });

  it("AC6 estado vacío etiquetas con CTA", async () => {
    const user = userEvent.setup();
    renderSection();
    await screen.findByText("SOAT");
    await user.click(screen.getByRole("tab", { name: "Etiquetas" }));
    expect(await screen.findByText(/No hay etiquetas configuradas/i)).toBeInTheDocument();
  });
});
