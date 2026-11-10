import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { AnchorHTMLAttributes, ReactNode } from "react";
import { BarChart3, FileStack, Landmark, Lock, Network } from "lucide-react";
import { buildDock, type NavCatalog, type NavViewer } from "@flit/shell/nav";
import { ShellNavProvider } from "@flit/shell/nav-context";

const mocks = vi.hoisted(() => ({ searchEstadoCounts: vi.fn(), push: vi.fn(), search: "" }));
vi.mock("@/lib/api/tramites-client", () => ({ tramitesClient: { searchEstadoCounts: mocks.searchEstadoCounts } }));
vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(mocks.search), useRouter: () => ({ push: mocks.push }) }));
vi.mock("next/link", () => ({
  default: ({ href, children, ...rest }: AnchorHTMLAttributes<HTMLAnchorElement> & { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { InicioModulos, enCurso } from "../InicioModulos";
import { ModulosAcordeon } from "../ModulosAcordeon";

const catalog: NavCatalog = {
  sections: [
    { id: "tramites", label: "Trámites", icon: FileStack, side: "left" },
    { id: "reportes", label: "Reportes", icon: BarChart3, side: "left" },
    { id: "administradores", label: "Administradores", icon: Lock, side: "right" },
    { id: "integraciones", label: "Integraciones", icon: Network, side: "right" },
  ],
  items: [
    { key: "tramites", label: "Trámites", href: "/tramites", section: "tramites", icon: FileStack },
    { key: "reportes", label: "Reportes", href: "/?m=reportes", section: "reportes", icon: BarChart3, permission: "reportes.ver" },
    {
      key: "admin-transit",
      label: "Compañías y tránsito",
      href: "",
      section: "administradores",
      icon: Landmark,
      superAdminOnly: true,
      children: [{ key: "admin-companies", label: "Compañías", href: "/admin/companies", section: "administradores", icon: Landmark, superAdminOnly: true }],
    },
    { key: "log-qx", label: "Log QX", href: "/?m=log-qx", section: "integraciones", icon: Network },
  ],
};

const gestor: NavViewer = { permissions: ["reportes.ver"], isSuperAdmin: false };
const superAdmin: NavViewer = { permissions: [], isSuperAdmin: true };

function renderInicio(viewer: NavViewer, ui: ReactNode = <InicioModulos />) {
  const groups = buildDock(catalog, viewer, "/");
  return render(<ShellNavProvider value={{ groups, homeHref: "/" }}>{ui}</ShellNavProvider>);
}

const fila = (nombre: string) => screen.getByRole("button", { name: new RegExp(nombre) });

beforeEach(() => {
  mocks.search = "";
  mocks.searchEstadoCounts.mockReset();
  window.history.replaceState(null, "", "/?m=dashboard");
});
afterEach(cleanup);

describe("Inicio: acordeón de módulos (HU #14481 AC1)", () => {
  it("muestra solo los grupos y módulos que el rol ve en el dock", async () => {
    mocks.searchEstadoCounts.mockResolvedValue({});
    renderInicio(gestor);
    const region = screen.getByRole("region", { name: "Módulos" });
    expect(within(region).getAllByRole("heading", { level: 3 }).map((h) => h.textContent)).toEqual([
      expect.stringContaining("Trámites"),
      expect.stringContaining("Reportes"),
      expect.stringContaining("Integraciones"),
    ]);
    expect(within(region).queryByText("Administradores")).toBeNull();
    await waitFor(() => expect(mocks.searchEstadoCounts).toHaveBeenCalled());
  });

  it("abre un solo grupo a la vez y deja el abierto en ?grupo= sin perder los demás parámetros", () => {
    mocks.searchEstadoCounts.mockResolvedValue({});
    renderInicio(superAdmin);

    expect(fila("Reportes")).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(fila("Reportes"));
    expect(fila("Reportes")).toHaveAttribute("aria-expanded", "true");
    expect(document.getElementById(fila("Reportes").getAttribute("aria-controls")!)).toBeVisible();
    expect(new URLSearchParams(window.location.search).get("grupo")).toBe("reportes");
    expect(new URLSearchParams(window.location.search).get("m")).toBe("dashboard");

    fireEvent.click(fila("Administradores"));
    expect(fila("Administradores")).toHaveAttribute("aria-expanded", "true");
    expect(fila("Reportes")).toHaveAttribute("aria-expanded", "false");
    expect(document.getElementById(fila("Reportes").getAttribute("aria-controls")!)).not.toBeVisible();
    expect(screen.getAllByRole("button", { expanded: true })).toHaveLength(1);
    expect(new URLSearchParams(window.location.search).get("grupo")).toBe("administradores");

    // Un contenedor del dock aporta sus hijos, con su nombre como subtítulo.
    const companias = screen.getByRole("link", { name: /Compañías/ });
    expect(companias).toHaveAttribute("href", "/admin/companies");
    expect(companias).toHaveTextContent("Compañías y tránsito");

    fireEvent.click(fila("Administradores"));
    expect(screen.queryAllByRole("button", { expanded: true })).toHaveLength(0);
    expect(new URLSearchParams(window.location.search).has("grupo")).toBe(false);
  });

  it("al entrar con ?grupo= ese grupo ya está abierto; uno inexistente no abre nada", () => {
    mocks.searchEstadoCounts.mockResolvedValue({});
    mocks.search = "m=dashboard&grupo=integraciones";
    renderInicio(gestor, <ModulosAcordeon />);
    expect(fila("Integraciones")).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: /Log QX/ })).toHaveAttribute("href", "/?m=log-qx");
    cleanup();

    mocks.search = "grupo=no-existe";
    renderInicio(gestor, <ModulosAcordeon />);
    expect(screen.queryAllByRole("button", { expanded: true })).toHaveLength(0);
  });
});

describe("Inicio: cifras que filtran (HU #14481 AC2)", () => {
  it("cada cifra sale de estado-counts y lleva a /tramites filtrado por su estado", async () => {
    mocks.searchEstadoCounts.mockResolvedValue({ borrador: 4, entregado: 2, aprobado: 7, subsanacion: 1 });
    renderInicio(gestor);

    const cifras = screen.getByRole("region", { name: "Tus trámites por estado" });
    const borrador = await within(cifras).findByRole("link", { name: "Borrador: 4 trámites" });
    expect(borrador).toHaveAttribute("href", "/tramites?estado=borrador");
    expect(within(cifras).getByRole("link", { name: "En subsanación: 1 trámite" })).toHaveAttribute("href", "/tramites?estado=subsanacion");
    // Un estado que el servidor no trae cuenta como cero, porque la respuesta sí llegó.
    expect(within(cifras).getByRole("link", { name: "Revocado: 0 trámites" })).toHaveAttribute("href", "/tramites?estado=revocado");
    expect(mocks.searchEstadoCounts).toHaveBeenCalledTimes(1);
    expect(mocks.searchEstadoCounts).toHaveBeenCalledWith({});
  });

  it("un clic simple en la tarjeta navega dentro de la app; con Ctrl lo deja al navegador", async () => {
    mocks.push.mockReset();
    mocks.searchEstadoCounts.mockResolvedValue({ subsanacion: 1 });
    renderInicio(gestor);

    const tarjeta = await screen.findByRole("link", { name: "En subsanación: 1 trámite" });
    fireEvent.click(tarjeta);
    expect(mocks.push).toHaveBeenCalledWith("/tramites?estado=subsanacion");
    fireEvent.click(tarjeta, { ctrlKey: true });
    expect(mocks.push).toHaveBeenCalledTimes(1);
  });

  it("sin respuesta del servidor no se inventan ceros", async () => {
    mocks.searchEstadoCounts.mockResolvedValue({});
    renderInicio(gestor);
    expect(await screen.findByText("Por ahora no hay cifras para mostrar.")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /: \d+ trámites?$/ })).toBeNull();
  });

  it("quien no ve Trámites en el dock no ve cifras ni dispara la consulta", () => {
    const sinTramites: NavCatalog = { ...catalog, items: catalog.items.filter((i) => i.key !== "tramites") };
    const groups = buildDock(sinTramites, gestor, "/");
    render(
      <ShellNavProvider value={{ groups, homeHref: "/" }}>
        <InicioModulos />
      </ShellNavProvider>,
    );
    expect(screen.queryByRole("region", { name: "Tus trámites por estado" })).toBeNull();
    expect(mocks.searchEstadoCounts).not.toHaveBeenCalled();
  });
});

