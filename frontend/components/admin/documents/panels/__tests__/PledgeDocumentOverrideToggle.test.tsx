// HU #10887 — Toggle dedicado "Documento de prenda obligatorio" (Admin OT).
// AC1: activar/desactivar persiste el override (REQUIRED/DEFAULT) del documento
// `inscripcion_prenda` vía el endpoint granular de obligatoriedad por OT (HU #10198).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { PledgeDocumentOverrideToggle } from "../PledgeDocumentOverrideToggle";
import type {
  DocumentRequirementOverride,
  ProcedureDocumentRequirement,
} from "@/lib/api/types-documents";

vi.mock("@/lib/api/admin-procedure-documents", () => ({
  fetchProcedureDocumentRequirements: vi.fn(),
}));
vi.mock("@/lib/api/admin-document-requirement-overrides", () => ({
  fetchDocumentRequirementOverrides: vi.fn(),
  setDocumentRequirementOverride: vi.fn(),
}));

import { fetchProcedureDocumentRequirements } from "@/lib/api/admin-procedure-documents";
import {
  fetchDocumentRequirementOverrides,
  setDocumentRequirementOverride,
} from "@/lib/api/admin-document-requirement-overrides";

const PROC = "33333333-3333-3333-3333-333333333333";
const OT = "aaaaaaaa-0001-4000-8000-000000000001";
const PLEDGE_DOC_ID = "doc-prenda";

const pledgeRequirement: ProcedureDocumentRequirement = {
  id: "req-prenda",
  procedureTypeId: PROC,
  documentTypeId: PLEDGE_DOC_ID,
  ordenDefault: 10,
  obligatorio: false,
  documento: {
    codigo: "inscripcion_prenda",
    nombre: "Inscripción / Registro de Prenda",
    estado: "activo",
  },
};

function renderToggle() {
  return render(
    <ToastProvider>
      <PledgeDocumentOverrideToggle procedureTypeId={PROC} transitOfficeId={OT} />
    </ToastProvider>,
  );
}

describe("PledgeDocumentOverrideToggle — HU #10887", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC1 — activar el switch persiste el override REQUIRED del documento de prenda", async () => {
    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([pledgeRequirement]);
    vi.mocked(fetchDocumentRequirementOverrides).mockResolvedValue([]);
    vi.mocked(setDocumentRequirementOverride).mockResolvedValue(undefined);

    const user = userEvent.setup();
    renderToggle();

    const toggle = await screen.findByRole("switch", { name: "Documento de prenda obligatorio" });
    expect(toggle).toHaveAttribute("aria-checked", "false");

    await user.click(toggle);

    await waitFor(() =>
      expect(setDocumentRequirementOverride).toHaveBeenCalledWith({
        procedureTypeId: PROC,
        documentTypeId: PLEDGE_DOC_ID,
        transitOfficeId: OT,
        estado: "REQUIRED",
      }),
    );
    expect(toggle).toHaveAttribute("aria-checked", "true");
  });

  it("desactivar el switch limpia el override (estado=DEFAULT)", async () => {
    const activeOverride: DocumentRequirementOverride = {
      id: "ov-1",
      procedureTypeId: PROC,
      documentTypeId: PLEDGE_DOC_ID,
      transitOfficeId: OT,
      estado: "REQUIRED",
      documento: { codigo: "inscripcion_prenda", nombre: "Inscripción / Registro de Prenda" },
    };
    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([pledgeRequirement]);
    vi.mocked(fetchDocumentRequirementOverrides).mockResolvedValue([activeOverride]);
    vi.mocked(setDocumentRequirementOverride).mockResolvedValue(undefined);

    const user = userEvent.setup();
    renderToggle();

    // Refleja el valor vigente al cargar.
    const toggle = await screen.findByRole("switch", { name: "Documento de prenda obligatorio" });
    await waitFor(() => expect(toggle).toHaveAttribute("aria-checked", "true"));

    await user.click(toggle);

    await waitFor(() =>
      expect(setDocumentRequirementOverride).toHaveBeenCalledWith({
        procedureTypeId: PROC,
        documentTypeId: PLEDGE_DOC_ID,
        transitOfficeId: OT,
        estado: "DEFAULT",
      }),
    );
    expect(toggle).toHaveAttribute("aria-checked", "false");
  });

  it("muestra el estado de carga mientras resuelve los catálogos", () => {
    vi.mocked(fetchProcedureDocumentRequirements).mockReturnValue(new Promise(() => {}));
    vi.mocked(fetchDocumentRequirementOverrides).mockReturnValue(new Promise(() => {}));

    renderToggle();

    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("muestra el estado de error si falla la carga y permite reintentar", async () => {
    vi.mocked(fetchProcedureDocumentRequirements).mockRejectedValueOnce(new Error("boom"));
    vi.mocked(fetchDocumentRequirementOverrides).mockResolvedValue([]);

    renderToggle();

    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();

    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([pledgeRequirement]);
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Reintentar/i }));

    expect(
      await screen.findByRole("switch", { name: "Documento de prenda obligatorio" }),
    ).toBeInTheDocument();
  });

  it("estado vacío cuando el trámite no tiene asociado el documento de prenda", async () => {
    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([]);
    vi.mocked(fetchDocumentRequirementOverrides).mockResolvedValue([]);

    renderToggle();

    expect(
      await screen.findByText(/no tiene asociado el documento de inscripción\/registro de prenda/i),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("switch", { name: "Documento de prenda obligatorio" }),
    ).not.toBeInTheDocument();
  });

  it("revierte el estado optimista y muestra error si falla la persistencia", async () => {
    vi.mocked(fetchProcedureDocumentRequirements).mockResolvedValue([pledgeRequirement]);
    vi.mocked(fetchDocumentRequirementOverrides).mockResolvedValue([]);
    vi.mocked(setDocumentRequirementOverride).mockRejectedValueOnce(new Error("boom"));

    const user = userEvent.setup();
    renderToggle();

    const toggle = await screen.findByRole("switch", { name: "Documento de prenda obligatorio" });
    await user.click(toggle);

    await waitFor(() => expect(toggle).toHaveAttribute("aria-checked", "false"));
  });
});
