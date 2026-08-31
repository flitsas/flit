// Feature #11939 / HU #11940 — la vista inicial de una sesión de organismo de tránsito.
//
// El defecto que cierra esta cobertura no era un conteo mal hecho: el inicio consultaba
// `/analytics/*`, que filtra por el tenant de quien llama, y el organismo no tiene trámites en su
// propio tenant. Volvían 200 y vacías, y la pantalla mostraba cuatro ceros indistinguibles de una
// cola sana. Por eso aquí se fija tanto DE DÓNDE salen los datos como que un fallo no se disfrace
// de cero.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { OtOperationalPanel } from "@/lib/api/ot-metrics";
import { OtDashboard } from "../OtDashboard";

const fetchOtProfile = vi.fn();
const fetchOtOperationalPanel = vi.fn();
const fetchAnalyticsOverview = vi.fn();
const fetchMonthlyTrend = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: (...args: unknown[]) => fetchOtProfile(...args),
}));

// Mock parcial: `report-columns` —de donde sale `formatHours`— también importa constantes de este
// módulo, y un mock total las borraría.
vi.mock("@/lib/api/ot-metrics", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/ot-metrics")>()),
  fetchOtOperationalPanel: (...args: unknown[]) => fetchOtOperationalPanel(...args),
}));

// Las métricas del gestor se doblan para poder AFIRMAR que no se piden, que es el AC1.
vi.mock("@/lib/api/analytics", () => ({
  fetchAnalyticsOverview: (...args: unknown[]) => fetchAnalyticsOverview(...args),
  fetchMonthlyTrend: (...args: unknown[]) => fetchMonthlyTrend(...args),
}));

const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";

function panel(overrides: Partial<OtOperationalPanel> = {}): OtOperationalPanel {
  return {
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
    ...overrides,
  };
}

/** Espera a que el panel deje de mostrar el guion de carga. */
async function renderReady(datos: OtOperationalPanel = panel()) {
  fetchOtOperationalPanel.mockResolvedValue(datos);
  render(<OtDashboard />);
  await waitFor(() => expect(fetchOtOperationalPanel).toHaveBeenCalled());
  return datos;
}

describe("OtDashboard — vista inicial del organismo", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // `resolveOtTransitOfficeId` cachea el id en sessionStorage; sin limpiarlo, el segundo test
    // nunca pediría el perfil y la aserción de AC1 quedaría midiendo la caché.
    window.sessionStorage.clear();
    fetchOtProfile.mockResolvedValue({ transitOfficeId: OT_ID });
  });

  it("AC1 — se alimenta de las métricas del organismo y no de las del gestor", async () => {
    await renderReady();

    expect(fetchOtOperationalPanel).toHaveBeenCalledWith(
      expect.objectContaining({ transitOfficeId: OT_ID }),
    );
    expect(fetchAnalyticsOverview).not.toHaveBeenCalled();
    expect(fetchMonthlyTrend).not.toHaveBeenCalled();
    // Vocabulario del gestor que esta pantalla no debe volver a mostrar.
    expect(screen.queryByText(/Validaciones Biom/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/^Matrículas$/)).not.toBeInTheDocument();
    expect(screen.queryByText(/^Traspasos$/)).not.toBeInTheDocument();
  });

  it("AC2 — muestra los indicadores propios y declara la ventana de la mediana", async () => {
    await renderReady();

    const decision = await screen.findByText("Esperan mi decisión");
    expect(decision.parentElement).toHaveTextContent("3");
    expect(screen.getByText("Pendientes en total").parentElement).toHaveTextContent("5");
    expect(screen.getByText("Entregados hoy").parentElement).toHaveTextContent("2");
    // La ventana se dice en la tarjeta: el usuario no la eligió, así que no puede deducirla.
    expect(screen.getByText("Últimos 30 días")).toBeInTheDocument();
    expect(screen.getByText("Tiempo mediano de decisión").parentElement).toHaveTextContent("6 h");
  });

  it("AC3 — desglosa la cola separando lo que espera al organismo", async () => {
    await renderReady();

    expect(await screen.findByText("Por revisar")).toBeInTheDocument();
    expect(screen.getByText("Esperando asignar placa")).toBeInTheDocument();
    expect(screen.getByText("En espera del cliente")).toBeInTheDocument();
    expect(
      screen.getByText("Solo «Por revisar» espera una acción del organismo."),
    ).toBeInTheDocument();
  });

  it("AC4 — el tramo de más de 7 días solo se resalta cuando tiene trámites", async () => {
    await renderReady();

    // El tramo puede ser un <div> (en cero, no navegable) o un <button> (con contenido); la clase
    // de alarma se busca en el contenedor del tramo sea cual sea.
    const enCero = (await screen.findByText("Más de 7 días")).closest("[class*='rounded-xl']");
    expect(enCero?.className).not.toContain("FF4E00");

    fetchOtOperationalPanel.mockResolvedValue(
      panel({
        antiguedad: {
          hasta1Dia: 0,
          entre2y3Dias: 0,
          entre4y7Dias: 0,
          masDe7Dias: 4,
          prioritariosEstancados: 0,
        },
      }),
    );
    render(<OtDashboard />);

    await waitFor(() => expect(screen.getAllByText("Más de 7 días")).toHaveLength(2));
    const conAtraso = screen.getAllByText("Más de 7 días")[1].closest("[class*='rounded-xl']");
    expect(conAtraso?.className).toContain("FF4E00");
  });

  it("AC5 — sin pendientes lo dice, en vez de mostrar una fila de ceros", async () => {
    await renderReady(
      panel({
        movimiento: {
          entregadosHoy: 0,
          decididosHoy: 0,
          pendientesTotal: 0,
          tiempoMedianoDecisionHoras: null,
        },
        cola: { porRevisar: 0, esperandoAsignarPlaca: 0, enEsperaDelCliente: 0 },
      }),
    );

    expect(
      await screen.findByText("No hay trámites pendientes en este momento."),
    ).toBeInTheDocument();
    // Los bloques de cola y antigüedad no se dibujan vacíos.
    expect(screen.queryByText("Por revisar")).not.toBeInTheDocument();
    expect(screen.queryByText("Más de 7 días")).not.toBeInTheDocument();
  });

  it("AC5 — un fallo de carga se ve como error y ofrece reintentar, no como ceros", async () => {
    fetchOtOperationalPanel.mockRejectedValueOnce(new Error("503 Service Unavailable"));
    render(<OtDashboard />);

    const aviso = await screen.findByRole("alert");
    expect(aviso).toHaveTextContent("No se pudo cargar el estado de la cola");
    expect(aviso).toHaveTextContent("503 Service Unavailable");
    // Ni un solo indicador en pantalla: un cero aquí se leería como buena noticia.
    expect(screen.queryByText("Pendientes en total")).not.toBeInTheDocument();

    fetchOtOperationalPanel.mockResolvedValue(panel());
    await userEvent.click(screen.getByRole("button", { name: /Reintentar/i }));

    expect(await screen.findByText("Pendientes en total")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
