// Barra de Trámites sobre @flit/shell (B-13, HU #12989): mismo catálogo y mismas reglas de acceso que el Shell
// anterior; ahora cada entrada es un enlace y la activa sale de la URL. Botón central de inicio, reparto por lado
// declarado, agrupadores con submenú y menú de cuenta.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { setDevSuperAdminToken } from "@/lib/api/client";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";

const nav = vi.hoisted(() => ({ pathname: "/" }));
vi.mock("next/navigation", () => ({
  usePathname: () => nav.pathname,
  useRouter: () => ({ push: vi.fn() }),
}));

// Sin `historial-placa`: con él, Trámites es un grupo de dos entradas (se prueba aparte) y no un enlace directo.
const ALL_SPA = ["tramites", "reportes", "reportes-detallados", "validaciones", "usuarios"];
const OT_OFFICE_KEY = "flit-ot-transit-office-id";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

function renderShell(visibleModuleCodes: string[] | undefined = ALL_SPA, search = "") {
  return render(
    <Shell visibleModuleCodes={visibleModuleCodes} search={search}>
      <div>contenido</div>
    </Shell>,
  );
}

const dock = () => screen.getByRole("navigation", { name: "Navegación principal" });

beforeEach(() => {
  nav.pathname = "/";
});

afterEach(() => {
  window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  window.sessionStorage.removeItem(OT_OFFICE_KEY);
});

describe("Shell — dock", () => {
  it("no muestra 'Ayuda' en el dock; la entrada vive en el menú de cuenta", async () => {
    renderShell(["reportes"]);
    expect(within(dock()).queryByRole("link", { name: "Ayuda" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Menú de cuenta" }));
    expect(screen.getByRole("link", { name: "Ayuda" })).toHaveAttribute("href", "/manual");
  });

  it("reparte el dock por lado declarado: Trámites a la izquierda del inicio, Usuarios a la derecha", () => {
    renderShell(["tramites", "usuarios", "reportes", "validaciones"]);
    const home = within(dock()).getByRole("link", { name: "Inicio Trámites" });
    const children = Array.from(home.parentElement!.children);
    const homeIndex = children.indexOf(home);
    const before = children.slice(0, homeIndex).map((el) => el.getAttribute("aria-label"));
    const after = children.slice(homeIndex + 1).map((el) => el.getAttribute("aria-label"));
    expect(before).toContain("Trámites");
    expect(after).toContain("Usuarios");
  });

  it("los módulos de la SPA llevan a /?m=… y Trámites a su ruta", () => {
    renderShell();
    expect(within(dock()).getByRole("link", { name: "Trámites" })).toHaveAttribute("href", "/tramites");
    expect(within(dock()).getByRole("link", { name: "Identidad" })).toHaveAttribute("href", "/?m=validaciones");
  });

  it("con Historial por placa, Trámites agrupa las dos entradas (HU #12194)", async () => {
    renderShell([...ALL_SPA, "historial-placa"]);
    await userEvent.click(within(dock()).getByRole("button", { name: "Trámites" }));
    expect(within(dock()).getByRole("link", { name: "Historial por placa" })).toHaveAttribute("href", "/?m=historial-placa");
  });

  it("marca el módulo de la URL (?m=) y no el inicio", () => {
    renderShell(["reportes", "tramites"], "m=reportes");
    const active = within(dock()).getByRole("link", { name: "Reportes" });
    expect(active).toHaveAttribute("aria-current", "page");
    // El activo no lleva degradado inline: lo pinta la clase del dock (HU #12723).
    expect(active.style.background).toBe("");
    expect(within(dock()).getByRole("link", { name: "Inicio Trámites" })).not.toHaveAttribute("aria-current");
  });

  it("en /tramites/… marca Trámites; en / sin módulo, el inicio", () => {
    nav.pathname = "/tramites/abc";
    const { unmount } = renderShell();
    expect(within(dock()).getByRole("link", { name: "Trámites" })).toHaveAttribute("aria-current", "page");
    unmount();

    nav.pathname = "/";
    renderShell();
    expect(within(dock()).getByRole("link", { name: "Inicio Trámites" })).toHaveAttribute("aria-current", "page");
  });

  it("mientras los módulos no cargan, los de la SPA no aparecen (deny-by-default)", () => {
    renderShell([]);
    expect(within(dock()).queryByRole("link", { name: "Trámites" })).not.toBeInTheDocument();
  });

  it("usa favicon.svg en el botón central", () => {
    renderShell(["reportes"]);
    const img = within(dock()).getByRole("link", { name: "Inicio Trámites" }).querySelector("img");
    expect(img?.getAttribute("src")).toBe("/assets/favicon.svg");
  });

  it("la barra común muestra el producto y su menú de productos", () => {
    renderShell();
    expect(screen.getByRole("button", { name: "Productos" })).toBeInTheDocument();
    expect(screen.getAllByText("Trámites").length).toBeGreaterThan(0);
  });
});

describe("Shell — usuario de organismo de tránsito", () => {
  beforeEach(() => {
    window.sessionStorage.setItem(OT_OFFICE_KEY, "ot-1");
  });

  it("ot_admin: pestañas del organismo en el dock, sin la administración de plataforma", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: "u1", role: "ot_admin", email: "ot@transito.gov.co" }));
    renderShell();

    const tramites = await within(dock()).findByRole("link", { name: "Trámites" });
    expect(tramites).toHaveAttribute("href", "/admin/transit-offices/ot-1/client-procedures");
    expect(within(dock()).getByRole("link", { name: "Usuarios" })).toHaveAttribute("href", "/admin/transit-offices/ot-1/usuarios");
    expect(within(dock()).getByRole("link", { name: "Reportes" })).toBeInTheDocument();
    for (const name of ["Tránsito", "Compañías", "Documental", "Administradores"]) {
      expect(within(dock()).queryByRole("button", { name })).not.toBeInTheDocument();
    }

    await userEvent.click(within(dock()).getByRole("button", { name: "Administración" }));
    expect(within(dock()).getByRole("link", { name: "Documentos" })).toBeInTheDocument();
    // HU #12850, #12856 y pedido 2026-09-16: ni Preasignación, ni Reglas/Requisitos/Configuración, ni Revocatorias.
    for (const name of ["Reglas", "Requisitos", "Configuración", "Preasignación", "Revocatorias"]) {
      expect(within(dock()).queryByRole("link", { name })).not.toBeInTheDocument();
    }

    await userEvent.click(screen.getByRole("button", { name: "Menú de cuenta" }));
    expect(screen.getByText("Admin OT")).toBeInTheDocument();
  });

  it("los módulos homónimos de la SPA no se duplican: Trámites es el del organismo", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: "u1", role: "ot_admin", email: "ot@transito.gov.co" }));
    renderShell();
    await within(dock()).findByRole("link", { name: "Trámites" });
    expect(within(dock()).getAllByRole("link", { name: "Trámites" })).toHaveLength(1);
  });

  // HU #12860 — Documentos y Usuarios solo para ot_admin.
  it.each(["gestor_tramites_ot", "otro_rol_ot"])("un %s no ve Documentos ni Usuarios", async (role) => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role, entity_type: "TRANSIT_OFFICE", email: "ot@transito.gov.co" }),
    );
    renderShell();
    await within(dock()).findByRole("link", { name: "Trámites" });
    expect(within(dock()).queryByRole("link", { name: "Documentos" })).not.toBeInTheDocument();
    expect(within(dock()).queryByRole("link", { name: "Usuarios" })).not.toBeInTheDocument();
    const admin = within(dock()).queryByRole("button", { name: "Administración" });
    if (admin) {
      await userEvent.click(admin);
      expect(within(dock()).queryByRole("link", { name: "Documentos" })).not.toBeInTheDocument();
    }
  });

  it("marca la pestaña del organismo según la ruta", async () => {
    nav.pathname = "/admin/transit-offices/ot-1/reportes";
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: "u1", role: "ot_admin", email: "ot@transito.gov.co" }));
    renderShell();
    expect(await within(dock()).findByRole("link", { name: "Reportes" })).toHaveAttribute("aria-current", "page");
  });
});

