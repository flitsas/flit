// AC4 (HU #10795): la entrada "LOG QX" del dock solo se renderiza cuando el usuario en sesión
// puede leer el LOG QX — tiene el permiso `logqx.read` o es SuperAdmin (bypass). Calcado del
// patrón de "Auditoría" (Shell.auditoria.test.tsx), pero gateado por permiso, no solo por rol.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { setDevSuperAdminToken } from "@/lib/api/client";
import { TOKEN_COOKIE, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));

/** Inyecta un JWT (sin firma) con los claims dados en cookie + localStorage. */
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

describe("Shell — dock LOG QX (HU #10795, AC4)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
    document.cookie = `${TOKEN_COOKIE}=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT`;
  });

  it("muestra 'LOG QX' cuando el usuario tiene el permiso logqx.read (sin ser SuperAdmin)", () => {
    setToken({ sub: "u1", role_code: "Soporte", permissions: ["logqx.read"] });
    renderShell();
    expect(screen.getByRole("button", { name: "LOG QX" })).toBeInTheDocument();
  });

  it("muestra 'LOG QX' cuando el usuario es SuperAdmin (bypass)", () => {
    setDevSuperAdminToken();
    renderShell();
    expect(screen.getByRole("button", { name: "LOG QX" })).toBeInTheDocument();
  });

  it("NO muestra 'LOG QX' para un usuario autenticado sin el permiso logqx.read", () => {
    setToken({ sub: "u2", role_code: "AdminCompany", permissions: ["tramites.read"] });
    renderShell();
    expect(screen.queryByRole("button", { name: "LOG QX" })).not.toBeInTheDocument();
  });

  it("NO muestra 'LOG QX' sin sesión", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "LOG QX" })).not.toBeInTheDocument();
  });
});
