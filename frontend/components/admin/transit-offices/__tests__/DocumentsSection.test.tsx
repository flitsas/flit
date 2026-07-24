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

// HU #10887 — el toggle dedicado que también expone esta sección (dependencias del hijo).
vi.mock("@/lib/api/admin-procedure-documents", () => ({
  fetchProcedureDocumentRequirements: vi.fn().mockResolvedValue([]),
}));
vi.mock("@/lib/api/admin-document-requirement-overrides", () => ({
  fetchDocumentRequirementOverrides: vi.fn().mockResolvedValue([]),
  setDocumentRequirementOverride: vi.fn(),
}));

import {
  createOtDocumentTag,
  fetchOtDocumentPrecedence,
  fetchOtDocumentTags,
} from "@/lib/api/admin-ot";
import { fetchProcedureDocumentRequirements } from "@/lib/api/admin-procedure-documents";
import {
  fetchDocumentRequirementOverrides,
  setDocumentRequirementOverride,
} from "@/lib/api/admin-document-requirement-overrides";
import type { ProcedureDocumentRequirement } from "@/lib/api/types-documents";

const OT_ID = "ot-hub-1";

function renderSection() {
  return render(
    <ToastProvider>
      <DocumentsSection transitOfficeId={OT_ID} />
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

// HU #10887 — el toggle "Documento de prenda obligatorio" se expone también en el detalle
// de la OT (pestaña Prelación), reutilizando PledgeDocumentOverrideToggle con el
// transitOfficeId del hub y el tipo de trámite seleccionado en esta misma sección.
describe("DocumentsSection — HU #10887 (toggle documento de prenda en el hub OT)", () => {
  const pledgeRequirement: ProcedureDocumentRequirement = {
    id: "req-prenda",
    procedureTypeId: "pt-1",
    documentTypeId: "doc-prenda",
    ordenDefault: 10,
    obligatorio: false,
    documento: {
      codigo: "inscripcion_prenda",
      nombre: "Inscripción / Registro de Prenda",
      estado: "activo",
    },
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtDocumentPrecedence).mockResolvedValue({
      data: [{ document_type_id: "doc-1", document_name: "SOAT", sort_order: 1 }],
    });
    vi.mocked(fetchOtDocumentTags).mockResolvedValue({ data: [] });
  });

  it("muestra el toggle con el transitOfficeId del hub y el trámite seleccionado, y al activarlo persiste", async () => {
    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([pledgeRequirement]);
    vi.mocked(fetchDocumentRequirementOverrides).mockResolvedValue([]);
    vi.mocked(setDocumentRequirementOverride).mockResolvedValue(undefined);

    const user = userEvent.setup();
    renderSection();

    expect(
      await screen.findByText("Documento de prenda por Organismo de Tránsito"),
    ).toBeInTheDocument();
    expect(fetchProcedureDocumentRequirements).toHaveBeenCalledWith("pt-1", expect.anything());
    expect(fetchDocumentRequirementOverrides).toHaveBeenCalledWith(
      "pt-1",
      OT_ID,
      expect.anything(),
    );

    const toggle = await screen.findByRole("switch", { name: "Documento de prenda obligatorio" });
    expect(toggle).toHaveAttribute("aria-checked", "false");

    await user.click(toggle);

    await waitFor(() =>
      expect(setDocumentRequirementOverride).toHaveBeenCalledWith({
        procedureTypeId: "pt-1",
        documentTypeId: "doc-prenda",
        transitOfficeId: OT_ID,
        estado: "REQUIRED",
      }),
    );
    expect(toggle).toHaveAttribute("aria-checked", "true");
  });
});
