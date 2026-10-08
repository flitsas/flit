// HU #13382 (épica #13216) — el seguimiento del lote de descarga masiva se monta en el Shell: se ve desde
// cualquier ruta, solo para quien tiene `consolidado-masivo.download` y sin tocar la barra de marca (#12237).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";
import type { LoteConsolidados } from "@/lib/api/types-consolidado-lotes";
import { useMostrarLoteDescarga } from "@/components/shared/LoteDescargaTracker";
import { Shell } from "../Shell";

// Uso de ejemplo:
//   <Shell visibleModuleCodes={modulos}>{pagina}</Shell>
//   // dentro de la página: const { mostrarLote } = useMostrarLoteDescarga(); mostrarLote(loteId);

const nav = vi.hoisted(() => ({ pathname: "/admin/companies" }));
vi.mock("next/navigation", () => ({
  usePathname: () => nav.pathname,
  useRouter: () => ({ push: vi.fn() }),
}));

/** Datos sintéticos según `LoteConsolidados` del contrato §5. */
const LOTE: LoteConsolidados = {
  id: "lote-shell",
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

const fetchMock = vi.fn();
const servidor = vi.hoisted(() => ({ actual: null as unknown, porId: null as unknown }));
const llamadasActual = () =>
  fetchMock.mock.calls.filter(([url]) => String(url).includes("/api/v1/consolidados/lotes/actual"));

beforeEach(() => {
  servidor.actual = null;
  servidor.porId = null;
  fetchMock.mockReset();
  fetchMock.mockImplementation(async (url: string) => {
    const path = new URL(String(url), "http://localhost").pathname;
    if (path === "/api/v1/consolidados/lotes/actual") {
      return servidor.actual
        ? new Response(JSON.stringify(servidor.actual), { status: 200, headers: { "Content-Type": "application/json" } })
        : new Response(null, { status: 204 });
    }
    if (path.startsWith("/api/v1/consolidados/lotes/") && servidor.porId) {
      return new Response(JSON.stringify(servidor.porId), { status: 200, headers: { "Content-Type": "application/json" } });
    }
    return new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } });
  });
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

const conPermiso = () =>
  window.localStorage.setItem(
    TOKEN_STORAGE_KEY,
    makeToken({ sub: "u1", role: "Gestor", permissions: ["consolidado-masivo.download"] }),
  );

describe("Shell + LoteDescargaTracker — HU #13382", () => {
  it("AC1 — con un lote activo, el aviso se ve en una ruta fuera de /tramites con «procesados / total»", async () => {
    conPermiso();
    servidor.actual = LOTE;
    render(
      <Shell visibleModuleCodes={["tramites"]}>
        <div>contenido</div>
      </Shell>,
    );
    const card = await screen.findByTestId("lote-descarga-card");
    expect(card).toHaveTextContent("45 / 120");
    expect(screen.getByText("contenido")).toBeInTheDocument();
  });

  it("marca blanca (#12237) — el aviso no vive en la barra de marca y el logo sigue en su sitio", async () => {
    conPermiso();
    servidor.actual = LOTE;
    render(
      <Shell visibleModuleCodes={["tramites"]}>
        <div>contenido</div>
      </Shell>,
    );
    const card = await screen.findByTestId("lote-descarga-card");
    const header = document.querySelector("header");
    expect(header).not.toBeNull();
    expect(header!.contains(card)).toBe(false);
    expect(header!.querySelector('a[aria-label*="Trámites"]')).not.toBeNull();
    expect(card.closest("main")).not.toBeNull();
  });

  it("AC5 — 204: no se pinta nada", async () => {
    conPermiso();
    render(
      <Shell visibleModuleCodes={["tramites"]}>
        <div>contenido</div>
      </Shell>,
    );
    await waitFor(() => expect(llamadasActual()).toHaveLength(1));
    expect(screen.queryByTestId("lote-descarga-card")).not.toBeInTheDocument();
  });

  it("sin consolidado-masivo.download no se monta el seguimiento (sin polling)", async () => {
    window.localStorage.setItem(TOKEN_STORAGE_KEY, makeToken({ sub: "u1", role: "Gestor", permissions: ["tramites.read"] }));
    render(
      <Shell visibleModuleCodes={["tramites"]}>
        <div>contenido</div>
      </Shell>,
    );
    await screen.findByText("contenido");
    await new Promise((r) => setTimeout(r, 20));
    expect(llamadasActual()).toHaveLength(0);
    expect(screen.queryByTestId("lote-descarga-card")).not.toBeInTheDocument();
  });

  it("useMostrarLoteDescarga — una página del Shell pide mostrar un lote concreto (409 lote_activo)", async () => {
    conPermiso();
    servidor.porId = { ...LOTE, id: "lote-409", procesados: 80 };
    function Pagina() {
      const { mostrarLote } = useMostrarLoteDescarga();
      return (
        <button type="button" onClick={() => mostrarLote("lote-409")}>
          seguir
        </button>
      );
    }
    render(
      <Shell visibleModuleCodes={["tramites"]}>
        <Pagina />
      </Shell>,
    );
    await waitFor(() => expect(llamadasActual()).toHaveLength(1));
    await userEvent.click(screen.getByRole("button", { name: "seguir" }));
    const card = await screen.findByTestId("lote-descarga-card");
    expect(card).toHaveTextContent("80 / 120");
  });

  it("useMostrarLoteDescarga fuera del Shell es inocuo (no-op)", async () => {
    function Suelta() {
      const { mostrarLote } = useMostrarLoteDescarga();
      return (
        <button type="button" onClick={() => mostrarLote("x")}>
          suelta
        </button>
      );
    }
    render(<Suelta />);
    await userEvent.click(screen.getByRole("button", { name: "suelta" }));
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
