// HU #11946 — el filtro de Estado de la bandeja OT refleja el universo que el organismo recibe.
//
// El candado de verdad es del backend (HU #11945): la bandeja ya no devuelve borradores aunque se
// pidan. Lo que se prueba aquí es que la pantalla no le mienta al usuario sobre lo que muestra ni
// se quede en un estado que su propio desplegable no puede representar.
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

vi.mock("@/lib/api/admin-mandate-signers", () => ({
  fetchMandateSigners: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPublishedProcedureTypes: vi.fn().mockResolvedValue([]),
  },
}));

import {
  fetchOtBandejaHealth,
  fetchOtClientProcedures,
  fetchOtProfile,
} from "@/lib/api/admin-ot";

const OT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

function renderSection() {
  return render(
    <ToastProvider>
      <ClientProceduresSection transitOfficeId={OT_ID} />
    </ToastProvider>,
  );
}

/** Deja `window.location.search` en el valor pedido durante la prueba. */
function conQueryString(query: string) {
  const original = window.location;
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: { ...original, search: query },
  });
  return () =>
    Object.defineProperty(window, "location", {
      configurable: true,
      writable: true,
      value: original,
    });
}

/**
 * Devuelve el desplegable de Estado, abriendo el panel solo si hace falta: un deep-link con
 * filtros lo abre por su cuenta, y en ese caso pulsar el botón lo cerraría.
 */
async function abrirFiltros() {
  const yaAbierto = screen.queryByRole("combobox", { name: "Filtrar por estado" });
  if (yaAbierto) return yaAbierto;

  // Nombre exacto: con filtros aplicados el botón pasa a llamarse "Filtros activos".
  await userEvent.click(await screen.findByRole("button", { name: /^Filtros/ }));
  return screen.getByRole("combobox", { name: "Filtrar por estado" });
}

describe("ClientProceduresSection — filtro de estado (HU #11946)", () => {
  let restaurarLocation: (() => void) | null = null;

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: OT_ID,
      featureFlags: [],
    });
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
    });
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: OT_ID,
      deliveredTotal: 0,
      deliveredWithGrant: 0,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
  });

  afterEach(() => {
    restaurarLocation?.();
    restaurarLocation = null;
  });

  // AC1 — «Todos» dejó de ser cierto: el organismo nunca ve el universo completo de sus empresas.
  it("AC1 — la opción abierta se rotula «Todos los recibidos» y sigue sin aplicar filtro", async () => {
    renderSection();
    const select = await abrirFiltros();

    // Acotado al select de Estado: el de «Tipo de trámite» tiene su propia opción «Todos».
    const estado = within(select);
    const abierta = estado.getByRole("option", { name: "Todos los recibidos" });
    expect((abierta as HTMLOptionElement).value).toBe("");
    expect(estado.queryByRole("option", { name: "Todos" })).not.toBeInTheDocument();

    await userEvent.selectOptions(select, "");

    await waitFor(() => {
      expect(vi.mocked(fetchOtClientProcedures).mock.calls.at(-1)?.[0].status).toBeUndefined();
    });
  });

  // AC1 (contrato) — el desplegable ofrece exactamente los tres estados recibidos, y ninguno de
  // los que el backend ya no sirve.
  it("AC1 — el desplegable no ofrece borrador, preparado ni anulado", async () => {
    renderSection();
    const estado = within(await abrirFiltros());

    expect(estado.getByRole("option", { name: "Pendiente OT" })).toBeInTheDocument();
    expect(estado.getByRole("option", { name: "Aprobado OT" })).toBeInTheDocument();
    expect(estado.getByRole("option", { name: "Rechazado OT" })).toBeInTheDocument();

    // Lista cerrada: cualquier estado nuevo tiene que añadirse aquí a conciencia.
    const valores = estado
      .getAllByRole("option")
      .map((o) => (o as HTMLOptionElement).value);
    expect(valores).toEqual(["entregado", "aprobado", "rechazado", ""]);
  });

  // AC2 — la bandeja sigue abriendo por la cola de decisión: es el trabajo pendiente del organismo.
  it("AC2 — sin parámetros en la URL, el filtro arranca en «Pendiente OT»", async () => {
    restaurarLocation = conQueryString("");
    renderSection();

    await waitFor(() => {
      expect(vi.mocked(fetchOtClientProcedures).mock.calls.at(-1)?.[0].status).toBe("entregado");
    });

    const select = await abrirFiltros();
    expect((select as HTMLSelectElement).value).toBe("entregado");
  });

  // AC3 — el caso que motiva la HU: sin este descarte el <select> se queda en "borrador", un valor
  // que ninguna <option> tiene. El desplegable se ve en blanco junto a una lista vacía y parece un
  // fallo de carga, no un filtro imposible.
  it("AC3 — un deep-link con estado no permitido cae al valor por defecto", async () => {
    restaurarLocation = conQueryString("?status=borrador");
    renderSection();

    await waitFor(() => {
      expect(vi.mocked(fetchOtClientProcedures)).toHaveBeenCalled();
    });
    expect(
      vi.mocked(fetchOtClientProcedures).mock.calls.every((c) => c[0].status !== "borrador"),
    ).toBe(true);

    const select = await abrirFiltros();
    expect((select as HTMLSelectElement).value).toBe("entregado");
  });

  // AC4 — el drill-down de reportes sigue aterrizando filtrado: descartar de más costaría esa ruta.
  it("AC4 — un deep-link con un estado válido sí se aplica", async () => {
    restaurarLocation = conQueryString("?status=aprobado");
    renderSection();

    await waitFor(() => {
      expect(vi.mocked(fetchOtClientProcedures).mock.calls.at(-1)?.[0].status).toBe("aprobado");
    });

    const select = await abrirFiltros();
    expect((select as HTMLSelectElement).value).toBe("aprobado");
  });
});
