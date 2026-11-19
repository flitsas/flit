// Pestaña «Caja» de Portal Persona Natural: qué ve cada rol (Admin OT, SuperAdmin, persona de caja) y que la persona de
// caja nunca monta lo que llama a APIs de administración.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { CajaCobro, OtCajaApi } from "@/lib/api/ot-caja";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useParams: () => ({ id: "ot-1" }),
  usePathname: () => "/admin/transit-offices/ot-1/portal-persona-natural",
  useSearchParams: () => new URLSearchParams(),
}));
vi.mock("../OtCajaUsuariosSection", () => ({ OtCajaUsuariosSection: () => <p>Usuarios de caja (administración)</p> }));
vi.mock("../OtCajaCobrosSection", () => ({ OtCajaCobrosSection: () => <p>Cobros de caja (administración)</p> }));
vi.mock("../PortalPnDesignSection", () => ({ PortalPnDesignSection: () => <p>Diseño del portal</p> }));

import { OtCajaTab, rolDeCaja, subDesdeQuery, subHaciaQuery } from "../OtCajaTab";
import { PortalPnTabs } from "../PortalPnTabs";

const COBRO: CajaCobro = {
  cobroId: "c1",
  codigo: "PN-2026-000001",
  valorCop: 100000,
  medio: "efectivo",
  pagadoEn: "2026-11-10T15:30:00Z",
  pagadoPorNombre: "Ana Caja",
  estado: "pagado",
  anulable: true,
  rowVersion: 1,
};

const api = (): OtCajaApi => ({
  tramite: vi.fn(),
  confirmar: vi.fn(),
  cobros: vi.fn().mockResolvedValue({ items: [COBRO], total: 1, totalDiaCop: 100000 }),
  anular: vi.fn(),
});

