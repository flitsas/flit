// Bug #13445: el contenedor ICT del dock se arma si el usuario ve al menos una hoja; cada hoja tiene
// su propio gate (Log ICT solo SuperAdmin, Trazabilidad `ict.trazabilidad.read`, Reportes `ict.reportes.read`).
// Uso de ejemplo: un Admin de Compañía con los dos slugs ve "ICT" → "Trazabilidad ICT" y "Reportes ICT", sin "Log ICT".
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { setDevSuperAdminToken } from "@/lib/api/client";
import { TOKEN_COOKIE, TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";

vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useRouter: () => ({ push: vi.fn() }),
}));

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

/**
 * Abre el contenedor ICT en el dock. Si Integraciones tiene varios ítems es un submenú; si ICT es el
 * único, la píldora del grupo se llama "ICT" y el panel repite "ICT" como nodo anidado.
 */
async function abrirIct(): Promise<void> {
  const integraciones = screen.queryByRole("button", { name: "Integraciones" });
  if (integraciones) await userEvent.click(integraciones);
  const icts = screen.getAllByRole("button", { name: "ICT" });
  await userEvent.click(icts[icts.length - 1]);
  if (screen.queryByRole("button", { name: "Reportes ICT" })) return;
  const anidados = screen.getAllByRole("button", { name: "ICT" });
  await userEvent.click(anidados[anidados.length - 1]);
}

describe("Shell — contenedor ICT por permiso (Bug #13445)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
    document.cookie = `${TOKEN_COOKIE}=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT`;
  });

  it("Admin de Compañía con ict.trazabilidad.read + ict.reportes.read ve ICT con Trazabilidad y Reportes, sin Log", async () => {
    setToken({
      sub: "u1",
      role_code: "AdminCompany",
      permissions: ["ict.trazabilidad.read", "ict.reportes.read"],
    });
    renderShell();
    await abrirIct();
    expect(screen.getByRole("button", { name: "Trazabilidad ICT" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reportes ICT" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Log ICT" })).not.toBeInTheDocument();
  });

  it("solo ict.logs.read sin ser SuperAdmin no ve el contenedor ICT", () => {
    setToken({ sub: "u2", role_code: "Soporte", permissions: ["ict.logs.read"] });
    renderShell();
    expect(screen.queryByRole("button", { name: "ICT" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Log ICT" })).not.toBeInTheDocument();
  });

  it("SuperAdmin ve las tres hojas de ICT", async () => {
    setDevSuperAdminToken();
    renderShell();
    await abrirIct();
    expect(screen.getByRole("button", { name: "Log ICT" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Trazabilidad ICT" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reportes ICT" })).toBeInTheDocument();
  });
});
