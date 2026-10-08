// HU #13388 (épica #13216) — «Cancelar» vive en el aviso global del Shell: sale igual en /tramites y en la
// consola del OT (/admin/transit-offices), y un clic manda la cancelación sin diálogo de confirmación.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import type { LoteConsolidados } from "@/lib/api/types-consolidado-lotes";
import { Shell } from "../Shell";

// Uso de ejemplo:
//   <Shell visibleModuleCodes={modulos}>{pagina}</Shell>
//   // el aviso del lote en curso trae «Cancelar» (nombre accesible «Cancelar la descarga»)

const nav = vi.hoisted(() => ({ pathname: "/tramites" }));
vi.mock("next/navigation", () => ({
  usePathname: () => nav.pathname,
  useRouter: () => ({ push: vi.fn() }),
}));

/** Datos sintéticos según `LoteConsolidados` del contrato. */
const LOTE: LoteConsolidados = {
  id: "lote-shell-cancelar",
  estado: "en_proceso",
  tipoDocumento: "consolidado",
  total: 120,
  procesados: 45,
  incluidos: 45,
  omitidos: 0,
  creadoEn: "2026-10-07T15:00:00Z",
  terminadoEn: null,
  expiraEn: null,
  partes: [],
};

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}
const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const fetchMock = vi.fn();
const servidor = vi.hoisted(() => ({ lote: null as unknown }));
const posts = () =>
  fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method === "POST");

beforeEach(() => {
  servidor.lote = LOTE;
  fetchMock.mockReset();
  // Mock del contrato §4 (#13307): el backend #13385 aún no existe.
  fetchMock.mockImplementation(async (url: string, init?: RequestInit) => {
    const path = new URL(String(url), "http://localhost").pathname;
    if (path === `/api/v1/consolidados/lotes/${LOTE.id}/cancelacion` && init?.method === "POST") {
      const fin = new Date().toISOString();
      servidor.lote = { ...LOTE, estado: "cancelado", terminadoEn: fin, expiraEn: fin, partes: [] };
      return json(202, servidor.lote);
    }
    if (path === "/api/v1/consolidados/lotes/actual") return json(200, servidor.lote);
    return new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } });
  });
  vi.stubGlobal("fetch", fetchMock);
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({ sub: "u1", role: "Gestor", permissions: ["consolidado-masivo.download"] }),
  );
});
afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

describe("Shell + LoteDescargaTracker — cancelar (HU #13388)", () => {
  it.each(["/tramites", "/admin/transit-offices"])(
    "AC1/AC2/AC3 — en %s el aviso trae «Cancelar»; un clic cancela y muestra «Descarga cancelada»",
    async (ruta) => {
      nav.pathname = ruta;
      render(
        <Shell visibleModuleCodes={["tramites"]}>
          <div>contenido</div>
        </Shell>,
      );
      const boton = await screen.findByRole("button", { name: "Cancelar la descarga" });
      await userEvent.click(boton);

      expect(await screen.findByText("Descarga cancelada")).toBeInTheDocument();
      expect(posts()).toHaveLength(1);
      expect(new URL(String(posts()[0][0]), "http://localhost").pathname).toBe(
        `/api/v1/consolidados/lotes/${LOTE.id}/cancelacion`,
      );
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Cancelar la descarga" })).not.toBeInTheDocument();
      expect(screen.getByTestId("lote-descarga-card")).toHaveAttribute("data-tone", "neutral");
    },
  );
});
