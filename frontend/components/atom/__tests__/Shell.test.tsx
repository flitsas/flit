// Dock del Shell (artefacto inferior): el FAB de inicio debe quedar SIEMPRE centrado y el
// botón "Ayuda" (soporte universal) debe verse aunque RBAC filtre los demás módulos.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { setDevSuperAdminToken } from "@/lib/api/client";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";

// Shell usa usePathname para resaltar la ruta admin activa.
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
    // Número impar de entradas para ejercitar el relleno con espaciador.
    renderShell(["dashboard", "reportes", "validaciones"]);
    const fab = screen.getByRole("button", { name: "Inicio FLIT" });
    const dock = fab.parentElement;
    expect(dock).not.toBeNull();
    const children = Array.from(dock!.children);
    const fabIndex = children.indexOf(fab);
    const before = fabIndex;
    const after = children.length - fabIndex - 1;
    expect(before).toBe(after);
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
    expect(screen.queryByRole("button", { name: "RBAC Admin" })).not.toBeInTheDocument();
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

    await userEvent.click(screen.getByRole("button", { name: "Mi Empresa" }));

    expect(onNav).toHaveBeenCalledWith("usuarios");
  });
});

describe("Shell — dock SuperAdmin (HU #10469)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("muestra la entrada 'Improntas' cuando el usuario en sesión es SuperAdmin", () => {
    setDevSuperAdminToken();
    renderShell();
    expect(screen.getByRole("button", { name: "Improntas" })).toBeInTheDocument();
  });

  it("no muestra la entrada 'Improntas' sin sesión SuperAdmin", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "Improntas" })).not.toBeInTheDocument();
  });
});
