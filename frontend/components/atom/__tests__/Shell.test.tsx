// Dock del Shell: FAB centrado, reparto por lado declarado, agrupadores menú/submenú.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { setDevSuperAdminToken } from "@/lib/api/client";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";

vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useRouter: () => ({ push: vi.fn() }),
}));

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
  it("no muestra 'Ayuda' en el dock; la entrada vive en el menú de usuario", async () => {
    renderShell(["dashboard", "reportes"]);
    expect(screen.queryByRole("button", { name: "Ayuda" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Menú de usuario" }));
    expect(screen.getByText("Ayuda")).toBeInTheDocument();
  });

  it("reparte el dock por lado declarado: Trámites a la izquierda del FAB, Usuarios a la derecha", () => {
    renderShell(["tramites", "usuarios", "reportes", "validaciones"]);
    const fab = screen.getByRole("button", { name: "Inicio FLIT" });
    const dock = fab.parentElement;
    expect(dock).not.toBeNull();
    const children = Array.from(dock!.children);
    const fabIndex = children.indexOf(fab);
    const labelsBeforeFab = children
      .slice(0, fabIndex)
      .map((el) => el.getAttribute("aria-label"));
    const labelsAfterFab = children
      .slice(fabIndex + 1)
      .map((el) => el.getAttribute("aria-label"));
    expect(labelsBeforeFab).toContain("Trámites");
    expect(labelsAfterFab).toContain("Usuarios");
  });

  it("ítem activo del dock (aria-current) no lleva fondo degradado inline", () => {
    render(
      <Shell active="reportes" onNav={vi.fn()} visibleModuleCodes={["reportes", "tramites"]}>
        <div>contenido</div>
      </Shell>,
    );
    const active = screen.getByRole("button", { name: "Reportes" });
    expect(active).toHaveAttribute("aria-current", "page");
    expect(active.style.background).toBe("");
    expect(active.style.backgroundImage).toBe("");
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
    expect(screen.getByRole("button", { name: "Usuarios" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reportes" })).toBeInTheDocument();

    // HU12856 AC1 (Feature #12847) — Administración = submenú Documentos (Reglas/Requisitos/
    // Configuración se retiraron: pasaron a exclusivos de Super Admin, ver tests debajo).
    await userEvent.click(screen.getByRole("button", { name: "Administración" }));
    expect(screen.getByRole("button", { name: "Documentos" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reglas" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Requisitos" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Configuración" })).not.toBeInTheDocument();
  });

  // HU12856 AC1 — ni como píldora directa ni dentro de "Administración", para admin u operador.
  it.each([
    ["ot_admin", "Admin OT"],
    ["gestor_tramites_ot", "Operador OT"],
  ])(
    "HU12856 AC1 — un %s (%s) ya NO ve 'Reglas', 'Requisitos' ni 'Configuración' en el dock",
    async (role: string) => {
      window.localStorage.setItem(
        TOKEN_STORAGE_KEY,
        makeToken({ sub: "u1", role, entity_type: "TRANSIT_OFFICE", email: "ot@transito.gov.co" }),
      );

      renderShell();

      expect(screen.queryByRole("button", { name: "Reglas" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Requisitos" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Configuración" })).not.toBeInTheDocument();

      const adminBtn = screen.queryByRole("button", { name: "Administración" });
      if (adminBtn) {
        await userEvent.click(adminBtn);
        expect(screen.queryByRole("button", { name: "Reglas" })).not.toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Requisitos" })).not.toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Configuración" })).not.toBeInTheDocument();
      }
    },
  );

  // HU12850 AC1 — la entrada "Preasignación" se retiró del dock: ni como píldora directa ni
  // dentro de "Administración".
  it("HU12850 AC1 — un Admin OT ya NO ve 'Preasignación' en el dock", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "ot_admin", email: "ot@transito.gov.co" }),
    );

    renderShell();

    expect(screen.queryByRole("button", { name: "Preasignación" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Administración" }));
    expect(screen.queryByRole("button", { name: "Preasignación" })).not.toBeInTheDocument();
  });

  // HU12856 AC1 (Feature #12847) — "Configuración" pasa a ser exclusiva de Super Admin: el
  // Admin OT deja de verla dentro de "Administración" (antes de esta HU era su punto de entrada
  // real al modo Dashboard/QX, la ventana de revocatoria y los feature flags operativos).
  it("HU12856 AC1 — un Admin OT ya NO ve 'Configuración' dentro de Administración", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "ot_admin", email: "ot@transito.gov.co" }),
    );

    renderShell();

    expect(screen.queryByRole("button", { name: "Configuración" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Administración" }));
    expect(screen.queryByRole("button", { name: "Configuración" })).not.toBeInTheDocument();
  });

  // Pedido del usuario (2026-09-16): se retiró la entrada de dock "Revocatorias" del Admin OT —
  // mismo criterio ya aplicado del lado gestor (HU #12578, AC2 revertido): el filtro "Revocado" del
  // listado de trámites ya cubre ese caso de uso sin una pantalla aparte. Test negativo para que no
  // reaparezca por accidente.
  it("un Admin OT NO ve 'Revocatorias' dentro de Administración", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "ot_admin", email: "ot@transito.gov.co" }),
    );

    renderShell();

    await userEvent.click(screen.getByRole("button", { name: "Administración" }));
    expect(screen.queryByRole("button", { name: "Revocatorias" })).not.toBeInTheDocument();
  });

  it("HU #12578 — un AdminCompany (no OT) no tiene el hub OT y por tanto no ve 'Revocatorias'", () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "AdminCompany", email: "admin@empresa.local" }),
    );

    renderShell();

    // Su "Administración" es la consola de compañía (RL, baúl…), no el hub OT: no hay OT_ADM_DOCK.
    expect(screen.queryByRole("button", { name: "Revocatorias" })).not.toBeInTheDocument();
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

    // Se acota al dock (la campana del topbar fue eliminada; aquí se verifica la entrada del dock).
    const dockNav = screen.getByRole("navigation", { name: "Navegación principal" });
    expect(within(dockNav).queryByRole("button", { name: "Plataforma" })).not.toBeInTheDocument();
    expect(
      within(dockNav).queryByRole("button", { name: "Notificaciones" }),
    ).not.toBeInTheDocument();
  });

  it("muestra Log QX e ICT en Integraciones, con Log ICT y Reportes ICT anidados bajo ICT", async () => {
    setDevSuperAdminToken();
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Integraciones" }));
    expect(screen.getByRole("button", { name: "Log QX" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Soporte" })).not.toBeInTheDocument();

    // ICT es contenedor, no destino (HU #11619): sus dos hojas aparecen al abrirlo, mismo patrón
    // que Administradores → Plataforma.
    expect(screen.queryByRole("button", { name: "Log ICT" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "ICT" }));
    const dockNav = screen.getByRole("navigation", { name: "Navegación principal" });
    expect(within(dockNav).getByRole("button", { name: "Log ICT" })).toBeInTheDocument();
    expect(within(dockNav).getByRole("button", { name: "Reportes ICT" })).toBeInTheDocument();
  });

  it("no muestra la entrada 'Improntas' sin sesión SuperAdmin", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "Improntas" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Administradores" })).not.toBeInTheDocument();
  });
});

describe("Shell — topbar (campana y menú de usuario)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("la campana de notificaciones NO está en el topbar", () => {
    renderShell();
    // El botón de campana fue ocultado; no debe existir en el DOM.
    expect(screen.queryByRole("button", { name: "Notificaciones" })).not.toBeInTheDocument();
  });

  it("al abrir el menú de usuario NO aparece 'Actualización de la información'", async () => {
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Menú de usuario" }));
    expect(screen.queryByText("Actualización de la información")).not.toBeInTheDocument();
  });

  it("al abrir el menú de usuario SÍ aparece 'Ayuda' (manual /manual)", async () => {
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Menú de usuario" }));
    expect(screen.getByText("Ayuda")).toBeInTheDocument();
  });

  it("al abrir el menú de usuario SÍ aparece 'Cambio de contraseña'", async () => {
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Menú de usuario" }));
    expect(screen.getByText("Cambio de contraseña")).toBeInTheDocument();
  });

  it("al abrir el menú de usuario SÍ aparece 'Salir de la plataforma'", async () => {
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Menú de usuario" }));
    expect(screen.getByText("Salir de la plataforma")).toBeInTheDocument();
  });
});
