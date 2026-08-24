// HU #11788 (Feature #11784) — bandeja del LOG QX: carga sin buscar (AC1), columnas y su orden
// (AC2), filtros combinables (AC3), contadores como filtro rápido (AC4), vistazo expandible (AC5),
// antigüedad destacada (AC6) y los estados de la pantalla (AC7). La capa de datos se mockea.
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type {
  LogQxBandejaEntry,
  LogQxBandejaEstado,
  LogQxBandejaPage,
} from "@/lib/api/admin-log-qx";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push, replace: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock("next/link", () => ({
  default: (props: { href: string; children: ReactNode; className?: string }) => (
    <a href={props.href} className={props.className}>
      {props.children}
    </a>
  ),
}));

const mocks = vi.hoisted(() => ({ fetchLogQxBandeja: vi.fn() }));
vi.mock("@/lib/api/admin-log-qx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-log-qx")>();
  return { ...actual, fetchLogQxBandeja: mocks.fetchLogQxBandeja };
});

import { LogQx } from "@/components/atom/modules/LogQx";

/**
 * Los contadores y el chip de estado de la fila muestran la misma etiqueta ("En trámite",
 * "Rechazado"…), así que las consultas se acotan al grupo de contadores.
 */
function contador(nombre: RegExp) {
  return within(screen.getByRole("group", { name: /Contadores por estado/i })).getByRole("button", {
    name: nombre,
  });
}

const INSTANCE = "22222222-2222-2222-2222-222222222222";
const SUBMISSION = "11111111-1111-1111-1111-111111111111";

function entry(over: Partial<LogQxBandejaEntry> = {}): LogQxBandejaEntry {
  return {
    procedureInstanceId: INSTANCE,
    referenceNumber: "TRM-2026-000271",
    plate: "ABC123",
    procedureTypeName: "Matrícula inicial",
    estado: "en_tramite",
    clientTenantName: "AutoFlota Antioquia S.A.S",
    transitOfficeName: "Ibagué",
    divipoCode: "17001",
    documentoQx: "TESLA_MI_20260811_1220_LRWYGCFJ3TC767907",
    submissionId: SUBMISSION,
    intentos: 1,
    attempts: 1,
    pollCount: 1065,
    qxRegisterCode: 81,
    qxProcedureCode: null,
    rejectionReason: null,
    ultimaActividad: "2026-08-24T11:52:00Z",
    esperandoDesde: "2026-08-18T17:40:00Z",
    horasEsperando: 148,
    submissionCreatedAt: "2026-08-18T17:40:00Z",
    ...over,
  };
}

function page(over: Partial<LogQxBandejaPage> = {}): LogQxBandejaPage {
  return {
    data: [entry()],
    totalCount: 1,
    page: 1,
    pageSize: 25,
    contadores: [
      { estado: "sin_radicar", total: 3 },
      { estado: "pendiente", total: 0 },
      { estado: "radicado", total: 9 },
      { estado: "en_tramite", total: 47 },
      { estado: "aprobado", total: 12 },
      { estado: "rechazado", total: 2 },
      { estado: "fallido", total: 1 },
    ],
    ...over,
  };
}