function token(roles: string[]): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${b64({ alg: "none" })}.${b64({ tenant_id: "t-1", roles: roles.map((code) => ({ id: code, code })) })}.`;
}
const conRol = (...roles: string[]) => window.localStorage.setItem(TOKEN_STORAGE_KEY, token(roles));
const nombres = (lista: HTMLElement) => within(lista).getAllByRole("tab").map((t) => t.textContent);

afterEach(() => window.localStorage.clear());

describe("OtCajaTab — subpestañas por rol", () => {
  it("Admin OT: Cobrar, Cobros del día y Usuarios de caja; abre en Cobrar", () => {
    render(<OtCajaTab transitOfficeId="ot-1" rol="admin" api={api()} />);
    expect(nombres(screen.getByRole("tablist", { name: "Secciones de la caja del portal" }))).toEqual([
      "Cobrar",
      "Cobros del día",
      "Usuarios de caja",
    ]);
    expect(screen.getByRole("tab", { name: "Cobrar" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByLabelText("ID del trámite")).toBeInTheDocument();
  });

  it("Admin OT: en «Cobros del día» puede anular y elegir el día", async () => {
    render(<OtCajaTab transitOfficeId="ot-1" rol="admin" api={api()} />);
    await userEvent.click(screen.getByRole("tab", { name: "Cobros del día" }));
    expect(await screen.findByRole("button", { name: "Acciones del cobro PN-2026-000001" })).toBeInTheDocument();
    expect(document.querySelector('input[type="date"]')).not.toBeNull();
  });

  it("SuperAdmin: Usuarios de caja y Cobros de caja (administración); no hay Cobrar ni Cobros del día", () => {
    const cliente = api();
    render(<OtCajaTab transitOfficeId="ot-1" rol="superadmin" api={cliente} />);
    expect(nombres(screen.getByRole("tablist", { name: "Secciones de la caja del portal" }))).toEqual([
      "Usuarios de caja",
      "Cobros de caja",
    ]);
    expect(screen.queryByRole("tab", { name: "Cobrar" })).not.toBeInTheDocument();
    expect(cliente.cobros).not.toHaveBeenCalled();
  });

  it("persona de caja: solo Cobrar y Cobros del día, sin Anular, sin selector de día y sin secciones de administración", async () => {
    const cliente = api();
    render(<OtCajaTab transitOfficeId="ot-1" rol="cajero" api={cliente} />);
    expect(nombres(screen.getByRole("tablist", { name: "Secciones de la caja del portal" }))).toEqual(["Cobrar", "Cobros del día"]);
    await userEvent.click(screen.getByRole("tab", { name: "Cobros del día" }));
    expect(await screen.findByText("PN-2026-000001")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Acciones del cobro/ })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="date"]')).toBeNull();
    expect(screen.queryByText(/\(administración\)/)).not.toBeInTheDocument();
  });

  it("el rol sale del token: ot_caja como único rol es «cajero», ot_admin es «admin», SuperAdmin es «superadmin»", () => {
    conRol("ot_caja");
    expect(rolDeCaja()).toBe("cajero");
    conRol("ot_admin");
    expect(rolDeCaja()).toBe("admin");
    conRol("SuperAdmin");
    expect(rolDeCaja()).toBe("superadmin");
  });

  it("?sub= se traduce según el rol: «usuarios» no existe para la persona de caja y «cobrar» no para el SuperAdmin", () => {
    expect(subDesdeQuery("cobrar", "admin")).toBe("cobrar");
    expect(subDesdeQuery("usuarios", "cajero")).toBeUndefined();
    expect(subDesdeQuery("cobrar", "superadmin")).toBeUndefined();
    expect(subDesdeQuery("cobros", "superadmin")).toBe("cobros-admin");
    expect(subDesdeQuery("cobros", "cajero")).toBe("cobros");
    expect(subHaciaQuery("cobros-admin")).toBe("cobros");
  });

  it("avisa la subpestaña elegida y respeta la inicial", async () => {
    const onSubChange = vi.fn();
    render(<OtCajaTab transitOfficeId="ot-1" rol="admin" api={api()} initialSub="usuarios" onSubChange={onSubChange} />);
    expect(screen.getByRole("tab", { name: "Usuarios de caja" })).toHaveAttribute("aria-selected", "true");
    await userEvent.click(screen.getByRole("tab", { name: "Cobrar" }));
    expect(onSubChange).toHaveBeenCalledWith("cobrar");
  });
});

describe("PortalPnTabs — pestañas por rol", () => {
  it("Admin OT: ve las 4 pestañas (Reportes, Cobro, Caja, Diseño)", () => {
    render(<PortalPnTabs transitOfficeId="ot-1" caja={<OtCajaTab transitOfficeId="ot-1" rol="admin" api={api()} />} />);
    expect(nombres(screen.getByRole("tablist", { name: "Secciones del portal Persona Natural" }))).toEqual([
      "Reportes",
      "Cobro",
      "Caja",
      "Diseño",
    ]);
  });

  it("con «seccion» muestra solo esa sección, sin la fila de pestañas", () => {
    render(
      <PortalPnTabs transitOfficeId="ot-1" seccion="caja" reportes={<p>Reportes aquí</p>} caja={<OtCajaTab transitOfficeId="ot-1" rol="cajero" api={api()} />} />,
    );
    expect(screen.queryByRole("tablist", { name: "Secciones del portal Persona Natural" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Caja" })).toBeInTheDocument();
    expect(screen.queryByText("Reportes aquí")).not.toBeInTheDocument();
    expect(screen.queryByText("Diseño del portal")).not.toBeInTheDocument();
  });

  it.each([
    ["reportes", "Reportes aquí"],
    ["cobro", "Cobro aquí"],
    ["diseno", "Diseño del portal"],
  ] as const)("seccion=%s abre directo esa sección", (seccion, texto) => {
    render(<PortalPnTabs transitOfficeId="ot-1" seccion={seccion} reportes={<p>Reportes aquí</p>} cobro={<p>Cobro aquí</p>} />);
    expect(screen.getByText(texto)).toBeInTheDocument();
  });
});
