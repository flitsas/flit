// Feature #11939 / HU #11942 — actividad reciente y bienvenida del organismo.
//
// La gráfica del tablero del gestor prometía una tendencia que en una sesión de OT no se dibujaba
// nunca, y el carrusel anunciaba «validación de identidad con IA ya integrada en TUS trámites» a
// quien no radica trámites. Aquí se cierran las dos: la franja sale del informe del propio
// organismo, y la bienvenida lo nombra.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { OtOperationalPanel, OtReport, OtReportSeriesPoint } from "@/lib/api/ot-metrics";
import { OtDashboard } from "../OtDashboard";

const fetchOtProfile = vi.fn();
const fetchOtOperationalPanel = vi.fn();
const fetchOtReport = vi.fn();
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

function informe(
  serie: OtReportSeriesPoint[],
  composicion: Partial<OtReport["resumen"]> = {},
): OtReport {
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
      ...composicion,
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

  it("AC2 — el desglose de lo recibido sale de la MISMA llamada, sin pedir el informe dos veces", async () => {
    fetchOtReport.mockResolvedValue(
      informe(CON_MOVIMIENTO, { aprobados: 4, esperandoPlaca: 1, rechazados: 2, enRevision: 3 }),
    );
    render(<OtDashboard />);

    expect(await screen.findByTestId("ot-inicio-composicion")).toBeInTheDocument();
    expect(screen.getByTestId("ot-inicio-actividad")).toBeInTheDocument();
    // Dos tarjetas, una sola petición: la serie y el desglose vienen juntos en el informe.
    expect(fetchOtReport).toHaveBeenCalledTimes(1);
  });

  it("AC2 — agrupa los ocho estados del OT en aprobados, rechazados, sin decisión y anulados", async () => {
    fetchOtReport.mockResolvedValue(
      informe(CON_MOVIMIENTO, {
        aprobados: 4,
        esperandoPlaca: 1, // expediente ya aprobado: cuenta como aprobado
        rechazados: 1,
        enSubsanacion: 1, // rechazo con subsanación abierta: sigue siendo rechazo
        enRevision: 2,
        esperandoCliente: 1, // sin veredicto todavía
        anulados: 0,
      }),
    );
    render(<OtDashboard />);

    const tarjeta = await screen.findByTestId("ot-inicio-composicion");
    // 4+1 aprobados, 1+1 rechazados, 2+1 sin decisión → 10 recibidos.
    expect(tarjeta).toHaveTextContent("Aprobados");
    expect(tarjeta).toHaveTextContent("Rechazados");
    expect(tarjeta).toHaveTextContent("Sin decisión");
    expect(tarjeta).toHaveTextContent("10 recibidos en total");
    // El centro es el % de aprobados: 5 de 10.
    expect(tarjeta).toHaveTextContent("50.0 %");
    expect(tarjeta).toHaveTextContent("20.0 %"); // rechazados
    expect(tarjeta).toHaveTextContent("30.0 %"); // sin decisión
    // Un grupo en cero no ocupa un color del anillo.
    expect(tarjeta).not.toHaveTextContent("Anulados");
  });

  it("AC3 — sin nada recibido, el desglose lo dice en vez de dibujar un anillo vacío", async () => {
    fetchOtReport.mockResolvedValue(informe(CON_MOVIMIENTO));
    render(<OtDashboard />);

    expect(
      await screen.findByText("No se recibió ningún trámite en los últimos 14 días."),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("ot-inicio-composicion")).not.toBeInTheDocument();
  });

  it("AC3 — sin movimiento lo dice con palabras, no con un gráfico en blanco", async () => {
    fetchOtReport.mockResolvedValue(informe(SIN_MOVIMIENTO));
    render(<OtDashboard />);

    expect(
      await screen.findByText("No hubo movimiento en los últimos 14 días."),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("ot-inicio-actividad")).not.toBeInTheDocument();
  });

  it("AC4 — la bienvenida nombra al organismo y no anuncia nada del gestor", async () => {
    render(<OtDashboard />);

    expect(
      await screen.findByText(/SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA \(11001000\)/),
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Tu cola de trabajo" })).toBeInTheDocument();
    expect(screen.queryByText(/Inteligencia Artificial/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Nueva integración/i)).not.toBeInTheDocument();
  });

  it("AC4 — el banner conserva el mecanismo de pasar mensajes, con contenido del organismo", async () => {
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

  it("AC4 — con la cola vacía el banner no promete trabajo que no existe", async () => {
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

  it("AC4 — si el catálogo no responde, la bienvenida degrada sin romper la pantalla", async () => {
    fetchTransitOffices.mockRejectedValue(new Error("503"));
    render(<OtDashboard />);

    expect(await screen.findByText("Organismo de tránsito")).toBeInTheDocument();
    expect(await screen.findByText("Pendientes en total")).toBeInTheDocument();
  });

  it("AC5 — un fallo de la actividad no tumba los indicadores de la cola", async () => {
    fetchOtReport.mockRejectedValue(new Error("500"));
    render(<OtDashboard />);

    expect(await screen.findByText("No se pudo cargar la actividad reciente.")).toBeInTheDocument();
    expect(screen.getByText("No se pudo cargar la composición del periodo.")).toBeInTheDocument();
    // La cola sigue en pie: es a lo que se entra a esta pantalla.
    expect(screen.getByText("Pendientes en total")).toBeInTheDocument();
    expect(screen.getByText("Por revisar")).toBeInTheDocument();
  });
});
