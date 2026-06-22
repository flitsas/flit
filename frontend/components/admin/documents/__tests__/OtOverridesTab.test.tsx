// HU #10198 — Pestaña Overrides OT: al borrar el override de ORDEN de un documento,
// también se limpia su OBLIGATORIEDAD por OT (ambas personalizaciones del doc para ese
// OT se eliminan juntas). API mockeada.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtOverridesTab } from "../tabs/OtOverridesTab";
import type {
  DocumentOrderOverride,
  DocumentRequirementOverride,
  ProcedureDocumentRequirement,
} from "@/lib/api/types-documents";
import type { TransitOffice } from "@/lib/api/types";

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
}));
vi.mock("@/lib/api/admin-procedure-documents", () => ({
  fetchProcedureDocumentRequirements: vi.fn(),
}));
vi.mock("@/lib/api/admin-document-overrides", () => ({
  fetchDocumentOrderOverrides: vi.fn(),
  createDocumentOrderOverride: vi.fn(),
  removeDocumentOrderOverride: vi.fn(),
  updateDocumentOrderOverride: vi.fn(),
}));
vi.mock("@/lib/api/admin-document-requirement-overrides", () => ({
  fetchDocumentRequirementOverrides: vi.fn(),
  setDocumentRequirementOverride: vi.fn(),
}));

import { fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchProcedureDocumentRequirements } from "@/lib/api/admin-procedure-documents";
import {
  fetchDocumentOrderOverrides,
  removeDocumentOrderOverride,
} from "@/lib/api/admin-document-overrides";
import {
  fetchDocumentRequirementOverrides,
  setDocumentRequirementOverride,
} from "@/lib/api/admin-document-requirement-overrides";

const PROC = "33333333-3333-3333-3333-333333333333";
const OT = "aaaaaaaa-0001-4000-8000-000000000001";
const DOC = "doc-a";

const office = { id: OT, name: "Secretaría Bogotá", code: "11001" } as unknown as TransitOffice;

const requirement: ProcedureDocumentRequirement = {
  id: "req-a",
  procedureTypeId: PROC,
  documentTypeId: DOC,
  ordenDefault: 10,
  obligatorio: false,
  documento: { codigo: "DOCA", nombre: "Documento A", estado: "activo" },
};

const orderOverride: DocumentOrderOverride = {
  id: "ord-a",
  procedureTypeId: PROC,
  documentTypeId: DOC,
  scope: "OT",
  scopeRefId: OT,
  orden: 70,
  documento: { codigo: "DOCA", nombre: "Documento A" },
};

const requirementOverride: DocumentRequirementOverride = {
  id: "rq-a",
  procedureTypeId: PROC,
  documentTypeId: DOC,
  transitOfficeId: OT,
  estado: "REQUIRED",
  documento: { codigo: "DOCA", nombre: "Documento A" },
};

function renderTab() {
  return render(
    <ToastProvider>
      <OtOverridesTab procedureTypeId={PROC} />
    </ToastProvider>,
  );
}

describe("OtOverridesTab — limpieza acoplada orden ↔ obligatoriedad", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTransitOffices).mockResolvedValue([office]);
    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([requirement]);
    vi.mocked(fetchDocumentOrderOverrides).mockResolvedValue([orderOverride]);
    vi.mocked(fetchDocumentRequirementOverrides).mockResolvedValue([requirementOverride]);
    vi.mocked(removeDocumentOrderOverride).mockResolvedValue(undefined);
    vi.mocked(setDocumentRequirementOverride).mockResolvedValue(undefined);
  });

  it("al borrar el override de orden, limpia (DEFAULT) la obligatoriedad del mismo doc+OT", async () => {
    const user = userEvent.setup();
    renderTab();

    // Selecciona el OT → cargan overrides de orden y obligatoriedad.
    await user.selectOptions(await screen.findByLabelText("Organismo de Tránsito"), OT);

    // La obligatoriedad arranca en «Obligatorio» (REQUIRED).
    const reqSelect = (await screen.findByLabelText(
      "Obligatoriedad de Documento A",
    )) as HTMLSelectElement;
    await waitFor(() => expect(reqSelect.value).toBe("REQUIRED"));

    // Borra el override de orden.
    await user.click(
      await screen.findByRole("button", { name: /eliminar override de Documento A/i }),
    );

    // Se limpia la obligatoriedad por OT del mismo documento.
    await waitFor(() =>
      expect(setDocumentRequirementOverride).toHaveBeenCalledWith({
        procedureTypeId: PROC,
        documentTypeId: DOC,
        transitOfficeId: OT,
        estado: "DEFAULT",
      }),
    );
    expect(removeDocumentOrderOverride).toHaveBeenCalledWith("ord-a");
    // El documento desaparece de «Obligatoriedad por OT» (membresía acoplada al orden).
    await waitFor(() =>
      expect(screen.queryByLabelText("Obligatoriedad de Documento A")).not.toBeInTheDocument(),
    );
  });

  it("no llama a la obligatoriedad si el doc no tenía override de obligatoriedad", async () => {
    vi.mocked(fetchDocumentRequirementOverrides).mockResolvedValue([]);
    const user = userEvent.setup();
    renderTab();

    await user.selectOptions(await screen.findByLabelText("Organismo de Tránsito"), OT);
    await user.click(
      await screen.findByRole("button", { name: /eliminar override de Documento A/i }),
    );

    await waitFor(() => expect(removeDocumentOrderOverride).toHaveBeenCalledWith("ord-a"));
    expect(setDocumentRequirementOverride).not.toHaveBeenCalled();
  });
});