describe("Shell — AdminCompany", () => {
  it("una sola entrada 'Administración' a su compañía, sin Compañías ni hub OT", () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: "u1", role: "AdminCompany", email: "admin@empresa.local" }));
    renderShell();

    // Ítem único en administradores → píldora directa con el label del ítem.
    expect(within(dock()).getByRole("link", { name: "Administración" })).toHaveAttribute("href", "/admin/companies");
    expect(within(dock()).queryByRole("button", { name: "Administradores" })).not.toBeInTheDocument();
    expect(within(dock()).queryByRole("link", { name: "Compañías" })).not.toBeInTheDocument();
    expect(within(dock()).queryByRole("link", { name: "Revocatorias" })).not.toBeInTheDocument();
  });

  it("cabeza de grupo: también 'Red de clientes', activa en su ruta (antes el dock la descartaba)", async () => {
    nav.pathname = "/admin/companies/t-1/children";
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "AdminCompany", tenant_id: "t-1", is_group_parent: true, email: "admin@empresa.local" }),
    );
    renderShell();

    await userEvent.click(within(dock()).getByRole("button", { name: "Administradores" }));
    const red = within(dock()).getByRole("link", { name: "Red de clientes" });
    expect(red).toHaveAttribute("href", "/admin/companies/t-1/children");
    expect(red).toHaveAttribute("aria-current", "page");
    expect(within(dock()).getByRole("link", { name: "Administración" })).not.toHaveAttribute("aria-current");
  });
});

