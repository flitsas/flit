// Dock del Shell: FAB centrado, Ayuda universal, agrupadores menú/submenú.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { setDevSuperAdminToken } from "@/lib/api/client";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

function renderShell(visibleModuleCodes?: string[]) {
  return render(
    <Shell active="dashboard" onNav={vi.fn()} visibleModuleCodes={visibleModuleCodes}>
      <div>contenido</div>
    </Shell>,
  );
}

describe("Shell — dock", () => {
  it("muestra 'Ayuda' aunque RBAC no la incluya en los módulos visibles", () => {
    renderShell(["dashboard", "reportes"]);
    expect(screen.getByRole("button", { name: "Ayuda" })).toBeInTheDocument();
  });

  it("mantiene el FAB de inicio centrado: mismo nº de elementos a cada lado", () => {
    renderShell(["dashboard", "reportes", "validaciones"]);
    const fab = screen.getByRole("button", { name: "Inicio FLIT" });
    const dock = fab.parentElement;
    expect(dock).not.toBeNull();
    const children = Array.from(dock!.children);
    const fabIndex = children.indexOf(fab);
    expect(fabIndex).toBe(children.length - fabIndex - 1);
  });

  it("usa favicon.svg en el FAB central", () => {
    renderShell(["dashboard"]);
    const fab = screen.getByRole("button", { name: "Inicio FLIT" });
    const img = fab.querySelector("img");
    expect(img?.getAttribute("src")).toBe("/assets/favicon.svg");
  });
});

describe("Shell — ot_admin (refactor adminOT)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("muestra Admin OT con dock de hub (sin Compañías / Documental / Tránsito único)", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "ot_admin", email: "ot@transito.gov.co" }),
    );

    renderShell();

    expect(screen.getByText("Admin OT")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Tránsito" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Compañías" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Documental" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Administradores" })).not.toBeInTheDocument();

    // Ítems directos del dock Admin OT
    expect(screen.getByRole("button", { name: "Trámites" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Preasignación" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Usuarios" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reportes" })).toBeInTheDocument();

    // Administración = submenú Reglas / Documentos / Requisitos
    await userEvent.click(screen.getByRole("button", { name: "Administración" }));
    expect(screen.getByRole("button", { name: "Reglas" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Documentos" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Requisitos" })).toBeInTheDocument();
  });
});

describe("Shell — Administración gestora (AdminCompany)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("muestra 'Administración' y no empuja Usuarios en el menú admin", () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "AdminCompany", email: "admin@empresa.local" }),
    );
    render(
      <Shell active="dashboard" onNav={vi.fn()}>
        <div>contenido</div>
      </Shell>,
    );

    // Ítem único en administradores → píldora directa con el label del ítem.
    expect(screen.getByRole("button", { name: "Administración" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Administradores" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Mi Empresa" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Compañías" })).not.toBeInTheDocument();
  });
});

describe("Shell — dock SuperAdmin (HU #10469)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("muestra Compañías, Tránsito e Improntas dentro de Administradores", async () => {
    setDevSuperAdminToken();
    renderShell();
    expect(screen.queryByRole("button", { name: "Compañías" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Tránsito" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Administradores" }));
    expect(screen.getByRole("button", { name: "Compañías" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tránsito" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Improntas" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "RBAC Admin" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Auditoría" })).toBeInTheDocument();
  });

  it("anida Organismos y Causales de rechazo bajo Administradores → Tránsito", async () => {
    setDevSuperAdminToken();
    renderShell();
    // El catálogo alimenta el modal de rechazo del organismo: cuelga de Tránsito, no de Compañías.
    await userEvent.click(screen.getByRole("button", { name: "Administradores" }));
    await userEvent.click(screen.getByRole("button", { name: "Tránsito" }));
    expect(screen.getByRole("button", { name: "Organismos" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Causales de rechazo" })).toBeInTheDocument();
  });

  it("muestra Mandatos y Notificaciones dentro de Administradores → Plataforma, en ese orden (HU #11369 AC1)", async () => {
    setDevSuperAdminToken();
    renderShell();
    expect(screen.queryByRole("button", { name: "Plataforma" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Administradores" }));
    await userEvent.click(screen.getByRole("button", { name: "Plataforma" }));

    // Se acota al panel del dock para no confundir con el icono genérico de
    // notificaciones del topbar (aria-label="Notificaciones", ajeno a esta HU).
    const dockNav = screen.getByRole("navigation", { name: "Navegación principal" });
    const mandatosBtn = within(dockNav).getByRole("button", { name: "Mandatos" });
    const notificacionesBtn = within(dockNav).getByRole("button", { name: "Notificaciones" });
    expect(mandatosBtn).toBeInTheDocument();
    expect(notificacionesBtn).toBeInTheDocument();

    // AC1: Mandatos y Notificaciones, EN ESE ORDEN.
    expect(
      mandatosBtn.compareDocumentPosition(notificacionesBtn) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it("un usuario sin sesión SuperAdmin no ve Plataforma ni Notificaciones (HU #11369 AC2)", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "Administradores" })).not.toBeInTheDocument();

    // Se acota al dock: el topbar tiene un icono genérico "Notificaciones"
    // (campana de alertas) ajeno a esta HU y siempre presente.
    const dockNav = screen.getByRole("navigation", { name: "Navegación principal" });
    expect(within(dockNav).queryByRole("button", { name: "Plataforma" })).not.toBeInTheDocument();
    expect(
      within(dockNav).queryByRole("button", { name: "Notificaciones" }),
    ).not.toBeInTheDocument();
  });

  it("muestra Log QX y Log ICT en el agrupador Integraciones", async () => {
    setDevSuperAdminToken();
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Integraciones" }));
    expect(screen.getByRole("button", { name: "Log QX" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Log ICT" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Soporte" })).not.toBeInTheDocument();
  });

  it("no muestra la entrada 'Improntas' sin sesión SuperAdmin", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "Improntas" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Administradores" })).not.toBeInTheDocument();
  });
});
