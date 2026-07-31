// HU #10795 (Feature #10792) — módulo LOG QX: búsqueda + línea de tiempo (AC1), detalle
// técnico expandible con visor JSON y copia (AC2) y los 4 estados de UI + "sin payload
// disponible" (AC3). La capa de datos se mockea (sin red real).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { LogQxEntry, LogQxPage } from "@/lib/api/admin-log-qx";

const mocks = vi.hoisted(() => ({ fetchLogQx: vi.fn() }));
vi.mock("@/lib/api/admin-log-qx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-log-qx")>();
  return { ...actual, fetchLogQx: mocks.fetchLogQx };
});

import { LogQx } from "@/components/atom/modules/LogQx";

const ENTRY: LogQxEntry = {
  id: "11111111-1111-1111-1111-111111111111",
  procedureInstanceId: "22222222-2222-2222-2222-222222222222",
  referenceNumber: "TRM-2026-000001",
  procedureTypeName: "Traspaso",
  clientTenantName: "Renting del Café S.A.S.",
  plate: "ABC123",
  documentName: "FLIT_TRM-2026-000001",
  divipoCode: "05001",
  status: "aprobado",
  attempts: 1,
  pollCount: 2,
  qxRegisterCode: 81,
  qxProcedureCode: 2,
  rejectionReason: null,
  createdAt: "2026-07-01T12:00:00Z",
  registeredAt: null,
  lastPolledAt: null,
  completedAt: null,
  updatedAt: null,
  events: [
    {
      stage: "registro_respuesta",
      outcome: "ok",
      detail: { codigo: 81, duration_ms: 1234, origen: "quipux_register" },
      durationMs: 1234,
      origin: "quipux_register",
      responseCode: 81,
      correlationId: null,
      occurredAt: "2026-07-01T12:00:00Z",
    },
    {
      // Evento histórico sin detalle → "sin payload disponible" (AC3 parcial).
      stage: "reintento_manual",
      outcome: "ok",
      detail: null,
      durationMs: null,
      origin: null,
      responseCode: null,
      correlationId: null,
      occurredAt: "2026-07-01T12:05:00Z",
    },
  ],
};

const PAGE: LogQxPage = { data: [ENTRY], totalCount: 1, page: 1, pageSize: 20 };

function search(value = "ABC123") {
  fireEvent.change(screen.getByLabelText(/Valor de búsqueda/i), { target: { value } });
  fireEvent.click(screen.getByRole("button", { name: "Buscar" }));
}

describe("LogQx — módulo LOG QX (HU #10795)", () => {
  beforeEach(() => {
    mocks.fetchLogQx.mockReset();
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } });
  });
  afterEach(() => vi.clearAllTimers());

  // ── AC3 — estado inicial (antes de buscar): prompt guía ──
  it("AC3: muestra el prompt inicial antes de cualquier búsqueda, sin llamar a la API", () => {
    render(<LogQx />);
    expect(screen.getByTestId("ui-empty")).toHaveTextContent(/Ingresa una placa/i);
    expect(mocks.fetchLogQx).not.toHaveBeenCalled();
  });

  // ── AC1 — búsqueda devuelve la radicación con su línea de tiempo ──
  it("AC1: busca por placa y renderiza la radicación con su línea de tiempo", async () => {
    mocks.fetchLogQx.mockResolvedValue(PAGE);
    render(<LogQx />);
    search("ABC123");

    await waitFor(() => expect(screen.getByText("TRM-2026-000001")).toBeInTheDocument());
    // Se consultó por el eje placa.
    expect(mocks.fetchLogQx).toHaveBeenCalledWith(
      expect.objectContaining({ placa: "ABC123", instanceId: undefined, radicado: undefined }),
    );
    // Estado final + timeline (etiqueta de etapa conocida).
    expect(screen.getByRole("status", { name: /Estado: Aprobado/i })).toBeInTheDocument();
    expect(screen.getByText("Respuesta de registro")).toBeInTheDocument();
    expect(screen.getByText("Reintento manual")).toBeInTheDocument();
  });

  // ── AC2 — detalle técnico expandible con visor JSON y copia ──
  it("AC2: expande un evento, muestra el JSON y lo copia", async () => {
    mocks.fetchLogQx.mockResolvedValue(PAGE);
    render(<LogQx />);
    search("ABC123");
    await waitFor(() => expect(screen.getByText("TRM-2026-000001")).toBeInTheDocument());

    // Expandir el evento con detalle.
    fireEvent.click(screen.getByRole("button", { name: /Respuesta de registro/i }));
    expect(screen.getByText(/"codigo": 81/)).toBeInTheDocument();

    // Copiar → clipboard recibe el JSON y aparece "Copiado".
    fireEvent.click(screen.getByRole("button", { name: /Copiar detalle JSON/i }));
    await waitFor(() =>
      expect(navigator.clipboard.writeText).toHaveBeenCalledWith(expect.stringContaining('"codigo": 81')),
    );
    await waitFor(() => expect(screen.getByText("Copiado")).toBeInTheDocument());
  });

  it("AC3 (parcial): un evento sin detalle muestra 'Sin payload disponible'", async () => {
    mocks.fetchLogQx.mockResolvedValue(PAGE);
    render(<LogQx />);
    search("ABC123");
    await waitFor(() => expect(screen.getByText("TRM-2026-000001")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: /Reintento manual/i }));
    expect(screen.getByText(/Sin payload disponible/i)).toBeInTheDocument();
  });

  // ── AC3 — estado vacío (sin resultados) ──
  it("AC3: sin resultados muestra el empty state de 'ninguna radicación'", async () => {
    mocks.fetchLogQx.mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 20 });
    render(<LogQx />);
    search("ZZZ999");
    await waitFor(() =>
      expect(screen.getByTestId("ui-empty")).toHaveTextContent(/Ninguna radicación Quipux coincide/i),
    );
  });

  // ── AC5 — dark mode y responsive (transversal) ──
  it("AC5: aplica variantes dark mode y layout responsive (clases transversales)", () => {
    const { container } = render(<LogQx />);
    const root = container.firstChild as HTMLElement;
    expect(root.className).toMatch(/dark:text-white/); // soporte modo oscuro
    expect(screen.getByRole("search").className).toMatch(/flex-wrap/); // barra de búsqueda reflow
  });

  // ── AC3 — estado de error con reintentar ──
  it("AC3: un fallo de la API muestra el error con botón Reintentar", async () => {
    mocks.fetchLogQx.mockRejectedValueOnce(new Error("boom")).mockResolvedValueOnce(PAGE);
    render(<LogQx />);
    search("ABC123");

    await waitFor(() => expect(screen.getByTestId("ui-error")).toBeInTheDocument());
    const errorBox = screen.getByTestId("ui-error");
    fireEvent.click(within(errorBox).getByRole("button", { name: /Reintentar/i }));
    // El reintento reusa la última búsqueda y ya resuelve con datos.
    await waitFor(() => expect(screen.getByText("TRM-2026-000001")).toBeInTheDocument());
  });
});