describe("Inicio: contador solo donde hay endpoint (HU #14481 AC3)", () => {
  it("el grupo de Trámites muestra los que siguen en curso; los demás grupos, ningún contador", async () => {
    mocks.searchEstadoCounts.mockResolvedValue({ borrador: 4, entregado: 2, aprobado: 7, anulado: 3, subsanacion: 1, rechazado_preasignacion: 1 });
    renderInicio(gestor);

    await waitFor(() => expect(fila("Trámites")).toHaveTextContent("7 en curso"));
    expect(fila("Reportes")).not.toHaveTextContent(/en curso|pendiente/);
    expect(fila("Integraciones")).not.toHaveTextContent(/en curso|pendiente/);
  });

  it("en curso = todos menos los finales, sin sumar dos veces el pseudo-estado de rechazo", () => {
    expect(enCurso({ borrador: 1, rechazado: 2, rechazado_preasignacion: 2, aprobado: 5, anulado: 1, revocado: 1 })).toBe(3);
  });

  it("sin respuesta del servidor, tampoco el grupo de Trámites muestra contador", async () => {
    mocks.searchEstadoCounts.mockResolvedValue({});
    renderInicio(gestor);
    await screen.findByText("Por ahora no hay cifras para mostrar.");
    expect(fila("Trámites")).not.toHaveTextContent(/en curso/);
  });
});
