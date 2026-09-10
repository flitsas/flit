// AC1 (HU #10680): "Auditoría" vive en el agrupador Administradores (solo SuperAdmin).
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { setDevSuperAdminToken } from "@/lib/api/client";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import { Shell } from "../Shell";

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));

function renderShell() {
  return render(
    <Shell active="dashboard" onNav={vi.fn()}>
      <div>contenido</div>
    </Shell>,
  );
}

describe("Shell — dock Auditoría (HU #10680, AC1)", () => {
  afterEach(() => {
    window.localStorage.removeItem(TOKEN_STORAGE_KEY);
  });

  it("muestra la entrada 'Auditoría' en el submenú Administradores (SuperAdmin)", async () => {
    setDevSuperAdminToken();
    renderShell();
    await userEvent.click(screen.getByRole("button", { name: "Administradores" }));
    expect(screen.getByRole("button", { name: "Auditoría" })).toBeInTheDocument();
  });

  it("no muestra la entrada 'Auditoría' sin sesión SuperAdmin", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "Administradores" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Auditoría" })).not.toBeInTheDocument();
  });
});
