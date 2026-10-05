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
    <Shell visibleModuleCodes={visibleModuleCodes}>
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
    expect(screen.getByRole("link", { name: "Generación documental" })).toHaveAttribute("href", "/admin/generacion-documental");
  });

  it("un usuario cuyo listado de módulos NO incluye el módulo no ve la entrada", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, ADMIN_COMPANY_TOKEN);

    renderShell(["tramites", "reportes"]);

    // "Administración" (AdminCompany) sigue existiendo, sola: sin la entrada del módulo es un enlace directo.
    expect(screen.getByRole("link", { name: "Administración" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Generación documental" })).not.toBeInTheDocument();
  });

  it("sin lista de módulos resuelta (undefined) un AdminCompany no la ve (deny-by-default)", () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, ADMIN_COMPANY_TOKEN);

    renderShell();

    expect(screen.queryByRole("link", { name: "Generación documental" })).not.toBeInTheDocument();
  });

  // B-13: el catálogo de la suite deja pasar al SuperAdmin en todo (contrato §2.1). La API ya le devuelve todos los
  // módulos, así que el resultado final es el mismo; solo cambia que no espera a que carguen.
  it("el SuperAdmin la ve aunque los módulos no hayan cargado", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "SuperAdmin", email: "super@flit.local" }),
    );

    renderShell();

    await userEvent.click(screen.getByRole("button", { name: "Administradores" }));
    expect(screen.getByRole("link", { name: "Generación documental" })).toBeInTheDocument();
  });

  it("un SuperAdmin con el módulo accesible también la ve (misma vía: módulos, no rol)", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "SuperAdmin", email: "super@flit.local" }),
    );

    renderShell(["generacion-documental"]);

    await userEvent.click(screen.getByRole("button", { name: "Administradores" }));
    expect(screen.getByRole("link", { name: "Generación documental" })).toBeInTheDocument();
  });
});
