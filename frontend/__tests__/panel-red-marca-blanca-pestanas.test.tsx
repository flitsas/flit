// Panel de red de la propia cabeza Marca Blanca: igual que la ficha del SuperAdmin, clientes hijos e
// identidad de marca (con el dominio) van en pestañas, no apilados. Otra cabeza conserva solo la tabla.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import NetworkChildrenPage from "@/app/admin/companies/[tenantId]/children/page";
import { fetchCompany } from "@/lib/api/admin-companies";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useParams: () => ({ tenantId: "mb-1" }),
}));
vi.mock("@/hooks/usePermissions", () => ({
  usePermissions: () => ({ isSuperAdmin: false, isGroupParent: true, isAdminCompany: true, tenantId: "mb-1" }),
}));
vi.mock("@/lib/api/admin-companies", () => ({ fetchCompany: vi.fn() }));
vi.mock("@/components/admin/companies/NetworkChildrenPanel", () => ({
  NetworkChildrenPanel: () => <p>Tabla de clientes hijos</p>,
}));
vi.mock("@/components/admin/branding/BrandingConfigurator", () => ({
  BrandingConfigurator: () => <p>Configurador de marca</p>,
}));
vi.mock("@/components/admin/domain/DomainStatusPanel", () => ({
  DomainStatusPanel: () => <p>Estado del dominio</p>,
}));

const cabeza = (tenantType: string) =>
  ({ id: "mb-1", tenantType, isGroupParent: true, razonSocial: "Red", nit: "900" }) as never;

describe("Panel de red — cabeza Marca Blanca", () => {
  beforeEach(() => vi.mocked(fetchCompany).mockReset());

  it("separa «Clientes hijos» e «Identidad de marca» en pestañas, cada una solo con lo suyo", async () => {
    vi.mocked(fetchCompany).mockResolvedValue(cabeza("MARCA_BLANCA"));
    const user = userEvent.setup();
    render(<NetworkChildrenPage />);

    const hijos = await screen.findByRole("tab", { name: "Clientes hijos" });
    const marca = screen.getByRole("tab", { name: "Identidad de marca" });
    expect(hijos).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Tabla de clientes hijos")).toBeVisible();
    expect(screen.getByText("Configurador de marca")).not.toBeVisible();

    await user.click(marca);
    expect(marca).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Configurador de marca")).toBeVisible();
    expect(screen.getByText("Estado del dominio")).toBeVisible();
    expect(screen.getByText("Tabla de clientes hijos")).not.toBeVisible();
  });

  it("otra cabeza de red conserva solo la tabla, sin pestañas ni marca", async () => {
    vi.mocked(fetchCompany).mockResolvedValue(cabeza("CONCESION"));
    render(<NetworkChildrenPage />);

    expect(await screen.findByText("Tabla de clientes hijos")).toBeInTheDocument();
    expect(screen.queryByRole("tab")).not.toBeInTheDocument();
    expect(screen.queryByText("Configurador de marca")).not.toBeInTheDocument();
  });
});
