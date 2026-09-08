// Tests unitarios del módulo "Historial por placa" (Feature #12189 · HU #12194).
//
// Cubre los criterios de la HU:
//   AC1 — buscador por placa: consulta el endpoint dedicado con la placa normalizada y paginación.
//   AC2 — los 4 estados de UI: inicial/vacío, cargando, error (con reintentar) y lleno.
//   AC3 — placa vacía NO dispara consulta (ni un 400 evitable, ni un estado vacío falso).
//   AC4 — resultado vacío se pinta como estado VACÍO redactado con la placa, nunca como error.
//   AC5 — la columna "Compañía" solo existe para SuperAdmin.
//   AC6 — accesibilidad: label real asociado al input y anuncio del resultado por `aria-live`.
//
// Mock del cliente HTTP calcado de Auditoria.test.tsx: se mockea `tramitesClient` y se renderiza
// el componente real (sin red).
//
// Uso de ejemplo:
//   render(<HistorialPlaca isSuperAdmin />) → tabla con columna Compañía tras consultar una placa.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import type { InstanceSummary } from "@/lib/api/types/procedure-runtime";

const mocks = vi.hoisted(() => ({
  listPlateHistory: vi.fn(),
}));

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: { listPlateHistory: mocks.listPlateHistory },
}));

import { HistorialPlaca } from "@/components/atom/modules/HistorialPlaca";

function fila(overrides: Partial<InstanceSummary> = {}): InstanceSummary {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    referenceNumber: "RAD-0001",
    modalidad: "TRASPASO",
    tipoNombre: "Traspaso",
    estado: "aprobado",
    placa: "ABC123",
    vin: "9BWZZZ377VT004251",
    vehiculoMarca: "Mazda",
    vehiculoLinea: "CX-5",
    compradorNombre: "Compradora Uno",
    compradorDocumento: "1000000001",
    vendedorNombre: "Vendedor Uno",
    vendedorDocumento: "1000000002",
    organismoTransito: "OT Medellín",
    pasoActual: 5,
    totalPasos: 5,
    createdAt: "2026-08-01T10:00:00Z",
    draftFinalizedAt: null,
    identityValidationStatus: null,
    signaturePending: false,
    canSubmit: true,
    prioritario: false,
    tenantId: "tenant-aaa",
    companiaNombre: "Compañía Alfa",
    updatedAt: "2026-08-05T09:00:00Z",
    gestorNombre: "Gestora Uno",
    ...overrides,
  } as InstanceSummary;
}

async function consultar(placa: string) {
  const user = userEvent.setup();
  await user.clear(screen.getByLabelText("Placa"));
  if (placa) await user.type(screen.getByLabelText("Placa"), placa);
  await user.click(screen.getByRole("button", { name: /consultar/i }));
  return user;
}

beforeEach(() => {
  mocks.listPlateHistory.mockReset();
});

describe("HistorialPlaca — estados de UI", () => {
  it("AC2/AC6 — arranca en estado inicial (no vacío genérico) con label accesible en el input", () => {
    render(<HistorialPlaca />);

    expect(screen.getByTestId("historial-placa-idle")).toBeInTheDocument();
    expect(
      screen.getByText(/Ingrese una placa y pulse Consultar para ver su historial/i),
    ).toBeInTheDocument();
    // Label real (htmlFor/id), no placeholder.
    expect(screen.getByLabelText("Placa")).toHaveAttribute("id", "historial-placa-input");
    expect(mocks.listPlateHistory).not.toHaveBeenCalled();
  });

  it("AC2 — muestra el estado de carga mientras la consulta está en vuelo", async () => {
    let resolver: (value: { items: InstanceSummary[]; total: number }) => void = () => {};
    mocks.listPlateHistory.mockReturnValue(
      new Promise((resolve) => {
        resolver = resolve;
      }),
    );

    render(<HistorialPlaca />);
    await consultar("ABC123");

    expect(await screen.findByTestId("ui-loading")).toBeInTheDocument();

    resolver({ items: [fila()], total: 1 });
    await waitFor(() => expect(screen.getByTestId("historial-placa-table")).toBeInTheDocument());
  });

  it("AC2/AC4 — un resultado vacío se pinta como VACÍO redactado con la placa, no como error", async () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [], total: 0 });

    render(<HistorialPlaca />);
    await consultar("xyz789");

    const vacio = await screen.findByTestId("ui-empty");
    expect(screen.queryByTestId("ui-error")).not.toBeInTheDocument();
    // El mensaje está redactado con la placa consultada, no es el vacío genérico. (El mismo
    // texto vive también en la región aria-live, de ahí el `within`.)
    expect(
      within(vacio).getByText("No hay trámites registrados para la placa XYZ789."),
    ).toBeInTheDocument();
  });

  it("AC2 — el fallo del servidor cae en estado error y reintentar repite la consulta", async () => {
    mocks.listPlateHistory.mockRejectedValueOnce(new Error("Servicio no disponible"));
    render(<HistorialPlaca />);
    const user = await consultar("ABC123");

    const error = await screen.findByTestId("ui-error");
    expect(within(error).getByText("Servicio no disponible")).toBeInTheDocument();

    mocks.listPlateHistory.mockResolvedValueOnce({ items: [fila()], total: 1 });
    await user.click(screen.getByRole("button", { name: /reintentar/i }));

    expect(await screen.findByTestId("historial-placa-table")).toBeInTheDocument();
    expect(mocks.listPlateHistory).toHaveBeenCalledTimes(2);
  });

  it("AC1/AC2 — con resultados pinta la tabla y consulta con placa normalizada y paginación", async () => {
    mocks.listPlateHistory.mockResolvedValue({
      items: [fila(), fila({ id: "22222222-2222-2222-2222-222222222222", referenceNumber: "RAD-0002" })],
      total: 2,
    });

    render(<HistorialPlaca />);
    await consultar("  abc123 ");

    expect(await screen.findByTestId("historial-placa-table")).toBeInTheDocument();
    expect(mocks.listPlateHistory).toHaveBeenCalledWith({ placa: "ABC123", skip: 0, take: 20 });

    const tabla = screen.getByRole("table", {
      name: "Historial de trámites de la placa ABC123",
    });
    expect(within(tabla).getByText("RAD-0001")).toBeInTheDocument();
    expect(within(tabla).getByText("RAD-0002")).toBeInTheDocument();
    // Campos del criterio funcional presentes en la fila.
    expect(within(tabla).getAllByText("OT Medellín").length).toBe(2);
    expect(within(tabla).getAllByText("Gestora Uno").length).toBe(2);
  });
});

