// HU #13420 (épica #13216) AC5 — sobre el Shell de staging (dock inline con buildDockGroups):
// «Descarga masiva» cuelga de Administradores → Plataforma solo para SuperAdmin y lleva a
// /admin/plataforma/descarga-masiva. AdminCompany (aunque tenga banners.manage) y ot_admin no la ven.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";

// Uso de ejemplo:
//   SuperAdmin → dock «Administradores» → «Plataforma» → «Descarga masiva» → router.push("/admin/plataforma/descarga-masiva")

const router = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useRouter: () => router,
}));

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

function renderShell() {
  return render(
    <Shell active="dashboard" onNav={vi.fn()}>
      <div>contenido</div>
    </Shell>,
  );
}

const dock = () => screen.getByRole("navigation", { name: "Navegación principal" });

/** Abre el grupo del dock que contiene «Plataforma» y luego el submenú «Plataforma». */
async function abrirPlataforma() {
  const grupo =
    within(dock()).queryByRole("button", { name: "Administradores" }) ??
    within(dock()).queryByRole("button", { name: "Administración" });
  if (grupo) await userEvent.click(grupo);
  const plataformas = within(dock()).getAllByRole("button", { name: "Plataforma" });
  await userEvent.click(plataformas[plataformas.length - 1]);
}

beforeEach(() => {
  router.push.mockReset();
});
afterEach(() => {
  window.localStorage.removeItem(TOKEN_STORAGE_KEY);
});

describe("Shell — Administradores → Plataforma → Descarga masiva (HU #13420 AC5)", () => {
  it("SuperAdmin la ve en Plataforma, después de Notificaciones, y navega a /admin/plataforma/descarga-masiva", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: "u1", role: "SuperAdmin" }));
    renderShell();

    expect(within(dock()).queryByRole("button", { name: "Descarga masiva" })).not.toBeInTheDocument();
    await abrirPlataforma();

    const notificaciones = within(dock()).getByRole("button", { name: "Notificaciones" });
    const descarga = within(dock()).getByRole("button", { name: "Descarga masiva" });
    expect(
      notificaciones.compareDocumentPosition(descarga) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();

    await userEvent.click(descarga);
    expect(router.push).toHaveBeenCalledWith("/admin/plataforma/descarga-masiva");
  });

  it("AdminCompany con banners.manage ve Plataforma (Banners) pero NO «Descarga masiva»", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "AdminCompany", permissions: ["banners.manage"] }),
    );
    renderShell();

    await abrirPlataforma();
    expect(within(dock()).getByRole("button", { name: "Banners" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Descarga masiva" })).not.toBeInTheDocument();
    expect(router.push).not.toHaveBeenCalledWith("/admin/plataforma/descarga-masiva");
  });

  it("ot_admin no tiene Plataforma ni «Descarga masiva» (ni dentro de Administración)", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "ot_admin", entity_type: "TRANSIT_OFFICE" }),
    );
    renderShell();

    expect(screen.queryByRole("button", { name: "Plataforma" })).not.toBeInTheDocument();
    const admin = within(dock()).queryByRole("button", { name: "Administración" });
    if (admin) await userEvent.click(admin);
    expect(screen.queryByRole("button", { name: "Plataforma" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Descarga masiva" })).not.toBeInTheDocument();
  });
});
