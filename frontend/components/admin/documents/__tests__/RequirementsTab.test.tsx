// AC2 — Tab de asociaciones: lista las asociaciones del trámite, muestra el empty
// state y agrega un documento (POST). API mockeada.
//
// Uso de ejemplo:
//   render(<ToastProvider><RequirementsTab procedureTypeId="…" /></ToastProvider>);
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { RequirementsTab } from "../tabs/RequirementsTab";
import type { DocumentTypePagedResult, ProcedureDocumentRequirement } from "@/lib/api/types-documents";

vi.mock("@/lib/api/admin-procedure-documents", () => ({
  fetchProcedureDocumentRequirements: vi.fn(),
  createProcedureDocumentRequirement: vi.fn(),
  updateProcedureDocumentRequirement: vi.fn(),
  removeProcedureDocumentRequirement: vi.fn(),
}));

vi.mock("@/lib/api/admin-document-types", () => ({
  fetchDocumentTypes: vi.fn(),
}));

import {
  createProcedureDocumentRequirement,
  fetchProcedureDocumentRequirements,
} from "@/lib/api/admin-procedure-documents";
import { fetchDocumentTypes } from "@/lib/api/admin-document-types";

const PROC = "33333333-3333-3333-3333-333333333333";

const catalog: DocumentTypePagedResult = {
  data: [
    { id: "doc-a", codigo: "DOCA", nombre: "Documento A", estado: "activo", fechaCreacion: "2026-01-01T00:00:00Z" },
    { id: "doc-b", codigo: "DOCB", nombre: "Documento B", estado: "activo", fechaCreacion: "2026-01-01T00:00:00Z" },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 100,
};

function existing(): ProcedureDocumentRequirement {
  return {
    id: "req-a",
    procedureTypeId: PROC,
    documentTypeId: "doc-a",
    ordenDefault: 10,
    obligatorio: true,
    documento: { codigo: "DOCA", nombre: "Documento A", estado: "activo" },
  };
}

function renderTab() {
  return render(
    <ToastProvider>
      <RequirementsTab procedureTypeId={PROC} />
    </ToastProvider>,
  );
}

describe("RequirementsTab (AC2)", () => {
  beforeEach(() => {
    vi.mocked(fetchDocumentTypes).mockResolvedValue(catalog);
  });

  it("lista las asociaciones del trámite", async () => {
    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([existing()]);
    renderTab();
    expect(await screen.findByText(/Documento A/)).toBeInTheDocument();
  });

  it("muestra el empty state cuando no hay asociaciones", async () => {
    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([]);
    renderTab();
    expect(await screen.findByText(/este trámite no tiene documentos asociados/i)).toBeInTheDocument();
  });

  it("agrega un documento al trámite vía POST", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([]);
    vi.mocked(createProcedureDocumentRequirement).mockResolvedValue({
      id: "req-b",
      procedureTypeId: PROC,
      documentTypeId: "doc-b",
      ordenDefault: 10,
      obligatorio: false,
      documento: { codigo: "DOCB", nombre: "Documento B", estado: "activo" },
    });

    renderTab();
    await screen.findByText(/este trámite no tiene documentos asociados/i);

    await user.selectOptions(screen.getByLabelText(/agregar documento al trámite/i), "doc-b");
    await user.click(screen.getByRole("button", { name: /^agregar$/i }));

    await waitFor(() =>
      expect(createProcedureDocumentRequirement).toHaveBeenCalledWith({
        procedureTypeId: PROC,
        documentTypeId: "doc-b",
        ordenDefault: 10,
        obligatorio: false,
      }),
    );
    expect(await screen.findByText("Documento B")).toBeInTheDocument();
  });
});
