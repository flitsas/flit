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

// Toggle prenda opcional por compañía (hub OT).
vi.mock("@/lib/api/admin-ot-prenda-document-policies", () => ({
  fetchOtPrendaDocumentPoliciesForOffice: vi.fn().mockResolvedValue([]),
  setOtPrendaDocumentPolicyForOffice: vi.fn(),
}));

import {
  createOtDocumentTag,
  fetchOtDocumentPrecedence,
  fetchOtDocumentTags,
  updateOtDocumentPrecedence,
} from "@/lib/api/admin-ot";
import {
  fetchOtPrendaDocumentPoliciesForOffice,
  setOtPrendaDocumentPolicyForOffice,
} from "@/lib/api/admin-ot-prenda-document-policies";

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

// HU #11185 — la pantalla de prelación pasa a ser operativa: lista completa del tipo de trámite,
// reordenamiento con teclado que guarda, aviso de aplicación diferida y rollback si falla.
describe("DocumentsSection — HU #11185 (prelación operativa)", () => {
  const listaCompleta = [
    {
      document_type_id: "doc-fur",
      document_code: "fur",
      document_name: "Formulario Único de Registro (FUR)",
      sort_order: 1,
      is_system_generated: true,
      is_configured: false,
    },
    {
      document_type_id: "doc-soat",
      document_code: "soat",
      document_name: "SOAT",
      sort_order: 2,
      is_system_generated: false,
      is_configured: false,
    },
  ];

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtDocumentPrecedence).mockResolvedValue({ data: listaCompleta });
    vi.mocked(fetchOtDocumentTags).mockResolvedValue({ data: [] });
  });

  it("AC1 lista todos los documentos que aplican, marcando los que genera el sistema", async () => {
    renderSection();

    expect(await screen.findByText("Formulario Único de Registro (FUR)")).toBeInTheDocument();
    expect(screen.getByText("SOAT")).toBeInTheDocument();
    // El FUR lo produce FLIT; el SOAT lo adjunta el gestor.
    expect(screen.getAllByText("Generado")).toHaveLength(1);
  });

  it("AC3 y AC4 reordenar con teclado guarda y avisa de que aplica en la próxima generación", async () => {
    vi.mocked(updateOtDocumentPrecedence).mockResolvedValue({
      data: [
        { ...listaCompleta[1], sort_order: 1 },
        { ...listaCompleta[0], sort_order: 2 },
      ],
    });
    const user = userEvent.setup();
    renderSection();

    const handle = await screen.findByLabelText(/Reordenar Formulario Único de Registro/i);
    handle.focus();
    // La primera flecha toma el documento (patrón WCAG de la lista); la segunda lo baja.
    await user.keyboard("{ArrowDown}");
    await user.keyboard("{ArrowDown}");
    await user.keyboard("{Enter}");

    await waitFor(() =>
      expect(updateOtDocumentPrecedence).toHaveBeenCalledWith({
        procedure_type_id: "pt-1",
        items: [
          { document_type_id: "doc-soat", sort_order: 1 },
          { document_type_id: "doc-fur", sort_order: 2 },
        ],
      }),
    );
    expect(
      await screen.findByText(/Orden guardado\. Aplica a partir de la próxima generación/i),
    ).toBeInTheDocument();
  });

  it("AC5 si falla el guardado avisa y la lista vuelve al orden anterior", async () => {
    vi.mocked(updateOtDocumentPrecedence).mockRejectedValue(new Error("500"));
    const user = userEvent.setup();
    renderSection();

    const handle = await screen.findByLabelText(/Reordenar Formulario Único de Registro/i);
    handle.focus();
    // La primera flecha toma el documento (patrón WCAG de la lista); la segunda lo baja.
    await user.keyboard("{ArrowDown}");
    await user.keyboard("{ArrowDown}");
    await user.keyboard("{Enter}");

    expect(await screen.findByText(/No se pudo guardar el orden/i)).toBeInTheDocument();
    await waitFor(() => {
      const nombres = screen
        .getAllByRole("listitem")
        .map((li) => li.textContent ?? "");
      expect(nombres[0]).toContain("Formulario Único de Registro (FUR)");
      expect(nombres[1]).toContain("SOAT");
    });
  });
});

// Documento de prenda opcional por compañía en el hub OT.
describe("DocumentsSection — prenda opcional por compañía", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtDocumentPrecedence).mockResolvedValue({
      data: [{ document_type_id: "doc-1", document_name: "SOAT", sort_order: 1 }],
    });
    vi.mocked(fetchOtDocumentTags).mockResolvedValue({ data: [] });
  });

  it("lista compañías del OT y permite activar prenda opcional", async () => {
    vi.mocked(fetchOtPrendaDocumentPoliciesForOffice).mockResolvedValue([
      { tenantId: "t1", tenantName: "Gestora Uno", documentOptional: false },
    ]);
    vi.mocked(setOtPrendaDocumentPolicyForOffice).mockResolvedValue(undefined);

    const user = userEvent.setup();
    renderSection();

    expect(await screen.findByText("Documento de prenda por compañía")).toBeInTheDocument();
    expect(fetchOtPrendaDocumentPoliciesForOffice).toHaveBeenCalledWith(OT_ID, expect.anything());

    const toggle = await screen.findByRole("switch", { name: /gestora uno — prenda opcional/i });
    await user.click(toggle);

    await waitFor(() =>
      expect(setOtPrendaDocumentPolicyForOffice).toHaveBeenCalledWith(OT_ID, "t1", true),
    );
  });
});
