// Feature #11939 / HU #11942 — actividad reciente y bienvenida del organismo.
//
// La gráfica del tablero del gestor prometía una tendencia que en una sesión de OT no se dibujaba
// nunca, y el carrusel anunciaba «validación de identidad con IA ya integrada en TUS trámites» a
// quien no radica trámites. Aquí se cierran las dos: la franja sale del informe del propio
// organismo, y la bienvenida lo nombra.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type {
  OtOperationalPanel,
  OtReport,
  OtReportSeriesPoint,
  OtReviewer,
} from "@/lib/api/ot-metrics";
import { OtDashboard } from "../OtDashboard";

const fetchOtProfile = vi.fn();
const fetchOtOperationalPanel = vi.fn();
const fetchOtReport = vi.fn();
const fetchOtPerformance = vi.fn();
const fetchTransitOffices = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: (...args: unknown[]) => fetchOtProfile(...args),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: (...args: unknown[]) => fetchTransitOffices(...args),
}));

vi.mock("@/lib/api/ot-metrics", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/ot-metrics")>()),
  fetchOtOperationalPanel: (...args: unknown[]) => fetchOtOperationalPanel(...args),
  fetchOtReport: (...args: unknown[]) => fetchOtReport(...args),
  fetchOtPerformance: (...args: unknown[]) => fetchOtPerformance(...args),
}));

// `ResponsiveContainer` mide el contenedor, y en jsdom todo mide 0: sin ancho no renderiza ni un
// eje. Se le fija un tamaño para que la gráfica exista en el árbol.
vi.mock("recharts", async (importOriginal) => {
  const real = await importOriginal<typeof import("recharts")>();
  return {
    ...real,
    ResponsiveContainer: ({ children }: { children: React.ReactNode }) => (
      <div style={{ width: 600, height: 220 }}>{children}</div>
    ),
  };
});

const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";

const PANEL: OtOperationalPanel = {
  movimiento: { entregadosHoy: 2, decididosHoy: 0, pendientesTotal: 5, tiempoMedianoDecisionHoras: 6 },
  cola: { porRevisar: 3, esperandoAsignarPlaca: 2, enEsperaDelCliente: 0 },
  antiguedad: {
    hasta1Dia: 2,
    entre2y3Dias: 0,
    entre4y7Dias: 3,
    masDe7Dias: 0,
    prioritariosEstancados: 0,
  },
};

function informe(serie: OtReportSeriesPoint[]): OtReport {
  return {
    resumen: {
      total: 0,
      enRevision: 0,
      esperandoPlaca: 0,
      esperandoCliente: 0,
      aprobados: 0,
      enSubsanacion: 0,
      rechazados: 0,
      anulados: 0,
      otros: 0,
      decididos: 0,
      devoluciones: 0,
      devolucionesPromedio: 0,
      tiempoMedianoHoras: null,
      tiempoPromedioHoras: null,
      tiempoP90Horas: null,
      tiempoMedianoAprobacionHoras: null,
      distribucionTiempos: [],
      granularidad: "dia",
      serie,
    },
    total: 0,
    page: 1,
    pageSize: 1,
    filas: [],
  };
}

const CON_MOVIMIENTO: OtReportSeriesPoint[] = [
  { bucket: "2026-08-30", label: "30 ago", desde: "2026-08-30", hasta: "2026-08-30", radicados: 3, aprobados: 2, rechazados: 1 },
  { bucket: "2026-08-31", label: "31 ago", desde: "2026-08-31", hasta: "2026-08-31", radicados: 1, aprobados: 0, rechazados: 0 },
];

