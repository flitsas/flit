import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EnabledProceduresPanel } from "../EnabledProceduresPanel";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";

// FEATURE-08 — pestaña "Trámites habilitados": lista tipos publicados y habilita/deshabilita por
// compañía. Se moquean el cliente superadmin, el cliente de grants y el toast.

const api = vi.hoisted(() => ({
  listProcedureTypes: vi.fn(),
  fetchProcedureGrants: vi.fn(),
  addProcedureGrant: vi.fn(),
  removeProcedureGrant: vi.fn(),
  show: vi.fn(),
}));

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: { listProcedureTypes: () => api.listProcedureTypes() },
}));
vi.mock("@/lib/api/admin-companies", () => ({
  fetchProcedureGrants: (tenantId: string) => api.fetchProcedureGrants(tenantId),
  addProcedureGrant: (tenantId: string, id: string) => api.addProcedureGrant(tenantId, id),
  removeProcedureGrant: (tenantId: string, id: string) => api.removeProcedureGrant(tenantId, id),
}));
vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show: api.show }),
}));

const published = (over: Partial<ProcedureTypeSummary>): ProcedureTypeSummary => ({
  id: "id",
  code: "CODE",
  name: "Name",
  family: "TRASPASO",
  publicationStatus: "published",
  isActive: true,
  publishedAt: null,
  ...over,
});

describe("EnabledProceduresPanel", () => {
  beforeEach(() => {
    api.listProcedureTypes.mockReset();
    api.fetchProcedureGrants.mockReset();
    api.addProcedureGrant.mockReset().mockResolvedValue(undefined);
    api.removeProcedureGrant.mockReset().mockResolvedValue(undefined);
    api.show.mockReset();
  });

  it("lista solo los tipos publicados con su estado de habilitación", async () => {
    api.listProcedureTypes.mockResolvedValue([
      published({ id: "t1", code: "TRASPASO_SIMPLE", name: "Traspaso Simple" }),
      published({ id: "t2", code: "MATRICULA_INICIAL", name: "Matrícula Inicial" }),
      published({ id: "d1", code: "BORRADOR", name: "Borrador", publicationStatus: "draft" }),
    ]);
    api.fetchProcedureGrants.mockResolvedValue({ procedureTypeIds: ["t1"] });

    render(<EnabledProceduresPanel tenantId="ten-1" />);

    const t1 = await screen.findByRole("switch", { name: /traspaso simple/i });
    const t2 = await screen.findByRole("switch", { name: /matrícula inicial/i });
    expect(t1).toBeChecked(); // habilitado (grant t1)
    expect(t2).not.toBeChecked(); // no habilitado
    // El borrador NO se lista.
    expect(screen.queryByRole("switch", { name: /borrador/i })).not.toBeInTheDocument();
  });

  it("al habilitar un tipo llama addProcedureGrant", async () => {
    const user = userEvent.setup();
    api.listProcedureTypes.mockResolvedValue([published({ id: "t2", code: "MATRICULA_INICIAL", name: "Matrícula Inicial" })]);
    api.fetchProcedureGrants.mockResolvedValue({ procedureTypeIds: [] });

    render(<EnabledProceduresPanel tenantId="ten-1" />);
    const sw = await screen.findByRole("switch", { name: /matrícula inicial/i });
    await user.click(sw);

    await waitFor(() => expect(api.addProcedureGrant).toHaveBeenCalledWith("ten-1", "t2"));
  });

  it("muestra vacío cuando no hay tipos publicados", async () => {
    api.listProcedureTypes.mockResolvedValue([published({ publicationStatus: "draft" })]);
    api.fetchProcedureGrants.mockResolvedValue({ procedureTypeIds: [] });

    render(<EnabledProceduresPanel tenantId="ten-1" />);
    expect(await screen.findByText(/no hay tipos de trámite publicados/i)).toBeInTheDocument();
  });
});