describe("Shell — dock SuperAdmin (HU #10469)", () => {
  it("muestra Compañías, Tránsito, Improntas, RBAC y Auditoría dentro de Administradores", async () => {
    setDevSuperAdminToken();
    renderShell();
    expect(within(dock()).queryByRole("link", { name: "Compañías" })).not.toBeInTheDocument();
    await userEvent.click(within(dock()).getByRole("button", { name: "Administradores" }));
    expect(within(dock()).getByRole("link", { name: "Compañías" })).toHaveAttribute("href", "/admin/companies");
    expect(within(dock()).getByRole("button", { name: "Tránsito" })).toBeInTheDocument();
    expect(within(dock()).getByRole("link", { name: "Improntas" })).toBeInTheDocument();
    expect(within(dock()).getByRole("link", { name: "RBAC Admin" })).toHaveAttribute("href", "/?m=rbac");
    expect(within(dock()).getByRole("link", { name: "Auditoría" })).toHaveAttribute("href", "/?m=auditoria");
  });

  it("anida Organismos y Causales de rechazo bajo Administradores → Tránsito", async () => {
    setDevSuperAdminToken();
    renderShell();
    await userEvent.click(within(dock()).getByRole("button", { name: "Administradores" }));
    await userEvent.click(within(dock()).getByRole("button", { name: "Tránsito" }));
    expect(within(dock()).getByRole("link", { name: "Organismos" })).toHaveAttribute("href", "/admin/transit-offices");
    expect(within(dock()).getByRole("link", { name: "Causales de rechazo" })).toBeInTheDocument();
  });

  it("muestra Mandatos y Notificaciones dentro de Administradores → Plataforma, en ese orden (HU #11369 AC1)", async () => {
    setDevSuperAdminToken();
    renderShell();
    await userEvent.click(within(dock()).getByRole("button", { name: "Administradores" }));
    await userEvent.click(within(dock()).getByRole("button", { name: "Plataforma" }));

    const mandatos = within(dock()).getByRole("link", { name: "Mandatos" });
    const notificaciones = within(dock()).getByRole("link", { name: "Notificaciones" });
    expect(mandatos.compareDocumentPosition(notificaciones) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("un usuario sin sesión SuperAdmin no ve Administradores, Plataforma ni Improntas (HU #11369 AC2)", () => {
    renderShell();
    for (const name of ["Administradores", "Plataforma"]) {
      expect(within(dock()).queryByRole("button", { name })).not.toBeInTheDocument();
    }
    expect(within(dock()).queryByRole("link", { name: "Improntas" })).not.toBeInTheDocument();
  });

  it("muestra Log QX e ICT en Integraciones, con Log ICT y Reportes ICT anidados bajo ICT", async () => {
    setDevSuperAdminToken();
    renderShell();
    await userEvent.click(within(dock()).getByRole("button", { name: "Integraciones" }));
    expect(within(dock()).getByRole("link", { name: "Log QX" })).toHaveAttribute("href", "/?m=log-qx");
    expect(within(dock()).queryByRole("link", { name: "Log ICT" })).not.toBeInTheDocument();
    await userEvent.click(within(dock()).getByRole("button", { name: "ICT" }));
    expect(within(dock()).getByRole("link", { name: "Log ICT" })).toBeInTheDocument();
    expect(within(dock()).getByRole("link", { name: "Reportes ICT" })).toBeInTheDocument();
  });
});

describe("Shell — barra y menú de cuenta", () => {
  it("sin campana de notificaciones en la barra", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "Notificaciones" })).not.toBeInTheDocument();
  });

  it("el menú de cuenta trae Ayuda, Cambio de contraseña y Cerrar sesión, sin 'Actualización de la información'", async () => {
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Menú de cuenta" }));
    expect(screen.getByRole("link", { name: "Ayuda" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Cambio de contraseña" })).toHaveAttribute("href", "/profile/change-password");
    expect(screen.getByRole("link", { name: "Cerrar sesión" })).toBeInTheDocument();
    expect(screen.queryByText("Actualización de la información")).not.toBeInTheDocument();
  });

  it("Cerrar sesión usa el cierre de la app cuando lo hay (sesión antigua o @flit/auth)", async () => {
    const onLogout = vi.fn();
    render(
      <Shell onLogout={onLogout} visibleModuleCodes={ALL_SPA}>
        <div>contenido</div>
      </Shell>,
    );
    await userEvent.click(screen.getByRole("button", { name: "Menú de cuenta" }));
    await userEvent.click(screen.getByRole("button", { name: "Cerrar sesión" }));
    expect(onLogout).toHaveBeenCalledOnce();
  });

  it("el contenido hace scroll en [data-shell-scroll], con colchón para el dock", () => {
    renderShell();
    const box = screen.getByText("contenido").parentElement!;
    expect(box).toHaveAttribute("data-shell-scroll");
    expect(box.className).toContain("overflow-y-auto");
    expect(box.className).toMatch(/pb-\d/);
  });
});
