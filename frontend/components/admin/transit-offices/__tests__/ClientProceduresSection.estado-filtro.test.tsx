// HU #11946 — el filtro de Estado de la bandeja OT refleja el universo que el organismo recibe.
//
// El candado de verdad es del backend (HU #11945): la bandeja ya no devuelve borradores aunque se
// pidan. Lo que se prueba aquí es que la pantalla no le mienta al usuario sobre lo que muestra ni
// se quede en un estado que su propio desplegable no puede representar.
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, waitFor } from "@testing-library/react";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  searchOtClientProcedures: vi.fn(),
  fetchOtBandejaFilterFields: vi.fn(),
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
  searchOtClientProcedures,
  fetchOtBandejaFilterFields,
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
    vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([]);
    vi.mocked(searchOtClientProcedures).mockResolvedValue({
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

  /*
   * Las dos pruebas de AC1 que había aquí afirmaban sobre el <select> de Estado del formulario
   * «Búsqueda avanzada»: que la opción abierta se rotulaba «Todos los recibidos» y que la lista
   * era exactamente entregado/aprobado/rechazado/revocado.
   *
   * Ese control desapareció con la HU #12218: el estado se filtra desde el panel «Filtros», y su
   * vocabulario ya NO lo escribe el frontend — lo sirve el catálogo del backend. Afirmarlo aquí
   * sería afirmar sobre el mock del catálogo, es decir, sobre nada. La regla vive ahora donde se
   * decide, con una prueba que la ata a `TramiteEstado.RecibidosPorOrganismo`:
   * `OtBandejaFiltrosTests.AC2_LosEstadosDelCatalogo_SonLosQueLaBandejaRecibe`.
   *
   * Lo que sí sigue siendo del frontend —y sigue probado abajo— es cuál es el estado con el que la
   * bandeja abre y qué hace con el de un enlace profundo.
   */

  // AC2 — la bandeja sigue abriendo por la cola de decisión: es el trabajo pendiente del organismo.
  it("AC2 — sin parámetros en la URL, el filtro arranca en «Pendiente OT»", async () => {
    restaurarLocation = conQueryString("");
    renderSection();

    await waitFor(() => {
      expect(vi.mocked(searchOtClientProcedures).mock.calls.at(-1)?.[0]?.status).toBe("entregado");
    });
  });

  // AC3 — el caso que motiva la HU: un estado que la bandeja nunca recibe se descarta en vez de
  // sembrarse. Sembrarlo dejaría la bandeja vacía sin nada que explicara por qué, que se lee como
  // un fallo de carga y no como un filtro imposible.
  it("AC3 — un deep-link con estado no permitido cae al valor por defecto", async () => {
    restaurarLocation = conQueryString("?status=borrador");
    renderSection();

    await waitFor(() => {
      expect(vi.mocked(searchOtClientProcedures)).toHaveBeenCalled();
    });
    expect(
      vi.mocked(searchOtClientProcedures).mock.calls.every((c) => c[0]?.status !== "borrador"),
    ).toBe(true);
  });

  // AC4 — el drill-down de reportes sigue aterrizando filtrado: descartar de más costaría esa ruta.
  it("AC4 — un deep-link con un estado válido sí se aplica", async () => {
    restaurarLocation = conQueryString("?status=aprobado");
    renderSection();

    await waitFor(() => {
      expect(vi.mocked(searchOtClientProcedures).mock.calls.at(-1)?.[0]?.status).toBe("aprobado");
    });
  });
});