/** Dos que decidieron y una que no: la tercera no debe ocupar un color del anillo. */
const REVISORES: OtReviewer[] = [
  {
    userId: "u-2",
    displayName: "Beto Sáenz",
    decididos: 2,
    aprobados: 2,
    aprobacionPct: 100,
    rechazados: 0,
    rechazoPct: 0,
    tiempoMedianoHoras: 4,
    vuelvenARechazarsePct: 0,
  },
  {
    userId: "u-1",
    displayName: "Ana Ruiz",
    decididos: 6,
    aprobados: 5,
    aprobacionPct: 83.3,
    rechazados: 1,
    rechazoPct: 16.7,
    tiempoMedianoHoras: 3,
    vuelvenARechazarsePct: 0,
  },
  {
    userId: "u-3",
    displayName: "Caro Díaz",
    decididos: 0,
    aprobados: 0,
    aprobacionPct: 0,
    rechazados: 0,
    rechazoPct: 0,
    tiempoMedianoHoras: null,
    vuelvenARechazarsePct: 0,
  },
];

const SIN_MOVIMIENTO: OtReportSeriesPoint[] = [
  { bucket: "2026-08-30", label: "30 ago", desde: "2026-08-30", hasta: "2026-08-30", radicados: 0, aprobados: 0, rechazados: 0 },
];

