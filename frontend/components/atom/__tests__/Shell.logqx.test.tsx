// AC4 (HU #10795): "Log QX" en agrupador Integraciones (permiso logqx.read o SuperAdmin).
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { setDevSuperAdminToken } from "@/lib/api/client";
import { TOKEN_COOKIE, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));

function setToken(payload: Record<string, unknown>): void {
  const b64 = (o: object) =>
    Buffer.from(JSON.stringify(o))
      .toString("base64")
      .replace(/\+/g, "-")
      .replace(/\//g, "_")
      .replace(/=+$/, "");
  const token = `${b64({ alg: "none", typ: "JWT" })}.${b64(payload)}.`;
  document.cookie = `${TOKEN_COOKIE}=${token}; path=/`;
  window.localStorage.setItem(TOKEN_STORAGE_KEY, token);
}

function renderShell() {
  return render(
    <Shell active="dashboard" onNav={vi.fn()}>
      <div>contenido</div>
    </Shell>,
  );
}

describe("Shell — dock Log QX (HU #10795, AC4)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
    document.cookie = `${TOKEN_COOKIE}=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT`;
  });

  it("muestra 'Log QX' cuando el usuario tiene el permiso logqx.read (sin ser SuperAdmin)", () => {
    setToken({ sub: "u1", role_code: "Soporte", permissions: ["logqx.read"] });
    renderShell();
    // Solo un ítem en Integraciones → píldora directa.
    expect(screen.getByRole("button", { name: "Log QX" })).toBeInTheDocument();
  });

  it("muestra 'Log QX' en el submenú Integraciones cuando el usuario es SuperAdmin", async () => {
    setDevSuperAdminToken();
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Integraciones" }));
    expect(screen.getByRole("button", { name: "Log QX" })).toBeInTheDocument();
  });

  it("NO muestra 'Log QX' para un usuario autenticado sin el permiso logqx.read", () => {
    setToken({ sub: "u2", role_code: "AdminCompany", permissions: ["tramites.read"] });
    renderShell();
    expect(screen.queryByRole("button", { name: "Log QX" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Integraciones" })).not.toBeInTheDocument();
  });

  it("NO muestra 'Log QX' sin sesión", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "Log QX" })).not.toBeInTheDocument();
  });
});
