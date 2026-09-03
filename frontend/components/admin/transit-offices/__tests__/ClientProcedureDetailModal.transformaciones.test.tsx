// HU #11931 — cuando el trámite transforma el vehículo, el OT debe ver las DOS caras: lo que el
// RUNT tiene registrado y el valor nuevo. Antes solo veía el efectivo, presentado como si fuera el
// dato oficial del vehículo.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { ClientProcedureDetailModal } from "../ClientProcedureDetailModal";
import type { OtClientProcedure } from "@/lib/api/types-ot";

const fetchOtClientProcedure = vi.fn();
const fetchOtDocuments = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedure: (...args: unknown[]) => fetchOtClientProcedure(...args),
  fetchOtDocuments: (...args: unknown[]) => fetchOtDocuments(...args),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
}));

const BASE: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "tenant-1",
  procedureTypeId: "tipo-1",
  procedureTypeName: "Traspaso",
  clientTenantName: "Empresa Demo",
  referenceNumber: "RAD-0001",
  status: "entregado",
  plateFlowStatus: null,
  soatEstado: "vigente",
  createdAt: "2026-08-01T00:00:00Z",
  placa: "ABC123",
  actors: [],
};

async function renderConDetalle(procedure: OtClientProcedure) {
  fetchOtClientProcedure.mockResolvedValue(procedure);
  render(<ClientProcedureDetailModal open procedure={BASE} onClose={vi.fn()} />);
  return screen.findByRole("table", { name: "Datos del trámite" });
}

const BLOQUE = "Transformaciones declaradas frente al RUNT";

describe("Detalle OT — transformaciones del vehículo (HU #11931)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchOtDocuments.mockResolvedValue({ data: [{ id: "d1", tipo: "fur", filename: "f.pdf" }] });
  });

  it("AC1 — un cambio de color muestra el color del RUNT y el nuevo, cada uno rotulado", async () => {
    await renderConDetalle({
      ...BASE,
      color: "ROJO",
      runtSnapshot: { color: "PLATA" },
      transformacionesDeclaradas: { color: true },
    });

    expect(screen.getByText(BLOQUE)).toBeInTheDocument();
    expect(screen.getByText("PLATA")).toBeInTheDocument();
    expect(screen.getByText("ROJO")).toBeInTheDocument();
    expect(screen.getByText("En el RUNT")).toBeInTheDocument();
    expect(screen.getByText("Nuevo en el trámite")).toBeInTheDocument();
    // «Color» aparece UNA sola vez, como rótulo de la transformación. No se repite además como
    // especificación suelta: ahí el valor nuevo parecería el dato oficial del vehículo.
    expect(screen.getAllByText("Color")).toHaveLength(1);
    // Y la ficha del trámite la anuncia como lo que es: un cambio pedido, no un dato del vehículo.
    expect(screen.getByText("Cambio de color")).toBeInTheDocument();
  });

  it("AC2 — combustible y carrocería reciben el mismo tratamiento", async () => {
    await renderConDetalle({
      ...BASE,
      combustible: "GAS",
      carroceria: "ESTACAS",
      runtSnapshot: { combustible: "GASOLINA", carroceria: "SEDAN" },
    });

    expect(screen.getByText("GASOLINA")).toBeInTheDocument();
    expect(screen.getByText("GAS")).toBeInTheDocument();
    expect(screen.getByText("SEDAN")).toBeInTheDocument();
    expect(screen.getByText("ESTACAS")).toBeInTheDocument();
  });

  it("AC3 — sin transformación el atributo es un valor único, sin transición", async () => {
    await renderConDetalle({
      ...BASE,
      color: "PLATA",
      runtSnapshot: { color: "PLATA" },
    });

    expect(screen.queryByText(BLOQUE)).not.toBeInTheDocument();
    // Aparece como una especificación normal.
    expect(screen.getByText("Color")).toBeInTheDocument();
    expect(screen.getByText("PLATA")).toBeInTheDocument();
  });

  it("AC4 — dos transformaciones en el mismo trámite se muestran ambas", async () => {
    await renderConDetalle({
      ...BASE,
      color: "ROJO",
      combustible: "GAS",
      runtSnapshot: { color: "PLATA", combustible: "GASOLINA" },
    });

    expect(screen.getByText("Color")).toBeInTheDocument();
    expect(screen.getByText("Combustible")).toBeInTheDocument();
    expect(screen.getByText("PLATA")).toBeInTheDocument();
    expect(screen.getByText("GASOLINA")).toBeInTheDocument();
  });

  it("AC5 — sin valor del RUNT lo dice, y no hace pasar el nuevo por el del RUNT", async () => {
    await renderConDetalle({
      ...BASE,
      color: "ROJO",
      runtSnapshot: null,
      transformacionesDeclaradas: { color: true },
    });

    expect(screen.getByText(BLOQUE)).toBeInTheDocument();
    expect(screen.getByText("Sin dato del RUNT")).toBeInTheDocument();
    expect(screen.getByText("ROJO")).toBeInTheDocument();
  });
});
