// HU #10523 (RF31) — panel de parámetros documentales por gestora. API mockeada.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyDocumentParamsPanel } from "../CompanyDocumentParamsPanel";
import type { CompanyDocumentParam } from "@/lib/api/admin-company-document-params";

vi.mock("@/lib/api/admin-company-document-params", () => ({
  fetchCompanyDocumentParams: vi.fn(),
  upsertCompanyDocumentParam: vi.fn(),
}));

import {
  fetchCompanyDocumentParams,
  upsertCompanyDocumentParam,
} from "@/lib/api/admin-company-document-params";

const TENANT = "aaaaaaaa-0000-4000-8000-000000000001";

function param(code: string, state: CompanyDocumentParam["state"]): CompanyDocumentParam {
  return { id: `id-${code}`, documentTypeCode: code, state };
}

describe("CompanyDocumentParamsPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("lista los parámetros existentes", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([param("soat", "OBLIGATORIO")]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(await screen.findByText("soat")).toBeInTheDocument();
    expect(fetchCompanyDocumentParams).toHaveBeenCalledWith(TENANT, expect.anything());
  });

  it("muestra el empty state sin parámetros", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    expect(await screen.findByText(/se aplica el comportamiento base/i)).toBeInTheDocument();
  });

  it("cambia el estado de un documento (upsert)", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([param("soat", "OBLIGATORIO")]);
    vi.mocked(upsertCompanyDocumentParam).mockResolvedValue(param("soat", "OCULTO"));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    const select = await screen.findByLabelText("Estado de soat");
    await userEvent.selectOptions(select, "OCULTO");

    await waitFor(() =>
      expect(upsertCompanyDocumentParam).toHaveBeenCalledWith(TENANT, {
        documentTypeCode: "soat",
        state: "OCULTO",
      }),
    );
  });

  it("agrega un parámetro nuevo", async () => {
    vi.mocked(fetchCompanyDocumentParams).mockResolvedValue([]);
    vi.mocked(upsertCompanyDocumentParam).mockResolvedValue(param("cepd", "OPCIONAL"));
    render(<CompanyDocumentParamsPanel tenantId={TENANT} />);

    await screen.findByText(/se aplica el comportamiento base/i);
    await userEvent.type(screen.getByLabelText("Código de documento"), "cepd");
    await userEvent.selectOptions(screen.getByLabelText("Estado del nuevo parámetro"), "OPCIONAL");
    await userEvent.click(screen.getByRole("button", { name: "Guardar" }));

    await waitFor(() =>
      expect(upsertCompanyDocumentParam).toHaveBeenCalledWith(TENANT, {
        documentTypeCode: "cepd",
        state: "OPCIONAL",
      }),
    );
    expect(await screen.findByText("cepd")).toBeInTheDocument();
  });
});
