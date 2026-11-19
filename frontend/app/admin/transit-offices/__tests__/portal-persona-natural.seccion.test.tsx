// Página de Portal Persona Natural: sin fila de pestañas; la sección sale de `?tab=` (la elige el submenú del menú de abajo).
// La persona de caja siempre ve Caja con Cobrar y Cobros del día, y no monta nada de administración.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

let query = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useParams: () => ({ id: "ot-1" }),
  usePathname: () => "/admin/transit-offices/ot-1/portal-persona-natural",
  useSearchParams: () => new URLSearchParams(query),
}));
vi.mock("@/components/admin/transit-offices/OtHubLayout", () => ({ OtHubLayout: ({ children }: { children: React.ReactNode }) => <div>{children}</div> }));
vi.mock("@/components/admin/transit-offices/portal-pn/OtReportesSection", () => ({ OtReportesSection: () => <p>Sección Reportes</p> }));
vi.mock("@/components/admin/transit-offices/portal-pn/OtCobroTab", () => ({ OtCobroTab: () => <p>Sección Cobro</p> }));
vi.mock("@/components/admin/transit-offices/portal-pn/PortalPnDesignSection", () => ({ PortalPnDesignSection: () => <p>Sección Diseño</p> }));
vi.mock("@/components/admin/transit-offices/portal-pn/OtPortalDomainSection", () => ({ OtPortalDomainSection: () => null }));
vi.mock("@/components/admin/transit-offices/portal-pn/OtCajaUsuariosSection", () => ({ OtCajaUsuariosSection: () => <p>Usuarios de caja (administración)</p> }));
vi.mock("@/components/admin/transit-offices/portal-pn/OtCajaCobrosSection", () => ({ OtCajaCobrosSection: () => <p>Cobros de caja (administración)</p> }));

import Page from "../[id]/portal-persona-natural/page";

function token(roles: string[]): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${b64({ alg: "none" })}.${b64({ tenant_id: "ot-1", roles: roles.map((code) => ({ id: code, code })) })}.`;
}
const conRol = (...roles: string[]) => window.localStorage.setItem(TOKEN_STORAGE_KEY, token(roles));

beforeEach(() => {
  query = "";
});
afterEach(() => window.localStorage.clear());

describe("Portal Persona Natural — sección por ruta", () => {
  it("el Admin OT no ve la fila de pestañas y abre Reportes por defecto", async () => {
    conRol("ot_admin");
    render(<Page />);
    expect(await screen.findByText("Sección Reportes")).toBeInTheDocument();
    expect(screen.queryByRole("tablist", { name: "Secciones del portal Persona Natural" })).not.toBeInTheDocument();
  });

  it.each([
    ["cobro", "Sección Cobro"],
    ["diseno", "Sección Diseño"],
  ])("?tab=%s abre directo esa sección", async (tab, texto) => {
    conRol("ot_admin");
    query = `tab=${tab}`;
    render(<Page />);
    expect(await screen.findByText(texto)).toBeInTheDocument();
    expect(screen.queryByText("Sección Reportes")).not.toBeInTheDocument();
  });

  it("?tab=caja: el Admin OT ve Cobrar, Cobros del día y Usuarios de caja", async () => {
    conRol("ot_admin");
    query = "tab=caja";
    render(<Page />);
    const tabs = await screen.findAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual(["Cobrar", "Cobros del día", "Usuarios de caja"]);
  });

  it("la persona de caja va siempre a Caja (aunque pida otra sección) y solo ve Cobrar y Cobros del día", async () => {
    conRol("ot_caja");
    query = "tab=diseno";
    render(<Page />);
    const tabs = await screen.findAllByRole("tab");
    expect(tabs.map((t) => t.textContent)).toEqual(["Cobrar", "Cobros del día"]);
    expect(screen.queryByText("Sección Diseño")).not.toBeInTheDocument();
    expect(screen.queryByText("Sección Reportes")).not.toBeInTheDocument();
    expect(screen.queryByText(/\(administración\)/)).not.toBeInTheDocument();
  });

  it("otro rol no entra", async () => {
    conRol("gestor_ot");
    render(<Page />);
    expect(await screen.findByTestId("portal-pn-sin-acceso")).toBeInTheDocument();
  });
});