describe("LOG QX — bandeja (HU #11788)", () => {
  beforeEach(() => {
    mocks.fetchLogQxBandeja.mockReset();
    push.mockReset();
  });

  it("AC1: carga con datos al montar, sin que nadie pulse Buscar", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page());

    render(<LogQx />);

    // Se consulta sola: es el defecto central que corrige esta HU.
    await waitFor(() => expect(mocks.fetchLogQxBandeja).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("TRM-2026-000271")).toBeInTheDocument();
  });

  it("AC2: las columnas aparecen en el orden acordado con el PO", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page());

    render(<LogQx />);
    await screen.findByText("TRM-2026-000271");

    const encabezados = screen.getAllByRole("columnheader").map((th) => th.textContent?.trim());
    expect(encabezados).toEqual([
      "",
      "Trámite",
      "Placa",
      "Tipo",
      "Estado",
      "Empresa",
      "Secretaría",
      "Documento QX",
      "Última actividad",
      "Antigüedad",
    ]);
  });

  it("AC2: un trámite con varias radicaciones muestra su número de intentos", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(
      page({ data: [entry({ intentos: 3, estado: "fallido" })] }),
    );

    render(<LogQx />);

    expect(await screen.findByText("3 intentos")).toBeInTheDocument();
  });

  it("AC3: los filtros de placa y documento viajan juntos al backend", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page());

    render(<LogQx />);
    await waitFor(() => expect(mocks.fetchLogQxBandeja).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "ABC123" } });
    fireEvent.change(screen.getByLabelText("Documento QX"), { target: { value: "LRWYGCF" } });
    fireEvent.click(screen.getByRole("button", { name: /Aplicar/i }));

    await waitFor(() =>
      expect(mocks.fetchLogQxBandeja).toHaveBeenLastCalledWith(
        expect.objectContaining({ placa: "ABC123", documento: "LRWYGCF" }),
      ),
    );
  });

  it("AC4: pulsar un contador filtra por ese estado, y volver a pulsarlo lo retira", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page());

    render(<LogQx />);
    await waitFor(() => expect(mocks.fetchLogQxBandeja).toHaveBeenCalled());

    const rechazado = contador(/Rechazado/i);
    fireEvent.click(rechazado);
    await waitFor(() =>
      expect(mocks.fetchLogQxBandeja).toHaveBeenLastCalledWith(
        expect.objectContaining({ estado: "rechazado" }),
      ),
    );

    fireEvent.click(rechazado);
    await waitFor(() =>
      expect(mocks.fetchLogQxBandeja).toHaveBeenLastCalledWith(
        expect.objectContaining({ estado: undefined }),
      ),
    );
  });

  it("AC4: los contadores muestran el total del conjunto filtrado, no el de la página", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page());

    render(<LogQx />);
    await screen.findByText("TRM-2026-000271");

    // Una sola fila en la página, pero 47 en el conjunto.
    expect(within(contador(/En trámite/i)).getByText("47")).toBeInTheDocument();
  });

  it("AC5: expandir la fila muestra el resumen en lenguaje natural, sin payloads", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page());

    render(<LogQx />);
    fireEvent.click(await screen.findByRole("button", { name: /TRM-2026-000271/i }));

    expect(
      await screen.findByText(/La Secretaría de Ibagué aún no lo resuelve/i),
    ).toBeInTheDocument();
    expect(screen.getAllByText(/1065 consultas/i).length).toBeGreaterThan(0);
    // El detalle técnico vive en la trazabilidad, no aquí.
    expect(screen.queryByText(/duration_ms/)).not.toBeInTheDocument();
  });

  it("AC5: desde el vistazo se navega a la trazabilidad conservando los filtros", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page());

    render(<LogQx />);
    await waitFor(() => expect(mocks.fetchLogQxBandeja).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "ABC123" } });
    fireEvent.click(screen.getByRole("button", { name: /Aplicar/i }));
    fireEvent.click(await screen.findByRole("button", { name: /TRM-2026-000271/i }));
    fireEvent.click(await screen.findByRole("button", { name: /Ver trazabilidad completa/i }));

    expect(push).toHaveBeenCalledWith(expect.stringContaining(`/log-qx/${SUBMISSION}`));
    expect(push).toHaveBeenCalledWith(expect.stringContaining("placa=ABC123"));
  });

  it("AC5: un trámite sin radicación no ofrece un enlace de trazabilidad roto", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(
      page({
        data: [entry({ estado: "sin_radicar", submissionId: null, documentoQx: null, intentos: 0 })],
      }),
    );

    render(<LogQx />);
    fireEvent.click(await screen.findByRole("button", { name: /TRM-2026-000271/i }));

    expect(await screen.findByText(/todavía no se ha encolado/i)).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Ver trazabilidad completa/i }),
    ).not.toBeInTheDocument();
  });

  it("AC6: una espera por encima del umbral se destaca; un trámite resuelto no muestra antigüedad", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(
      page({
        data: [
          entry({ horasEsperando: 148 }),
          entry({
            procedureInstanceId: "33333333-3333-3333-3333-333333333333",
            referenceNumber: "TRM-2026-000265",
            estado: "aprobado",
            horasEsperando: null,
            esperandoDesde: null,
          }),
        ],
        totalCount: 2,
      }),
    );

    render(<LogQx />);
    await screen.findByText("TRM-2026-000265");

    // 148 h = 6 días 4 horas, por encima del umbral de 48 h.
    expect(screen.getByText(/6 d 4 h/)).toBeInTheDocument();
    const resuelta = screen.getByText("TRM-2026-000265").closest("tr")!;
    expect(within(resuelta).queryByText(/⚠/)).not.toBeInTheDocument();
  });

  it("AC7: un fallo de la API muestra el error con opción de reintentar", async () => {
    mocks.fetchLogQxBandeja.mockRejectedValue(new Error("boom"));

    render(<LogQx />);

    expect(await screen.findByText(/boom/i)).toBeInTheDocument();
  });

  it("AC7: sin resultados se explica cómo ampliar la búsqueda", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page({ data: [], totalCount: 0 }));

    render(<LogQx />);

    expect(
      await screen.findByText(/Amplía el rango de fechas o quita algún filtro/i),
    ).toBeInTheDocument();
  });

  it("AC9: aplica variantes de modo oscuro y la tabla desplaza en su propio contenedor", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page());

    const { container } = render(<LogQx />);
    await screen.findByText("TRM-2026-000271");

    expect(container.querySelector(".dark\\:text-white")).not.toBeNull();
    // El body nunca desplaza en horizontal: lo hace el contenedor de la tabla.
    expect(container.querySelector(".overflow-x-auto")).not.toBeNull();
  });
});

describe("LOG QX — estados de la bandeja (HU #11788)", () => {
  beforeEach(() => mocks.fetchLogQxBandeja.mockReset());

  const casos: [LogQxBandejaEstado, RegExp][] = [
    ["sin_radicar", /todavía no se ha encolado/i],
    ["pendiente", /Está en cola para radicarse/i],
    ["radicado", /Todavía no se ha ejecutado la primera consulta/i],
    ["en_tramite", /aún no lo resuelve/i],
    ["aprobado", /lo aprobó/i],
    ["rechazado", /lo rechazó/i],
    ["fallido", /nunca llegó a la Secretaría/i],
  ];

  it.each(casos)("el estado %s se explica en lenguaje natural", async (estado, esperado) => {
    mocks.fetchLogQxBandeja.mockResolvedValue(page({ data: [entry({ estado })] }));

    render(<LogQx />);
    fireEvent.click(await screen.findByRole("button", { name: /TRM-2026-000271/i }));

    expect(await screen.findByText(esperado)).toBeInTheDocument();
  });
});
