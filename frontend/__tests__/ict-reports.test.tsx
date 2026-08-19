// IctReports (HU #11619): módulo nuevo "Reportes ICT" — pestañas de reportes en vivo
// (Novedades/Atascados/Jobs/Webhooks), Consultas personalizadas (HU #11610) y Programación
// (movidas aquí desde IctLogs), con la pestaña "Jobs" restringida a SuperAdmin.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({
  isSuperAdmin: vi.fn(() => false),
  fetchIctQueryFields: vi.fn(),
  fetchIctSavedQueries: vi.fn(),
  runIctQuery: vi.fn(),
  fetchIctNovedadesReport: vi.fn(),
  fetchIctAtascadosReport: vi.fn(),
  fetchIctJobsReport: vi.fn(),
  fetchIctWebhooksReport: vi.fn(),
  exportIctNovedadesReport: vi.fn(),
  exportIctAtascadosReport: vi.fn(),
  exportIctJobsReport: vi.fn(),
  exportIctWebhooksReport: vi.fn(),
  fetchCompaniesIndex: vi.fn(),
}));

vi.mock("@/lib/api/admin-companies", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-companies")>();
  return { ...actual, fetchCompaniesIndex: mocks.fetchCompaniesIndex };
});

vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return {
    ...actual,
    decodeJwtPayload: () => ({ tenant_id: "tenant-super" }),
    isSuperAdmin: mocks.isSuperAdmin,
  };
});

vi.mock("@/lib/api/ict-queries", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/ict-queries")>();
  return {
    ...actual,
    fetchIctQueryFields: mocks.fetchIctQueryFields,
    fetchIctSavedQueries: mocks.fetchIctSavedQueries,
    runIctQuery: mocks.runIctQuery,
  };
});

vi.mock("@/lib/api/ict-reports", () => ({
  ICT_REPORT_PAGE_SIZE: 50,
  ICT_EXCEL_MAX_ROWS: 2_000,
  fetchIctNovedadesReport: mocks.fetchIctNovedadesReport,
  fetchIctAtascadosReport: mocks.fetchIctAtascadosReport,
  fetchIctJobsReport: mocks.fetchIctJobsReport,
  fetchIctWebhooksReport: mocks.fetchIctWebhooksReport,
  exportIctNovedadesReport: mocks.exportIctNovedadesReport,
  exportIctAtascadosReport: mocks.exportIctAtascadosReport,
  exportIctJobsReport: mocks.exportIctJobsReport,
  exportIctWebhooksReport: mocks.exportIctWebhooksReport,
}));

vi.mock("@/lib/api/analytics-scheduling", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/analytics-scheduling")>();
  return {
    ...actual,
    fetchAlertRules: vi.fn().mockResolvedValue({ items: [] }),
    fetchAlertEvents: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }),
    fetchReportSchedules: vi.fn().mockResolvedValue({ items: [] }),
  };
});

import { IctReports } from "@/components/atom/modules/IctReports";

