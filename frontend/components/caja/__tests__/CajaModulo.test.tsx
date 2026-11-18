// Feature #14453 — el módulo Caja decide por el token quién anula: el Admin OT sí, la persona de caja no.
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import type { CajaCobro, OtCajaApi } from "@/lib/api/ot-caja";
import { CajaModulo } from "../CajaModulo";

const COBRO: CajaCobro = {
  cobroId: "c1",
  codigo: "PN-2026-000001",
  valorCop: 100000,
  medio: "efectivo",
  pagadoEn: "2026-11-10T15:30:00Z",
  pagadoPorNombre: "Ana Caja",
  estado: "pagado",
  anulable: true,
  rowVersion: 1,
};

function token(roles: string[]): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${b64({ alg: "none" })}.${b64({ roles: roles.map((code) => ({ id: code, code })) })}.`;
}

const api = (): OtCajaApi => ({
  tramite: vi.fn(),
  confirmar: vi.fn(),
  cobros: vi.fn().mockResolvedValue({ items: [COBRO], total: 1, totalDiaCop: 100000 }),
  anular: vi.fn(),
});

afterEach(() => window.localStorage.clear());

describe("CajaModulo", () => {
  it("tiene las pestañas «Cobrar» y «Cobros del día»; abre en Cobrar", () => {
    render(<CajaModulo api={api()} puedeAnular={false} />);
    expect(screen.getByRole("tab", { name: "Cobrar" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Cobros del día" })).toBeInTheDocument();
    expect(screen.getByLabelText("ID del trámite")).toBeInTheDocument();
  });

  it("token ot_admin: en «Cobros del día» ofrece «Acciones»", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, token(["ot_admin"]));
    render(<CajaModulo api={api()} />);
    await userEvent.click(screen.getByRole("tab", { name: "Cobros del día" }));
    expect(await screen.findByRole("button", { name: "Acciones del cobro PN-2026-000001" })).toBeInTheDocument();
  });

  it("token ot_caja: en «Cobros del día» no hay «Acciones»", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, token(["ot_caja"]));
    render(<CajaModulo api={api()} />);
    await userEvent.click(screen.getByRole("tab", { name: "Cobros del día" }));
    expect(await screen.findByText("PN-2026-000001")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Acciones del cobro/ })).not.toBeInTheDocument();
  });
});
