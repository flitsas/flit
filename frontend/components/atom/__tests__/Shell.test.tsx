// Dock del Shell: FAB centrado, Ayuda universal, agrupadores menú/submenú.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
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

  it("muestra el roleLabel 'Admin OT' y el botón 'Tránsito', sin botones de SuperAdmin/AdminCompany", () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "ot_admin", email: "ot@transito.gov.co" }),
    );

    renderShell();

    expect(screen.getByText("Admin OT")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tránsito" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Compañías" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Documental" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Administradores" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Mi Empresa" })).not.toBeInTheDocument();
  });
});

describe("Shell — Mi Empresa (HU #10512)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("AC1 — navega internamente al módulo de Usuarios en vez de salir de la SPA", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "AdminCompany", email: "admin@empresa.local" }),
    );
    const onNav = vi.fn();
    render(
      <Shell active="dashboard" onNav={onNav}>
        <div>contenido</div>
      </Shell>,
    );

    // Grupo Compañías con un solo ítem visible → píldora directa "Mi Empresa".
    await userEvent.click(screen.getByRole("button", { name: "Mi Empresa" }));

    expect(onNav).toHaveBeenCalledWith("usuarios");
  });
});

describe("Shell — dock SuperAdmin (HU #10469)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("muestra 'Improntas' dentro del agrupador Compañías cuando es SuperAdmin", async () => {
    setDevSuperAdminToken();
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Compañías" }));
    expect(screen.getByRole("button", { name: "Improntas" })).toBeInTheDocument();
  });

  it("no muestra la entrada 'Improntas' sin sesión SuperAdmin", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "Improntas" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Compañías" })).not.toBeInTheDocument();
  });
});
