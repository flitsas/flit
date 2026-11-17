// Feature #11939 (HU #11940, #11941) y rediseño del Inicio en 3 dashboards (2026-11-17) — franja 1 del organismo:
// carrusel + las 4 cifras de la cola.
//
// El defecto que cierra esta cobertura no era un conteo mal hecho: el inicio consultaba `/analytics/*`, que filtra por
// el tenant de quien llama, y el organismo no tiene trámites en su propio tenant. Volvían 200 y vacías. Por eso se fija
// tanto DE DÓNDE salen los datos como que un fallo no se disfrace de cero, y que cada cifra abra su lista.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { OtDrilldown, OtOperationalPanel, OtReportSummary } from "@/lib/api/ot-metrics";
import { OtDashboard } from "../OtDashboard";

const tokenHolder = vi.hoisted(() => ({ current: null as string | null }));

vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return { ...actual, getToken: () => tokenHolder.current };
});

const fetchOtProfile = vi.fn();
const fetchOtOperationalPanel = vi.fn();
const fetchOtDrilldown = vi.fn();
const fetchOtReport = vi.fn();
const fetchOtRejectionReasons = vi.fn();
const fetchOtClientsByFamily = vi.fn();
const fetchTransitOffices = vi.fn();
const fetchAnalyticsOverview = vi.fn();
const fetchMonthlyTrend = vi.fn();
const getActiveBanners = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: (...args: unknown[]) => fetchOtProfile(...args),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: (...args: unknown[]) => fetchTransitOffices(...args),
}));

vi.mock("@/lib/api/ot-metrics", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/ot-metrics")>()),
  fetchOtOperationalPanel: (...args: unknown[]) => fetchOtOperationalPanel(...args),
  fetchOtDrilldown: (...args: unknown[]) => fetchOtDrilldown(...args),
  fetchOtReport: (...args: unknown[]) => fetchOtReport(...args),
  fetchOtRejectionReasons: (...args: unknown[]) => fetchOtRejectionReasons(...args),
  fetchOtClientsByFamily: (...args: unknown[]) => fetchOtClientsByFamily(...args),
}));

// Las métricas del gestor se doblan para poder AFIRMAR que no se piden.
vi.mock("@/lib/api/analytics", () => ({
  fetchAnalyticsOverview: (...args: unknown[]) => fetchAnalyticsOverview(...args),
  fetchMonthlyTrend: (...args: unknown[]) => fetchMonthlyTrend(...args),
}));

vi.mock("@/lib/api/public-banners", () => ({
  getActiveBanners: (...args: unknown[]) => getActiveBanners(...args),
  bannerImageUrl: (id: string) => `http://api.test/api/v1/public/banners/${id}/image`,
}));

const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";

function panel(overrides: Partial<OtOperationalPanel> = {}): OtOperationalPanel {
  return {
    movimiento: { entregadosHoy: 2, decididosHoy: 0, pendientesTotal: 5, tiempoMedianoDecisionHoras: 36 },
    cola: { porRevisar: 3, esperandoAsignarPlaca: 2, enEsperaDelCliente: 0, prioritarios: 4 },
    antiguedad: { hasta1Dia: 2, entre2y3Dias: 0, entre4y7Dias: 3, masDe7Dias: 0, prioritariosEstancados: 1 },
    ...overrides,
  };
}