describe("OtDashboard — actividad reciente y bienvenida", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
    fetchOtProfile.mockResolvedValue({ transitOfficeId: OT_ID });
    fetchOtOperationalPanel.mockResolvedValue(PANEL);
    fetchOtReport.mockResolvedValue(informe(CON_MOVIMIENTO));
    fetchOtPerformance.mockResolvedValue({ revisores: REVISORES, empresas: [] });
    fetchTransitOffices.mockResolvedValue([
      { id: OT_ID, name: "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA", code: "11001000" },
    ]);
  });

  it("AC1 — la franja pide el informe del organismo y distingue radicado, aprobado y rechazado", async () => {
    render(<OtDashboard />);

    await waitFor(() =>
      expect(fetchOtReport).toHaveBeenCalledWith(
        expect.objectContaining({ transitOfficeId: OT_ID, pageSize: 1 }),
        expect.anything(),
      ),
    );
    expect(await screen.findByTestId("ot-inicio-actividad")).toBeInTheDocument();
    expect(screen.getByText("Actividad de los últimos 14 días")).toBeInTheDocument();
  });

  it("AC2 — el reparto entre evaluadores sale de su propia llamada al desempeño", async () => {
    render(<OtDashboard />);

    await waitFor(() =>
      expect(fetchOtPerformance).toHaveBeenCalledWith(
        expect.objectContaining({ transitOfficeId: OT_ID }),
      ),
    );
    expect(await screen.findByTestId("ot-inicio-evaluadores")).toBeInTheDocument();
    expect(screen.getByText("Quién decidió en los últimos 14 días")).toBeInTheDocument();
  });

  it("AC2 — reparte por decisiones, ordenado de mayor a menor, y no repite la cola por estado", async () => {
    render(<OtDashboard />);

    const tarjeta = await screen.findByTestId("ot-inicio-evaluadores");
    expect(tarjeta).toHaveTextContent("Ana Ruiz");
    expect(tarjeta).toHaveTextContent("Beto Sáenz");
    // 6 + 2 decisiones: el centro suma y cada fila lleva su porcentaje.
    expect(tarjeta).toHaveTextContent("Decisiones");
    expect(tarjeta).toHaveTextContent("75.0 %");
    expect(tarjeta).toHaveTextContent("25.0 %");
    expect(tarjeta).toHaveTextContent("2 evaluadores con decisiones");
    // Quien no decidió nada no ocupa un color del anillo.
    expect(tarjeta).not.toHaveTextContent("Caro Díaz");
    // Y no se vuelve a contar la cola por estado, que ya está arriba tres veces.
    expect(tarjeta).not.toHaveTextContent("Esperando placa");
    expect(tarjeta).not.toHaveTextContent("En revisión");
  });

  it("AC3 — sin decisiones en el periodo lo dice, en vez de dibujar un anillo vacío", async () => {
    fetchOtPerformance.mockResolvedValue({ revisores: [], empresas: [] });
    render(<OtDashboard />);

    expect(
      await screen.findByText("Nadie decidió trámites en los últimos 14 días."),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("ot-inicio-evaluadores")).not.toBeInTheDocument();
  });

  it("AC5 — si falla el desempeño, la actividad y la cola siguen en pie", async () => {
    fetchOtPerformance.mockRejectedValue(new Error("500"));
    render(<OtDashboard />);

    expect(
      await screen.findByText("No se pudo cargar el reparto entre evaluadores."),
    ).toBeInTheDocument();
    expect(screen.getByTestId("ot-inicio-actividad")).toBeInTheDocument();
    expect(screen.getByText("Pendientes en total")).toBeInTheDocument();
  });

  it("AC2 — sin movimiento lo dice con palabras, no con un gráfico en blanco", async () => {
    fetchOtReport.mockResolvedValue(informe(SIN_MOVIMIENTO));
    render(<OtDashboard />);

    expect(
      await screen.findByText("No hubo movimiento en los últimos 14 días."),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("ot-inicio-actividad")).not.toBeInTheDocument();
  });

  it("AC3 — la bienvenida nombra al organismo y no anuncia nada del gestor", async () => {
    render(<OtDashboard />);

    expect(
      await screen.findByText(/SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA \(11001000\)/),
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Tu cola de trabajo" })).toBeInTheDocument();
    expect(screen.queryByText(/Inteligencia Artificial/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Nueva integración/i)).not.toBeInTheDocument();
  });

  it("AC3 — el banner conserva el mecanismo de pasar mensajes, con contenido del organismo", async () => {
    render(<OtDashboard />);

    expect(await screen.findByRole("heading", { name: "Tu cola de trabajo" })).toBeInTheDocument();
    // El primer mensaje cuenta el estado real de la cola, no un texto fijo.
    expect(screen.getByText(/Tienes 3 trámites esperando tu decisión/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Mensaje siguiente" }));
    expect(
      screen.getByRole("heading", { name: "Lo que se envejece, primero" }),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Mensaje siguiente" }));
    expect(screen.getByRole("heading", { name: "Reportes del organismo" })).toBeInTheDocument();

    // El nombre del organismo NO rota: identifica de quién es la pantalla.
    expect(
      screen.getByText(/SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA \(11001000\)/),
    ).toBeInTheDocument();

    // Y se puede saltar directo a uno con su selector.
    await userEvent.click(
      screen.getByRole("button", { name: /Mensaje 1 de 3: Tu cola de trabajo/ }),
    );
    expect(screen.getByRole("heading", { name: "Tu cola de trabajo" })).toBeInTheDocument();
  });

  it("AC3 — con la cola vacía el banner no promete trabajo que no existe", async () => {
    fetchOtOperationalPanel.mockResolvedValue({
      ...PANEL,
      movimiento: { ...PANEL.movimiento, pendientesTotal: 0 },
      cola: { porRevisar: 0, esperandoAsignarPlaca: 0, enEsperaDelCliente: 0 },
    });
    render(<OtDashboard />);

    expect(
      await screen.findByText(/No tienes trámites esperando decisión en este momento/),
    ).toBeInTheDocument();
  });

  it("AC3 — si el catálogo no responde, la bienvenida degrada sin romper la pantalla", async () => {
    fetchTransitOffices.mockRejectedValue(new Error("503"));
    render(<OtDashboard />);

    expect(await screen.findByText("Organismo de tránsito")).toBeInTheDocument();
    expect(await screen.findByText("Pendientes en total")).toBeInTheDocument();
  });

  it("AC4 — un fallo de la actividad no tumba los indicadores de la cola", async () => {
    fetchOtReport.mockRejectedValue(new Error("500"));
    render(<OtDashboard />);

    expect(await screen.findByText("No se pudo cargar la actividad reciente.")).toBeInTheDocument();
    // La cola sigue en pie: es a lo que se entra a esta pantalla.
    expect(screen.getByText("Pendientes en total")).toBeInTheDocument();
    expect(screen.getByText("Por revisar")).toBeInTheDocument();
  });
});
