// HU #14390 AC4 — el historial de Plataforma muestra «Registrada por» solo cuando el servidor informa el autor
// (`creadaPorNombre`, aditivo); sin el campo el historial se ve exactamente como antes. Los errores `{ code, message }`
// del portal del OT muestran su motivo; los 403 de Plataforma conservan su texto.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import { TarifasTab } from "../TarifasTab";
import { pnError } from "../pnErrors";
import { fetchHistorialTarifaPn, fetchTarifasPn, type TarifaPn } from "@/lib/api/admin-pn-tarifas";
import { fetchOrganismosPn, fetchTiposPn } from "@/lib/api/admin-pn-config";

vi.mock("@/lib/api/admin-pn-tarifas", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-tarifas")>();
  return { ...real, fetchTarifasPn: vi.fn(), fetchHistorialTarifaPn: vi.fn(), registrarTarifasPn: vi.fn() };
});
vi.mock("@/lib/api/admin-pn-config", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-config")>();
  return { ...real, fetchTiposPn: vi.fn(), fetchOrganismosPn: vi.fn() };
});

const tarifa = (over: Partial<TarifaPn>): TarifaPn => ({
  id: "a",
  concepto: "DERECHOS_TRANSITO",
  tipoTramiteId: "t",
  tipoTramiteCodigo: "TRASPASO",
  tipoTramiteNombre: "Traspaso",
  organismoId: "o",
  organismoCodigo: "BOG",
  organismoNombre: "Bogotá",
  valorCop: 245000,
  vigenteDesde: "2026-01-15",
  vigenteHasta: null,
  estado: "vigente",
  creadaEn: "2026-01-10T15:00:00Z",
  creadaPor: "abcdef12-0000-0000-0000-000000000000",
  actualizadaEn: null,
  actualizadaPor: null,
  ...over,
});

async function abrirHistorial() {
  render(
    <ToastProvider>
      <TarifasTab />
    </ToastProvider>,
  );
  const fila = await screen.findByRole("row", { name: /traspaso/i });
  await userEvent.click(within(fila).getByRole("button", { name: /ver historial/i }));
  return screen.findByRole("dialog");
}

describe("TarifasTab (Plataforma): autor en el historial", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTarifasPn).mockResolvedValue([tarifa({})]);
    vi.mocked(fetchTiposPn).mockResolvedValue([]);
    vi.mocked(fetchOrganismosPn).mockResolvedValue([]);
  });

  it("con creadaPorNombre muestra la columna y el nombre", async () => {
    vi.mocked(fetchHistorialTarifaPn).mockResolvedValue([tarifa({ creadaPorNombre: "Laura Gómez" })]);
    const modal = await abrirHistorial();
    expect(await within(modal).findByText("Laura Gómez")).toBeInTheDocument();
    expect(within(modal).getByRole("columnheader", { name: /registrada por/i })).toBeInTheDocument();
  });

  it("sin el campo el historial no cambia", async () => {
    vi.mocked(fetchHistorialTarifaPn).mockResolvedValue([tarifa({})]);
    const modal = await abrirHistorial();
    expect(await within(modal).findByText("$ 245.000")).toBeInTheDocument();
    expect(within(modal).queryByRole("columnheader", { name: /registrada por/i })).not.toBeInTheDocument();
  });
});

describe("pnError", () => {
  it("403 con code y message (portal del OT) muestra el motivo del servidor", () => {
    const err = new ApiError(403, "x", { code: "otro_organismo", message: "Ese organismo no es el tuyo." });
    expect(pnError(err).message).toBe("Ese organismo no es el tuyo.");
  });

  it("403 de Plataforma conserva su texto", () => {
    const err = new ApiError(403, "x", { error: "forbidden", message: "otro" });
    expect(pnError(err).message).toMatch(/solo el superadministrador/i);
  });
});
