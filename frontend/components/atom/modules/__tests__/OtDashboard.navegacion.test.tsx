// Feature #11939 / HU #11941 — de ver el número a trabajarlo.
//
// La cifra de un indicador no puede ser un callejón sin salida: quien lee «3 con más de 7 días»
// necesita saber CUÁLES son. Se reutiliza el mismo detalle que ya usa la consola de Reportes, y por
// el mismo motivo que documenta `DrilldownPanel`: el backend recalcula el bloque con idénticos
// predicados, así que la lista nunca contradice a la tarjeta que la abrió. Un filtro aproximado en
// la bandeja sí podría contradecirla —«pendientes en total» y los tramos de antigüedad no son un
// estado del trámite—, y por eso no se navega con un `?status=` inventado.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { OtDrilldown, OtOperationalPanel } from "@/lib/api/ot-metrics";
import { OtDashboard } from "../OtDashboard";

const fetchOtProfile = vi.fn();
const fetchOtOperationalPanel = vi.fn();
const fetchOtDrilldown = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: (...args: unknown[]) => fetchOtProfile(...args),
}));

vi.mock("@/lib/api/ot-metrics", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/ot-metrics")>()),
  fetchOtOperationalPanel: (...args: unknown[]) => fetchOtOperationalPanel(...args),
  fetchOtDrilldown: (...args: unknown[]) => fetchOtDrilldown(...args),
}));

const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";

const PANEL: OtOperationalPanel = {
  movimiento: {
    entregadosHoy: 2,
    decididosHoy: 0,
    pendientesTotal: 5,
    tiempoMedianoDecisionHoras: 6,
  },
  cola: { porRevisar: 3, esperandoAsignarPlaca: 2, enEsperaDelCliente: 0 },
  antiguedad: {
    hasta1Dia: 2,
    entre2y3Dias: 0,
    entre4y7Dias: 3,
    masDe7Dias: 0,
    prioritariosEstancados: 0,
  },
};

function drilldown(bucket: string): OtDrilldown {
  return {
    bucket,
    total: 1,
    omitidos: 0,
    items: [
      {
        procedureInstanceId: "proc-1",
        referenceNumber: "RAD-0001",
        placa: "ABC123",
        vin: null,
        clientTenantId: "tenant-1",
        clientTenantName: "Empresa Demo",
        status: "entregado",
        familia: "TRASPASO",
        tipoTramite: "Traspaso",
        prioritario: false,
        diasEsperando: 9,
      },
    ],
  };
}

async function renderReady() {
  render(<OtDashboard />);
  await waitFor(() => expect(fetchOtOperationalPanel).toHaveBeenCalled());
  await screen.findByText("Pendientes en total");
}

describe("OtDashboard — navegación desde los indicadores", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
    fetchOtProfile.mockResolvedValue({ transitOfficeId: OT_ID });
    fetchOtOperationalPanel.mockResolvedValue(PANEL);
    fetchOtDrilldown.mockImplementation((_p: unknown, bucket: string) =>
      Promise.resolve(drilldown(bucket)),
    );
  });

  it("AC1 — el indicador de lo que espera mi decisión abre esos trámites", async () => {
    await renderReady();

    await userEvent.click(
      screen.getByRole("button", { name: /Ver los 3 trámites: esperan mi decisión/i }),
    );

    await waitFor(() =>
      expect(fetchOtDrilldown).toHaveBeenCalledWith(
        expect.objectContaining({ transitOfficeId: OT_ID }),
        "por_revisar",
      ),
    );
    expect(await screen.findByText("RAD-0001")).toBeInTheDocument();
  });

  it("AC2 — los tramos de antigüedad abren su propia lista", async () => {
    await renderReady();

    await userEvent.click(
      screen.getByRole("button", { name: /Ver los 3 trámites pendientes de 4–7 días/i }),
    );

    await waitFor(() =>
      expect(fetchOtDrilldown).toHaveBeenCalledWith(expect.anything(), "antiguedad_4_7"),
    );
  });

  it("AC3 — un indicador en cero no se ofrece como enlace", async () => {
    await renderReady();

    // «En espera del cliente» y «Más de 7 días» valen 0 en este panel.
    expect(screen.queryByRole("button", { name: /en espera del cliente/i })).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /pendientes de más de 7 días/i }),
    ).not.toBeInTheDocument();
    // Pero sí siguen mostrándose como dato.
    expect(screen.getByText("En espera del cliente")).toBeInTheDocument();
    expect(screen.getByText("Más de 7 días")).toBeInTheDocument();
  });

  it("AC3 — la mediana de decisión no es un conjunto de trámites y no navega", async () => {
    await renderReady();

    expect(
      screen.queryByRole("button", { name: /tiempo mediano de decisión/i }),
    ).not.toBeInTheDocument();
  });

  it("AC4 — los indicadores navegables se alcanzan y activan con el teclado", async () => {
    await renderReady();

    const objetivo = screen.getByRole("button", {
      name: /Ver los 3 trámites: esperan mi decisión/i,
    });
    objetivo.focus();
    expect(objetivo).toHaveFocus();

    await userEvent.keyboard("{Enter}");
    await waitFor(() => expect(fetchOtDrilldown).toHaveBeenCalledWith(expect.anything(), "por_revisar"));
  });

  it("AC5 — la lista enlaza a la bandeja del organismo de la sesión", async () => {
    await renderReady();

    await userEvent.click(
      screen.getByRole("button", { name: /Ver los 5 trámites: pendientes en total/i }),
    );

    const ir = await screen.findByRole("link", { name: /Ir a gestionar/i });
    expect(ir).toHaveAttribute(
      "href",
      `/admin/transit-offices/${OT_ID}/client-procedures?placa=ABC123&status=entregado`,
    );
  });
});
