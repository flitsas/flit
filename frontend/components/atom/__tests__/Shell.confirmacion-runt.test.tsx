// HU #12313 (Feature #12276) — la entrada "Confirmación RUNT" cuelga de Administradores → Plataforma
// y se resuelve por permiso: SuperAdmin la ve dentro de su Plataforma completa; un rol con solo el
// permiso ve un submenú Plataforma con esa única entrada; sin permiso no hay Plataforma.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
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

function renderShell() {
  return render(
    <Shell visibleModuleCodes={["tramites"]}>
      <div>contenido</div>
    </Shell>,
  );
}

describe("Shell — Administradores → Plataforma → Confirmación RUNT", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("SuperAdmin la ve dentro de Plataforma, después de Tipos de trámites (AC4)", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: "u1", role: "SuperAdmin" }));
    renderShell();

    await userEvent.click(screen.getByRole("button", { name: "Administradores" }));

    // HU #14479: Plataforma es una sección con nombre del submenú; la entrada lleva su descripción, así que se compara
    // por el nombre accesible y no por el texto.
    const labels = within(screen.getByRole("group", { name: "Plataforma" }))
      .getAllByRole("link")
      .map((b) => b.getAttribute("aria-label") ?? b.textContent?.trim())
      .filter((t) => t === "Tipos de trámites" || t === "Confirmación RUNT" || t === "Mandatos");
    expect(labels).toEqual(["Tipos de trámites", "Confirmación RUNT", "Mandatos"]);
  });

  it("un rol con solo runt_confirmation.history.read ve Plataforma con esa única entrada (AC4)", async () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "Auditor", permissions: ["runt_confirmation.history.read"] }),
    );
    renderShell();

    // Con un solo ítem en el grupo Administradores, la píldora toma el label del ítem
    // (Plataforma) en vez del grupo — regla de buildDockGroups. La píldora abre el panel y
    // dentro va la sección Plataforma con sus entradas (HU #14479): un clic.
    await userEvent.click(screen.getByRole("button", { name: "Plataforma" }));

    const plataforma = within(screen.getByRole("group", { name: "Plataforma" }));
    expect(plataforma.getByRole("link", { name: "Confirmación RUNT" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Tipos de trámites" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Mandatos" })).not.toBeInTheDocument();
  });

  it("sin ninguno de los dos permisos no aparece Plataforma ni la entrada (AC4)", () => {
    window.localStorage.setItem(
      TOKEN_STORAGE_KEY,
      makeToken({ sub: "u1", role: "Auditor", permissions: ["tramites.read"] }),
    );
    renderShell();

    expect(screen.queryByRole("button", { name: "Plataforma" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Confirmación RUNT" })).not.toBeInTheDocument();
  });
});
