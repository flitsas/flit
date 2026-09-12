// HU-01 (Feature #12201) — R12: la entrada "Generación documental" del dock se resuelve por
// los MÓDULOS ACCESIBLES del usuario, no por `currentUser?.isSuperAdmin`.
// Uso de ejemplo: <Shell visibleModuleCodes={["generacion-documental"]} …/> → la entrada aparece.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

const ADMIN_COMPANY_TOKEN = makeToken({
  sub: "u1",
  role: "AdminCompany",
  email: "admin@empresa.local",
});

describe("Shell — entrada de dock 'Generación documental' (R12)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("un AdminCompany (isSuperAdmin = false) con el módulo accesible VE la entrada", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, ADMIN_COMPANY_TOKEN);

    renderShell(["tramites", "generacion-documental"]);

    await userEvent.click(screen.getByRole("button", { name: "Administradores" }));
    expect(screen.getByRole("button", { name: "Generación documental" })).toBeInTheDocument();
  });

  it("un usuario cuyo listado de módulos NO incluye el módulo no ve la entrada", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, ADMIN_COMPANY_TOKEN);

    renderShell(["tramites", "reportes"]);

    // "Administración" (AdminCompany) sigue existiendo, pero sin la entrada del módulo.
    await userEvent.click(screen.getByRole("button", { name: "Administración" }));
    expect(
      screen.queryByRole("button", { name: "Generación documental" }),
    ).not.toBeInTheDocument();
  });

  it("sin lista de módulos resuelta (undefined) la entrada no se muestra, ni siquiera a SuperAdmin", () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "SuperAdmin", email: "super@flit.local" }),
    );

    renderShell();

    expect(
      screen.queryByRole("button", { name: "Generación documental" }),
    ).not.toBeInTheDocument();
  });

  it("un SuperAdmin con el módulo accesible también la ve (misma vía: módulos, no rol)", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "SuperAdmin", email: "super@flit.local" }),
    );

    renderShell(["generacion-documental"]);

    await userEvent.click(screen.getByRole("button", { name: "Administradores" }));
    expect(screen.getByRole("button", { name: "Generación documental" })).toBeInTheDocument();
  });
});