describe("HistorialPlaca — reglas de consulta y alcance", () => {
  it("AC3 — una placa vacía no dispara la consulta y deja la vista en estado inicial", async () => {
    render(<HistorialPlaca />);
    await consultar("   ");

    expect(mocks.listPlateHistory).not.toHaveBeenCalled();
    expect(screen.getByTestId("historial-placa-idle")).toBeInTheDocument();
  });

  it("AC5 — SuperAdmin ve la columna Compañía", async () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [fila()], total: 1 });

    render(<HistorialPlaca isSuperAdmin />);
    await consultar("ABC123");

    const tabla = await screen.findByRole("table", {
      name: "Historial de trámites de la placa ABC123",
    });
    expect(within(tabla).getByRole("columnheader", { name: "Compañía" })).toBeInTheDocument();
    expect(within(tabla).getByText("Compañía Alfa")).toBeInTheDocument();
  });

  it("AC5 — un usuario no SuperAdmin no ve la columna Compañía", async () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [fila()], total: 1 });

    render(<HistorialPlaca isSuperAdmin={false} />);
    await consultar("ABC123");

    const tabla = await screen.findByRole("table", {
      name: "Historial de trámites de la placa ABC123",
    });
    expect(within(tabla).queryByRole("columnheader", { name: "Compañía" })).toBeNull();
    expect(within(tabla).queryByText("Compañía Alfa")).toBeNull();
  });

  it("AC6 — el resultado se anuncia por una región aria-live", async () => {
    mocks.listPlateHistory.mockResolvedValue({ items: [fila()], total: 1 });

    render(<HistorialPlaca />);
    await consultar("ABC123");

    const anuncio = await screen.findByTestId("historial-placa-anuncio");
    expect(anuncio).toHaveAttribute("aria-live", "polite");
    await waitFor(() =>
      expect(anuncio).toHaveTextContent("1 trámite encontrado para la placa ABC123."),
    );
  });
});

describe("registro del módulo en la navegación SPA", () => {
  it("AC1 — `historial-placa` es un módulo navegable solo si el RBAC lo concede", async () => {
    const { ALL_MODULE_IDS, SPA_DOCK_MODULE_IDS, resolveNavigableModuleIds } = await import(
      "@/lib/nav/modules"
    );

    expect(ALL_MODULE_IDS).toContain("historial-placa");
    expect(SPA_DOCK_MODULE_IDS).toContain("historial-placa");

    const ctx = {
      isSuperAdmin: false,
      isOtAdmin: false,
      canReadLogQx: false,
      canReadIctLogs: false,
    };
    expect(
      resolveNavigableModuleIds({ ...ctx, accessibleCodes: ["historial-placa"] }),
    ).toContain("historial-placa");
    expect(resolveNavigableModuleIds({ ...ctx, accessibleCodes: [] })).not.toContain(
      "historial-placa",
    );
  });
});
