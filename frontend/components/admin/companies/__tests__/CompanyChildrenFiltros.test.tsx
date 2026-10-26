/** HU #13999 — filtros por columna en Compañías hijas y Panel de red (modo cliente). */
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NetworkChildrenPanel } from "../NetworkChildrenPanel";
import { CompanyChildrenSection } from "../CompanyChildrenSection";
import type { CompanyChildListItem, CompanyListItem } from "@/lib/api/types";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompanyChildren: vi.fn(),
  createChildCompany: vi.fn(),
  inviteChildCompanyUser: vi.fn(),
}));
import { fetchCompanyChildren } from "@/lib/api/admin-companies";

const hijo = (over: Partial<CompanyChildListItem>): CompanyChildListItem => ({
  id: "x",
  nit: "900000000-0",
  razonSocial: "X",
  code: "COMP-0000",
  tenantType: "RENTING",
  estadoActivo: true,
  fechaVinculacion: "2026-01-15T10:00:00Z",
  rowVersion: 1,
  ...over,
});

const HIJOS = [
  hijo({ id: "a", razonSocial: "Alfa Ltda", nit: "900111111-1", tenantType: "RENTING" }),
  hijo({
    id: "b",
    razonSocial: "Beta SAS",
    nit: "900222222-2",
    tenantType: "CONCESIONARIO",
    estadoActivo: false,
    fechaVinculacion: "2026-03-15T10:00:00Z",
  }),
  hijo({ id: "c", razonSocial: "Gamma SAS", nit: "900333333-3", tenantType: "CONCESIONARIO" }),
];

const HEAD = {
  id: "head",
  nit: "1",
  razonSocial: "Cabeza",
  code: "COMP-0100",
  tenantType: "CONCESION",
  isTransitOffice: false,
  isGroupParent: true,
  estadoActivo: true,
  fechaCreacion: "2026-01-01T00:00:00Z",
  rowVersion: 1,
} satisfies CompanyListItem;

const casos = [
  {
    nombre: "Panel de red",
    texto: "Cliente",
    tabla: "Red de clientes",
    montar: () => render(<NetworkChildrenPanel headTenantId="head" headTenantType="CONCESION" />),
  },
  {
    nombre: "Compañías hijas",
    texto: "Razón social",
    tabla: "Clientes hijos",
    montar: () => render(<CompanyChildrenSection company={HEAD} />),
  },
];

describe.each(casos)("$nombre — filtros por columna (HU #13999)", ({ texto, tabla, montar }) => {
  beforeEach(() => {
    vi.mocked(fetchCompanyChildren).mockReset();
    vi.mocked(fetchCompanyChildren).mockResolvedValue(HIJOS);
  });

  const nombres = () =>
    within(screen.getByRole("table", { name: tabla }))
      .getAllByRole("row")
      .slice(1)
      .map((r) => within(r).getAllByRole("cell")[0].textContent);

  it("AC2: Razón social/Cliente (texto) y NIT filtran con AND", async () => {
    const user = userEvent.setup();
    montar();
    await screen.findByRole("table", { name: tabla });

    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: texto }));
    await user.type(screen.getByLabelText(new RegExp(`${texto} contiene`, "i")), "sas");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(nombres()).toEqual(["Beta SAS", "Gamma SAS"]);

    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "NIT" }));
    await user.type(screen.getByLabelText(/nit contiene/i), "3333");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(nombres()).toEqual(["Gamma SAS"]);
  });

  it("AC2: Tipo y Estado (selección) filtran", async () => {
    const user = userEvent.setup();
    montar();
    await screen.findByRole("table", { name: tabla });

    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Tipo" }));
    await user.click(screen.getByRole("checkbox", { name: "Cliente concesión" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(nombres()).toEqual(["Beta SAS", "Gamma SAS"]);

    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "Estado" }));
    await user.click(screen.getByRole("checkbox", { name: "Activo" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(nombres()).toEqual(["Gamma SAS"]);
  });

  it("AC2: Vinculación (Periodo) filtra por fecha", async () => {
    const user = userEvent.setup();
    montar();
    await screen.findByRole("table", { name: tabla });

    await user.click(screen.getByRole("button", { name: /periodo/i }));
    await user.selectOptions(screen.getByLabelText("Periodo"), "Rango propio");
    await user.type(screen.getByLabelText("Fecha inicial del rango propio"), "2026-03-01");
    await user.type(screen.getByLabelText("Fecha final del rango propio"), "2026-03-31");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(nombres()).toEqual(["Beta SAS"]);
  });

  it("AC3: sin resultados muestra «Limpiar filtros» y al limpiar vuelve la lista completa", async () => {
    const user = userEvent.setup();
    montar();
    await screen.findByRole("table", { name: tabla });

    await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
    await user.click(screen.getByRole("button", { name: "NIT" }));
    await user.type(screen.getByLabelText(/nit contiene/i), "zzz");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));

    await user.click(await screen.findByRole("button", { name: "Limpiar filtros" }));
    expect(nombres()).toEqual(["Alfa Ltda", "Beta SAS", "Gamma SAS"]);
  });
});