describe("IctReports — pestañas en vivo, Consultas y Programación (HU #11619)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.isSuperAdmin.mockReturnValue(false);
    mocks.fetchIctQueryFields.mockResolvedValue([]);
    mocks.fetchIctSavedQueries.mockResolvedValue([]);
    mocks.runIctQuery.mockResolvedValue({
      total: 0,
      page: 1,
      pageSize: 25,
      desde: "2026-07-07",
      hasta: "2026-08-05",
      totalPeriodoAnterior: 0,
      filas: [],
      cobertura: [],
    });
    mocks.fetchIctNovedadesReport.mockResolvedValue({
      // El backend manda el porcentaje SIN el símbolo (nació para una celda de Excel bajo una
      // cabecera que ya decía "%"); la UI se lo pone.
      resumenPorCausa: [{ causa: "Documento ilegible", cantidad: 3, porcentajeTexto: "60" }],
      totalPeriodoAnterior: 2,
      detalle: [
        {
          placa: "ABC123",
          vin: null,
          radicado: "R-1",
          comentarios: "Falta soporte",
          registradoEn: "2026-08-01T10:00:00Z",
        },
      ],
      total: 3,
      truncated: false,
      page: 1,
      pageSize: 50,
    });
    mocks.fetchIctAtascadosReport.mockResolvedValue({
      detalle: [], total: 0, truncated: false, page: 1, pageSize: 50,
    });
    mocks.fetchIctJobsReport.mockResolvedValue({
      resumenPorJob: [], corridasFueraDeSla: [], total: 0, truncated: false, totalPeriodoAnterior: 0,
      totalFueraDeSla: 0, page: 1, pageSize: 50,
    });
    mocks.fetchIctWebhooksReport.mockResolvedValue({
      detalle: [], total: 0, truncated: false, totalPeriodoAnterior: 0, page: 1, pageSize: 50,
      totalEntregados: 0, totalFallidos: 0, totalPendientes: 0,
    });
    mocks.exportIctNovedadesReport.mockResolvedValue(undefined);
    mocks.exportIctJobsReport.mockResolvedValue(undefined);
    mocks.fetchCompaniesIndex.mockResolvedValue({ data: [{ id: "t1", razonSocial: "Compañía Uno", nit: "900" }] });
    window.history.replaceState({}, "", "/");
  });

  it("muestra Novedades/Atascados/Webhooks/Consultas para un usuario no SuperAdmin, sin Jobs", () => {
    render(<IctReports />);
    expect(screen.getByRole("tab", { name: "Novedades" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Atascados" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Webhooks" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Consultas" })).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Jobs" })).not.toBeInTheDocument();
  });

  it("SuperAdmin sí ve la pestaña Jobs", () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    render(<IctReports />);
    expect(screen.getByRole("tab", { name: "Jobs" })).toBeInTheDocument();
  });

  it("la pestaña Novedades carga y muestra el resumen y el detalle en vivo", async () => {
    render(<IctReports />);
    await waitFor(() => expect(mocks.fetchIctNovedadesReport).toHaveBeenCalled());
    expect(await screen.findByText("Documento ilegible")).toBeInTheDocument();
    expect(screen.getByText("R-1")).toBeInTheDocument();
  });

  it("cambia a la pestaña Consultas y monta la consola de consultas de ICT", async () => {
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Consultas" }));

    expect(screen.getByTestId("ict-queries-tab")).toBeInTheDocument();
    await waitFor(() => expect(mocks.fetchIctQueryFields).toHaveBeenCalled());
  });

  it("abre el panel de Programación al hacer clic en el botón", async () => {
    const user = userEvent.setup();
    render(<IctReports />);

    expect(screen.queryByTestId("scheduling-panel")).not.toBeInTheDocument();
    await user.click(screen.getByTestId("ict-reportes-abrir-programacion"));

    expect(await screen.findByTestId("scheduling-panel")).toBeInTheDocument();
  });

  it("no ofrece 'ICT · Detalle de rendimiento de jobs' en el selector de tipo cuando el usuario no es SuperAdmin", async () => {
    mocks.isSuperAdmin.mockReturnValue(false);
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByTestId("ict-reportes-abrir-programacion"));
    await screen.findByTestId("scheduling-panel");
    await user.click(screen.getByRole("button", { name: /Nuevo informe/i }));

    const select = screen.getByLabelText("Tipo de informe");
    const options = within(select).getAllByRole("option").map((o) => o.textContent);
    expect(options).toContain("ICT · Detalle de novedades por causa");
    expect(options).not.toContain("ICT · Detalle de rendimiento de jobs");
  });

  it("sí ofrece 'ICT · Detalle de rendimiento de jobs' cuando el usuario es SuperAdmin", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByTestId("ict-reportes-abrir-programacion"));
    await screen.findByTestId("scheduling-panel");
    await user.click(screen.getByRole("button", { name: /Nuevo informe/i }));

    const select = screen.getByLabelText("Tipo de informe");
    const options = within(select).getAllByRole("option").map((o) => o.textContent);
    expect(options).toContain("ICT · Detalle de rendimiento de jobs");
  });

  it("Programar informe desde una consulta guardada abre la Programación con esa consulta preseleccionada", async () => {
    mocks.fetchIctSavedQueries.mockResolvedValue([
      {
        id: "q1",
        nombre: "Mis pendientes",
        descripcion: null,
        deFabrica: false,
        definition: { fechas: { preset: "ultimos_30" }, condiciones: [], columnas: [] },
        createdAt: "2026-08-01T00:00:00Z",
        updatedAt: null,
      },
    ]);
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Consultas" }));
    await waitFor(() => expect(mocks.fetchIctSavedQueries).toHaveBeenCalled());

    const scheduleButton = await screen.findByRole("button", { name: "Programar informe de Mis pendientes" });
    await user.click(scheduleButton);

    const panel = await screen.findByTestId("scheduling-panel");
    expect(within(panel).getByText(/Mis pendientes/i)).toBeInTheDocument();
  });

  // ── Compañía para SuperAdmin ────────────────────────────────────────────────────────────────
  // Antes el módulo usaba en silencio el tenant del propio SuperAdmin, que casi nunca es donde
  // están los datos de ICT: el resultado era ver tres pestañas vacías sin ninguna pista del porqué.

  it("SuperAdmin sin compañía elegida ve el aviso y NO consulta la API", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    render(<IctReports />);

    expect(await screen.findByTestId("aviso-selecciona-compania")).toBeInTheDocument();
    expect(mocks.fetchIctNovedadesReport).not.toHaveBeenCalled();
  });

  it("SuperAdmin con compañía en la dirección sí consulta, y la manda como tenantId", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    window.history.replaceState({}, "", "/?compania=t1");
    render(<IctReports />);

    await waitFor(() => expect(mocks.fetchIctNovedadesReport).toHaveBeenCalled());
    expect(mocks.fetchIctNovedadesReport.mock.calls[0][1]).toBe("t1");
    expect(screen.queryByTestId("aviso-selecciona-compania")).not.toBeInTheDocument();
  });

  it("Jobs no pide compañía: es de plataforma y carga aunque el SuperAdmin no haya elegido", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Jobs" }));

    await waitFor(() => expect(mocks.fetchIctJobsReport).toHaveBeenCalled());
    expect(screen.queryByTestId("aviso-selecciona-compania")).not.toBeInTheDocument();
  });

  // ── Formato de las cifras ───────────────────────────────────────────────────────────────────

  it("las duraciones de los jobs se muestran en milisegundos, no aplastadas a '0 s'", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.fetchIctJobsReport.mockResolvedValue({
      // 11,5 ms de promedio: con un decimal de segundo esto se veía como "0 s".
      resumenPorJob: [
        {
          job: "orchestrator",
          corridas: 365,
          duracionPromedioSeg: 0.0115,
          duracionMaximaSeg: 0.4129,
          porcentajeFueraDeSlaTexto: "0",
        },
      ],
      corridasFueraDeSla: [],
      total: 365,
      truncated: false,
      totalPeriodoAnterior: 300,
      totalFueraDeSla: 0,
      page: 1,
      pageSize: 50,
    });
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Jobs" }));

    // Se redondea al milisegundo entero: por debajo de eso ya es ruido de medición.
    expect(await screen.findByText("12 ms")).toBeInTheDocument();
    expect(screen.getByText("413 ms")).toBeInTheDocument();
    expect(screen.queryByText("0 s")).not.toBeInTheDocument();
  });

  it("los porcentajes llevan el símbolo que el backend no manda", async () => {
    render(<IctReports />);
    // "60" del resumen por causa se pinta como "60%" junto a la cantidad.
    expect(await screen.findByText(/3 · 60%/)).toBeInTheDocument();
  });

  it("la tarjeta muestra la variación frente al periodo anterior", async () => {
    render(<IctReports />);
    // 3 novedades frente a 2 del periodo previo = +50%.
    expect(await screen.findByLabelText(/Variación de 50% frente al periodo comparado/)).toBeInTheDocument();
  });

  // ── Exportación ─────────────────────────────────────────────────────────────────────────────

  it("exporta a Excel el informe de la pestaña activa con el rango y la compañía vigentes", async () => {
    const user = userEvent.setup();
    render(<IctReports />);
    await waitFor(() => expect(mocks.fetchIctNovedadesReport).toHaveBeenCalled());

    await user.click(screen.getByTestId("ict-reportes-exportar-excel"));

    await waitFor(() => expect(mocks.exportIctNovedadesReport).toHaveBeenCalled());
    const [range] = mocks.exportIctNovedadesReport.mock.calls[0];
    expect(range).toEqual(expect.objectContaining({ from: expect.any(String), to: expect.any(String) }));
  });

  it("«Exportar Excel» queda deshabilitado cuando la pestaña no tiene nada que exportar", async () => {
    // Atascados vacío descargaba un Excel con la cabecera y nada más.
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Atascados" }));
    await waitFor(() => expect(mocks.fetchIctAtascadosReport).toHaveBeenCalled());

    await waitFor(() =>
      expect(screen.getByTestId("ict-reportes-exportar-excel")).toBeDisabled(),
    );
  });

  // ── Paginación del detalle (HU #11630) ──────────────────────────────────────────────────────
  // Antes el módulo pintaba de golpe las 2.000 filas que devolvía el backend: 2.001 <tr> y 12.242
  // nodos en una sola pantalla.

  it("pide la primera página con el tamaño acordado y muestra el conteo del universo real", async () => {
    mocks.fetchIctNovedadesReport.mockResolvedValue({
      resumenPorCausa: [{ causa: "SOAT", cantidad: 120, porcentajeTexto: "100" }],
      totalPeriodoAnterior: 100,
      detalle: [
        { placa: "AAA111", vin: null, radicado: "R-1", comentarios: null, registradoEn: "2026-08-01T10:00:00Z" },
      ],
      total: 120,
      truncated: false,
      page: 1,
      pageSize: 50,
    });
    render(<IctReports />);

    await waitFor(() => expect(mocks.fetchIctNovedadesReport).toHaveBeenCalled());
    expect(mocks.fetchIctNovedadesReport.mock.calls[0][2]).toEqual({ page: 1, pageSize: 50 });
    expect(await screen.findByText(/Mostrando 1–50 de 120/)).toBeInTheDocument();
    // El encabezado usa el total del periodo, no el largo de la página.
    expect(screen.getByText("Detalle de novedades (120)")).toBeInTheDocument();
  });

  it("«Siguiente» vuelve a consultar el detalle pidiendo la página 2", async () => {
    mocks.fetchIctNovedadesReport.mockResolvedValue({
      resumenPorCausa: [],
      totalPeriodoAnterior: 0,
      detalle: [
        { placa: "AAA111", vin: null, radicado: "R-1", comentarios: null, registradoEn: "2026-08-01T10:00:00Z" },
      ],
      total: 120,
      truncated: false,
      page: 1,
      pageSize: 50,
    });
    const user = userEvent.setup();
    render(<IctReports />);
    await screen.findByRole("button", { name: "Página siguiente" });

    await user.click(screen.getByRole("button", { name: "Página siguiente" }));

    await waitFor(() =>
      expect(mocks.fetchIctNovedadesReport.mock.calls.at(-1)?.[2]).toEqual({ page: 2, pageSize: 50 }),
    );
  });

  it("el aviso de corte habla del Excel, no de la pantalla, y solo cuando el periodo lo supera", async () => {
    mocks.fetchIctNovedadesReport.mockResolvedValue({
      resumenPorCausa: [],
      totalPeriodoAnterior: 0,
      detalle: [],
      total: 5210,
      truncated: true,
      page: 1,
      pageSize: 50,
    });
    render(<IctReports />);

    const aviso = await screen.findByTestId("ict-aviso-excel-truncado");
    expect(aviso).toHaveTextContent(/El Excel de este informe se corta en las primeras 2.000 filas/);
    expect(aviso).not.toHaveTextContent(/El detalle se limitó/);
  });

  it("Jobs no avisa de corte con 38.865 corridas si solo 17 quedaron fuera de SLA: la hoja cabe entera", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.fetchIctJobsReport.mockResolvedValue({
      resumenPorJob: [
        { job: "orchestrator", corridas: 38865, duracionPromedioSeg: 0.01, duracionMaximaSeg: 0.4, porcentajeFueraDeSlaTexto: "0" },
      ],
      corridasFueraDeSla: [
        { job: "orchestrator", resultado: "ok", duracionSeg: 9, inicio: "2026-08-01T10:00:00Z" },
      ],
      total: 38865,
      // `truncated` se calcula sobre `totalFueraDeSla` (17 ≤ 2.000), no sobre las 38.865 corridas:
      // la única hoja que el Excel puede cortar es la de fuera de SLA.
      truncated: false,
      totalPeriodoAnterior: 38102,
      totalFueraDeSla: 17,
      page: 1,
      pageSize: 50,
    });
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Jobs" }));
    await waitFor(() => expect(mocks.fetchIctJobsReport).toHaveBeenCalled());

    expect(await screen.findByText("Corridas fuera de SLA (17)")).toBeInTheDocument();
    expect(screen.queryByTestId("ict-aviso-excel-truncado")).not.toBeInTheDocument();
  });

  it("el KPI de corridas fuera de SLA usa el universo del periodo, no el largo de la página", async () => {
    mocks.isSuperAdmin.mockReturnValue(true);
    mocks.fetchIctJobsReport.mockResolvedValue({
      resumenPorJob: [
        { job: "orchestrator", corridas: 10, duracionPromedioSeg: 0.01, duracionMaximaSeg: 0.4, porcentajeFueraDeSlaTexto: "0" },
      ],
      corridasFueraDeSla: [
        { job: "orchestrator", resultado: "ok", duracionSeg: 9, inicio: "2026-08-01T10:00:00Z" },
      ],
      total: 10,
      truncated: true, // 3.120 fuera de SLA > 2.000: ahí sí se corta la hoja del Excel
      totalPeriodoAnterior: 10,
      totalFueraDeSla: 3120,
      page: 1,
      pageSize: 50,
    });
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Jobs" }));

    expect(await screen.findByText("3.120")).toBeInTheDocument();
    // 3.120 > 2.000: ahí sí se corta la hoja del Excel.
    expect(screen.getByTestId("ict-aviso-excel-truncado")).toBeInTheDocument();
  });

  // ── Novedades: las 5 causas (HU #11630) ─────────────────────────────────────────────────────
  // Antes solo se pintaban las 3 primeras: en dev eso enseñaba tres ceros mientras el 100% de las
  // novedades estaba en "Otra/sin clasificar".

  it("muestra TODAS las causas del resumen, no solo las tres primeras", async () => {
    mocks.fetchIctNovedadesReport.mockResolvedValue({
      resumenPorCausa: [
        { causa: "SOAT", cantidad: 0, porcentajeTexto: "0" },
        { causa: "RTM", cantidad: 0, porcentajeTexto: "0" },
        { causa: "RNMC", cantidad: 0, porcentajeTexto: "0" },
        { causa: "Documento faltante", cantidad: 0, porcentajeTexto: "0" },
        { causa: "Otra/sin clasificar", cantidad: 2000, porcentajeTexto: "100" },
      ],
      totalPeriodoAnterior: 0,
      detalle: [],
      total: 2000,
      truncated: false,
      page: 1,
      pageSize: 50,
    });
    render(<IctReports />);

    for (const causa of ["SOAT", "RTM", "RNMC", "Documento faltante", "Otra/sin clasificar"]) {
      expect(await screen.findByText(causa)).toBeInTheDocument();
    }
    // La causa que se llevaba todo ya no queda escondida.
    expect(screen.getByText("2.000 · 100%")).toBeInTheDocument();
  });

  // ── Webhooks: entregadas vs fallidas ────────────────────────────────────────────────────────

  it("muestra entregados/fallidos/pendientes del PERIODO, no de la página", async () => {
    mocks.fetchIctWebhooksReport.mockResolvedValue({
      // La página trae 3 filas; el KPI tiene que hablar de las 5.210 del periodo.
      detalle: [
        { radicado: "R-1", estado: "Fallido", intentos: 3, urlDestino: null, registradoEn: "2026-08-01T10:00:00Z" },
        { radicado: "R-2", estado: "Fallido", intentos: 2, urlDestino: null, registradoEn: "2026-08-01T11:00:00Z" },
        { radicado: "R-3", estado: "Entregado", intentos: 1, urlDestino: null, registradoEn: "2026-08-01T12:00:00Z" },
      ],
      total: 5210,
      truncated: true,
      totalPeriodoAnterior: 4980,
      page: 1,
      pageSize: 50,
      totalEntregados: 4810,
      totalFallidos: 315,
      totalPendientes: 85,
    });
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Webhooks" }));

    expect(await screen.findByText("Entregados")).toBeInTheDocument();
    expect(screen.getByText("4.810 · 92,3%")).toBeInTheDocument();
    expect(screen.getByText("315 · 6%")).toBeInTheDocument();
    expect(screen.getByText("85 · 1,6%")).toBeInTheDocument();
    // El reparto de la página ya no se pinta: el del periodo lo reemplaza.
    expect(screen.queryByTestId("ict-webhooks-estados-pagina")).not.toBeInTheDocument();
  });

  it("con todo fallido, el indicador lo grita: 100% en Fallidos y 0% en Entregados", async () => {
    // Evidencia de dev con «Pruebas Carga ICT»: todas las entregas de esa compañía estaban en
    // "Fallido" y el único KPI era el conteo total, que no lo dejaba ver.
    mocks.fetchIctWebhooksReport.mockResolvedValue({
      detalle: [
        { radicado: "R-1", estado: "Fallido", intentos: 3, urlDestino: null, registradoEn: "2026-08-01T10:00:00Z" },
      ],
      total: 5210,
      truncated: true,
      totalPeriodoAnterior: 4980,
      page: 1,
      pageSize: 50,
      totalEntregados: 0,
      totalFallidos: 5210,
      totalPendientes: 0,
    });
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Webhooks" }));

    expect(await screen.findByText("5.210 · 100%")).toBeInTheDocument();
    expect(screen.getAllByText("0 · 0%")).toHaveLength(2); // entregados y pendientes
  });

  it("no ofrece el botón de exportar en Consultas: esa consola ya trae el suyo", async () => {
    const user = userEvent.setup();
    render(<IctReports />);

    await user.click(screen.getByRole("tab", { name: "Consultas" }));

    expect(screen.queryByTestId("ict-reportes-exportar-excel")).not.toBeInTheDocument();
  });
});
