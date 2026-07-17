// AC1 (HU #10680): la entrada "Auditoría" del dock (puerta de entrada al módulo
// SuperAdmin-only) solo se renderiza cuando el usuario en sesión es SuperAdmin —
// calcado del patrón ya usado para "Improntas" (HU #10469) en Shell.test.tsx.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
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

  it("muestra la entrada 'Auditoría' cuando el usuario en sesión es SuperAdmin", () => {
    setDevSuperAdminToken();
    renderShell();
    expect(screen.getByRole("button", { name: "Auditoría" })).toBeInTheDocument();
  });

  it("no muestra la entrada 'Auditoría' sin sesión SuperAdmin", () => {
    renderShell();
    expect(screen.queryByRole("button", { name: "Auditoría" })).not.toBeInTheDocument();
  });
});
