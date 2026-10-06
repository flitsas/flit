// HU #13181b — al editar, las compañías asociadas se precargan con nombre y NIT del servidor.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

vi.mock("@/lib/api/admin-mandate-signers", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/admin-mandate-signers")>()),
  fetchOtAssociableCompanies: vi.fn().mockResolvedValue({
    items: [],
    total: 0,
    page: 1,
    pageSize: 10,
    aplicaSoloASuCompania: false,
  }),
  fetchCompanyAssociableCompanies: vi.fn(),
}));
vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVaultByDocument: vi.fn().mockResolvedValue([]),
  createSignatureVaultEntry: vi.fn(),
}));

import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { CompanyMandatarioForm } from "../CompanyMandatarioForm";
import { precargarAsociadas } from "../MandatarioCompaniasAsociadas";

const OFICINAS = [{ transitOfficeId: "ot-1", code: "05001000", name: "Tránsito de Medellín" }];
const onSubmit = vi.fn();

function renderEditando(officeCompanies: unknown) {
  const editing = {
    id: "ms-1",
    signerModel: "natural",
    fullName: "Ana Restrepo",
    documentType: "CC",
    documentNumber: "1020304050",
    email: "ana@ejemplo.com",
    signatureMethod: "biometria",
    transitOfficeIds: ["ot-1"],
    officeCompanies,
  } as unknown as MandateSigner;
  return render(
    <CompanyMandatarioForm
      variant="hub"
      offices={OFICINAS}
      editing={editing}
      restrictToOfficeIds={["ot-1"]}
      ownerCompanyIds={["t-propia"]}
      onCancel={vi.fn()}
      onSubmit={onSubmit}
    />,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  onSubmit.mockResolvedValue({ id: "ms-1", integrityHash: "h" });
});

describe("HU #13181b — precargado de asociadas", () => {
  it("muestra nombre y NIT sin depender de la búsqueda y guarda solo ids", async () => {
    const user = userEvent.setup();
    renderEditando([
      {
        transitOfficeId: "ot-1",
        associatedCompanyTenantIds: ["t-acme", "t-beta"],
        associatedCompanies: [
          { id: "t-acme", name: "ACME SAS", nit: "900111111" },
          { id: "t-beta", name: "BETA SAS", nit: "900222222" },
        ],
      },
    ]);
    const sel = await screen.findByRole("list", { name: "Compañías seleccionadas" });
    expect(within(sel).getByText(/ACME SAS · NIT 900111111/)).toBeInTheDocument();
    expect(within(sel).getByText(/BETA SAS · NIT 900222222/)).toBeInTheDocument();
    expect(within(sel).queryByText("Compañía asociada")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0].officeCompanies).toEqual([
      { transitOfficeId: "ot-1", associatedCompanyTenantIds: ["t-acme", "t-beta"] },
    ]);
  });

  it("usa el respaldo «Compañía asociada» solo para el id sin datos", async () => {
    renderEditando([
      {
        transitOfficeId: "ot-1",
        associatedCompanyTenantIds: ["t-acme", "t-x"],
        associatedCompanies: [{ id: "t-acme", name: "ACME SAS", nit: "900111111" }],
      },
    ]);
    const sel = await screen.findByRole("list", { name: "Compañías seleccionadas" });
    expect(within(sel).getByText(/ACME SAS · NIT 900111111/)).toBeInTheDocument();
    expect(within(sel).getAllByText("Compañía asociada")).toHaveLength(1);
  });

  it("precargarAsociadas acepta respuestas sin associatedCompanies", () => {
    expect(
      precargarAsociadas([{ transitOfficeId: "o", associatedCompanyTenantIds: ["a"] }]),
    ).toEqual({ a: { id: "a" } });
    expect(precargarAsociadas(undefined)).toEqual({});
  });
});