const RESUMEN_VACIO: OtReportSummary = {
  total: 0,
  enRevision: 0,
  esperandoPlaca: 0,
  esperandoCliente: 0,
  aprobados: 0,
  enSubsanacion: 0,
  rechazados: 0,
  anulados: 0,
  revocados: 0,
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
  serie: [],
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

async function renderReady(datos: OtOperationalPanel = panel()) {
  fetchOtOperationalPanel.mockResolvedValue(datos);
  render(<OtDashboard />);
  await waitFor(() => expect(fetchOtOperationalPanel).toHaveBeenCalled());
  await screen.findByText("Radicados ingresados hoy");
  return datos;
}

function kpis() {
  return within(screen.getByTestId("ot-inicio-kpis"));
}

describe("OtDashboard — franja 1: carrusel y cifras de la cola", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    tokenHolder.current = null;
    // `resolveOtTransitOfficeId` cachea el id en sessionStorage; sin limpiarlo el siguiente test mediría la caché.
    window.sessionStorage.clear();
    fetchOtProfile.mockResolvedValue({ transitOfficeId: OT_ID });
    fetchTransitOffices.mockResolvedValue([]);
    getActiveBanners.mockResolvedValue([]);
    fetchOtReport.mockResolvedValue({ resumen: RESUMEN_VACIO, total: 0, page: 1, pageSize: 1, filas: [] });
    fetchOtRejectionReasons.mockResolvedValue({
      causales: [],
      totalRechazos: 0,
      rechazosSinCausal: 0,
      promedioCausalesPorRechazo: 0,
    });
    fetchOtClientsByFamily.mockResolvedValue({ clientes: [], total: 0 });
    fetchOtDrilldown.mockImplementation((_p: unknown, bucket: string) => Promise.resolve(drilldown(bucket)));
  });

  it("AC1 — se alimenta de las métricas del organismo y no de las del gestor", async () => {
    await renderReady();

    expect(fetchOtOperationalPanel).toHaveBeenCalledWith(expect.objectContaining({ transitOfficeId: OT_ID }));
    expect(fetchAnalyticsOverview).not.toHaveBeenCalled();
    expect(fetchMonthlyTrend).not.toHaveBeenCalled();
    expect(screen.queryByText(/Validaciones Biom/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Seguimiento/i)).not.toBeInTheDocument();
  });

  it("muestra las 4 cifras del prototipo con los datos de la cola", async () => {
    await renderReady();

    const k = kpis();
    expect(k.getByText("Pendientes de hoy")).toBeInTheDocument();
    expect(k.getByText("Trámites prioritarios")).toBeInTheDocument();
    expect(k.getByText("Sin gestión más de 7 días")).toBeInTheDocument();
    expect(k.getByText("Por asignar / preasignar placa")).toBeInTheDocument();
    // Sin SLA ni meta en el dominio: ninguna cifra habla de SLA.
    expect(screen.queryByText(/SLA/)).not.toBeInTheDocument();
    expect(k.getByRole("button", { name: "Ver los 2 trámites: pendientes de hoy" })).toBeInTheDocument();
    expect(k.getByRole("button", { name: "Ver los 4 trámites: trámites prioritarios" })).toBeInTheDocument();
    expect(k.getByRole("button", { name: "Ver los 2 trámites: por asignar / preasignar placa" })).toBeInTheDocument();
  });

  it("quita lo que salió del Inicio: antigüedad, actividad de 14 días y el desglose de la espera", async () => {
    await renderReady();

    expect(screen.queryByText("Antigüedad de lo pendiente")).not.toBeInTheDocument();
    expect(screen.queryByText(/Actividad de los últimos/)).not.toBeInTheDocument();
    expect(screen.queryByText("En qué está esperando cada trámite")).not.toBeInTheDocument();
  });

  it("cada cifra abre su propia lista con los parámetros de la cola", async () => {
    await renderReady();

    await userEvent.click(kpis().getByRole("button", { name: /trámites prioritarios/i }));
    await waitFor(() =>
      expect(fetchOtDrilldown).toHaveBeenCalledWith(expect.objectContaining({ transitOfficeId: OT_ID }), "prioritarios"),
    );
    expect(await screen.findByRole("dialog", { name: "Trámites prioritarios" })).toBeInTheDocument();
  });

  it("una cifra en cero no se ofrece como enlace", async () => {
    await renderReady();

    // masDe7Dias = 0 en el panel de prueba.
    expect(kpis().queryByRole("button", { name: /sin gestión más de 7 días/i })).not.toBeInTheDocument();
  });

  it("las cifras navegables se alcanzan y activan con el teclado", async () => {
    await renderReady();

    const objetivo = kpis().getByRole("button", { name: /pendientes de hoy/i });
    objetivo.focus();
    expect(objetivo).toHaveFocus();
    await userEvent.keyboard("{Enter}");
    await waitFor(() => expect(fetchOtDrilldown).toHaveBeenCalledWith(expect.anything(), "entregados_hoy"));
  });

  it("la lista del detalle enlaza a la bandeja del organismo", async () => {
    await renderReady();

    await userEvent.click(kpis().getByRole("button", { name: /por asignar/i }));
    const dialogo = await screen.findByRole("dialog");
    const ir = await within(dialogo).findByRole("link", { name: /Ir a gestionar/i });
    expect(ir).toHaveAttribute("href", `/admin/transit-offices/${OT_ID}/client-procedures?placa=ABC123&status=entregado`);
  });

  it("el carrusel lleva el antetítulo, el nombre del organismo y el mensaje real de la cola en días", async () => {
    fetchTransitOffices.mockResolvedValue([
      { id: OT_ID, name: "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA", code: "11001000" },
    ]);
    await renderReady(panel({ antiguedad: { ...panel().antiguedad, masDe7Dias: 2 } }));

    const carrusel = screen.getByTestId("ot-inicio-carrusel");
    expect(within(carrusel).getByText("Control de la operación")).toBeInTheDocument();
    expect(
      await within(carrusel).findByRole("heading", { name: "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA (11001000)" }),
    ).toBeInTheDocument();
    expect(
      within(carrusel).getByText(
        "Tienes 3 trámites esperando tu decisión, 2 llevan más de 7 días esperando. Tu mediana de decisión de los últimos 30 días es de 1,5 días. Los datos son del día calendario de Bogotá.",
      ),
    ).toBeInTheDocument();
  });

  it("sin catálogo de organismos, el slide degrada al título genérico", async () => {
    fetchTransitOffices.mockRejectedValue(new Error("caído"));
    await renderReady();

    expect(within(screen.getByTestId("ot-inicio-carrusel")).getByRole("heading", { name: "Tu cola de trabajo" })).toBeInTheDocument();
  });

  it("con la cola vacía el carrusel no promete trabajo que no existe", async () => {
    await renderReady(panel({ cola: { porRevisar: 0, esperandoAsignarPlaca: 0, enEsperaDelCliente: 0, prioritarios: 0 } }));

    expect(
      within(screen.getByTestId("ot-inicio-carrusel")).getByText(/No tienes trámites esperando decisión en este momento\./),
    ).toBeInTheDocument();
  });

  it("los banners Activos se suman al carrusel detrás del slide fijo", async () => {
    getActiveBanners.mockResolvedValue([{ id: "b1", name: "Banner del organismo", linkUrl: null }]);
    await renderReady();

    await waitFor(() =>
      expect(within(screen.getByTestId("ot-inicio-carrusel")).getAllByRole("button", { name: /^Slide \d/ })).toHaveLength(2),
    );
  });

  it("un fallo de carga se ve como error y ofrece reintentar, no como ceros", async () => {
    fetchOtOperationalPanel.mockRejectedValueOnce(new Error("Servicio no disponible"));
    render(<OtDashboard />);

    expect(await screen.findByText("No se pudo cargar el estado de la cola")).toBeInTheDocument();
    expect(screen.getByText("Servicio no disponible")).toBeInTheDocument();
    expect(screen.queryByTestId("ot-inicio-kpis")).not.toBeInTheDocument();

    fetchOtOperationalPanel.mockResolvedValue(panel());
    await userEvent.click(screen.getByRole("button", { name: /Reintentar/i }));
    expect(await screen.findByText("Radicados ingresados hoy")).toBeInTheDocument();
  });

  it("Bug #14010 H-08: sin ot.reportes.manage no pide nada ni muestra un error de carga", async () => {
    const body = Buffer.from(
      JSON.stringify({
        sub: "u1",
        entity_type: "TRANSIT_OFFICE",
        roles: [{ code: "gestor_tramites_ot" }],
        permissions: ["ot.tramites.manage"],
      }),
    ).toString("base64url");
    tokenHolder.current = `e30.${body}.x`;
    render(<OtDashboard />);

    expect(await screen.findByRole("status")).toHaveTextContent("No tienes permiso para ver esta información.");
    expect(fetchOtOperationalPanel).not.toHaveBeenCalled();
    expect(fetchOtReport).not.toHaveBeenCalled();
    expect(fetchOtClientsByFamily).not.toHaveBeenCalled();
    expect(screen.queryByTestId("rango-fechas")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Reintentar/i })).not.toBeInTheDocument();
  });
});
