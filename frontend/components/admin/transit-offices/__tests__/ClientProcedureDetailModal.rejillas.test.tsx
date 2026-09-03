// HU #12061 — el detalle del OT presenta la información con las rejillas del prototipo: ficha del
// trámite, ficha del vehículo, tabla de actores y rejilla de documentos con el consolidado
// destacado. La regla de datos acordada es «lo que no tengamos, no se pone»: ni un rótulo vacío ni
// un valor tomado prestado de otro campo.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
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

vi.mock("@/lib/api/download", () => ({ downloadFile: vi.fn() }));

const PROCEDURE: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "tenant-1",
  procedureTypeId: "tipo-1",
  procedureTypeName: "Traspaso",
  clientTenantName: "Renting Colombia S.A.",
  gestorNombre: "Laura Gil",
  referenceNumber: "OT-2026-10021",
  status: "entregado",
  plateFlowStatus: null,
  soatEstado: "vigente",
  createdAt: "2026-08-01T00:00:00Z",
  placa: "ABC123",
  vin: "3FA6P0H75HR123456",
  marca: "Mazda",
  linea: "CX-5 Grand Touring",
  clase: "Automóvil",
  color: "Gris Meteoro",
  // Sin cilindraje, sin ejes y sin números de motor/chasis/serie: no deben pintarse.
  actors: [
    {
      actorType: "vendedor",
      documentType: "C.C.",
      documentNumber: "43115882",
      fullName: "María Elena Restrepo",
      email: "maria@example.com",
    },
    {
      actorType: "comprador",
      documentType: "C.C.",
      documentNumber: "122332321",
      fullName: "Juan Carlos Pérez Gómez",
      personType: "natural",
    },
  ],
};

const ADJUNTOS = [
  {
    id: "d1",
    tipo: "fur",
    filename: "formulario-unico.pdf",
    mimetype: "application/pdf",
    sizeBytes: 2048,
    sha256: "a",
    source: "upload",
    uploadedAt: "2026-08-02T10:00:00Z",
  },
];

function abrir(procedure: OtClientProcedure = PROCEDURE) {
  render(
    <ToastProvider>
      <ClientProcedureDetailModal open procedure={procedure} onClose={vi.fn()} />
    </ToastProvider>,
  );
}

/** Todos los rótulos de columna de una rejilla, en orden. */
function columnas(tabla: HTMLElement): string[] {
  return within(tabla)
    .getAllByRole("columnheader")
    .map((c) => c.textContent?.trim() ?? "");
}

describe("Detalle OT — presentación con las rejillas del prototipo (HU #12061)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchOtClientProcedure.mockResolvedValue(PROCEDURE);
    fetchOtDocuments.mockResolvedValue({ data: ADJUNTOS });
  });

  it("AC1 — la ficha del trámite es una rejilla con las cinco columnas del prototipo", async () => {
    abrir();

    const ficha = await screen.findByRole("table", { name: "Datos del trámite" });
    expect(columnas(ficha)).toEqual([
      "Radicado",
      "Fecha radicación",
      "Empresa / Gestor",
      "Tipo trámite solicitado",
      "Transformaciones solicitadas",
    ]);
    expect(within(ficha).getByText("OT-2026-10021")).toBeInTheDocument();
    // Empresa y gestor comparten celda, como en el prototipo.
    expect(within(ficha).getByText("Renting Colombia S.A. · Laura Gil")).toBeInTheDocument();
    expect(within(ficha).getByText("Traspaso")).toBeInTheDocument();
    expect(within(ficha).getByText("Ninguna")).toBeInTheDocument();
  });

  it("AC2 — las características del vehículo van en fichas de rótulo y valor", async () => {
    abrir();

    const tandas = await screen.findAllByRole("table", {
      name: /^Especificaciones del vehículo/,
    });
    const rotulos = tandas.flatMap(columnas);

    // El orden del prototipo: VIN, Placa, Marca, Línea… y después el resto.
    expect(rotulos.slice(0, 4)).toEqual(["VIN", "Placa", "Marca", "Línea"]);
    expect(screen.getByText("3FA6P0H75HR123456")).toBeInTheDocument();
    expect(screen.getByText("CX-5 Grand Touring")).toBeInTheDocument();
  });

  it("AC3 — un campo sin dato no se pinta, ni con rótulo vacío ni con el valor de otro", async () => {
    abrir();

    await screen.findByRole("table", { name: "Datos del trámite" });
    // El trámite no trae cilindraje, ejes ni identificadores mecánicos.
    for (const ausente of ["Cilindraje", "Ejes", "N. Motor", "N. Chasis", "N. Serie"]) {
      expect(screen.queryByText(ausente)).not.toBeInTheDocument();
    }
    // «Peso» sale en el prototipo pero no existe en el contrato del OT: tampoco se inventa.
    expect(screen.queryByText("Peso")).not.toBeInTheDocument();
  });

  it("AC4 — los actores se presentan en una tabla de tres columnas", async () => {
    abrir();
    const user = userEvent.setup();

    await screen.findByRole("table", { name: "Datos del trámite" });
    await user.click(screen.getByRole("button", { name: "Actores del Trámite" }));

    const tabla = await screen.findByRole("table", { name: "Actores del trámite" });
    expect(columnas(tabla)).toEqual(["Documento", "Nombre completo", "Tipo de actor"]);
    expect(within(tabla).getByText("C.C. 43115882")).toBeInTheDocument();
    expect(within(tabla).getByText("María Elena Restrepo")).toBeInTheDocument();
    expect(within(tabla).getByText("Vendedor")).toBeInTheDocument();
    // El contacto acompaña al nombre en vez de ocupar columna propia.
    expect(within(tabla).getByText("maria@example.com")).toBeInTheDocument();
    // Y lo que el OT no tiene no se finge: el mockup traía firma y validación inventadas.
    expect(screen.queryByText(/firma de la persona/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/validación/i)).not.toBeInTheDocument();
  });

  it("AC5 — los documentos van en rejilla y el consolidado aparece destacado", async () => {
    abrir();
    const user = userEvent.setup();

    await screen.findByRole("table", { name: "Datos del trámite" });
    await user.click(screen.getByRole("button", { name: "Documentos del Trámite" }));

    const panel = await screen.findByTestId("ot-detalle-documentos");
    expect(within(panel).getByText("formulario-unico.pdf")).toBeInTheDocument();
    expect(
      within(panel).getByRole("button", { name: "Previsualizar formulario-unico.pdf" }),
    ).toBeInTheDocument();
    expect(
      within(panel).getByRole("button", { name: "Descargar formulario-unico.pdf" }),
    ).toBeInTheDocument();

    // El consolidado es una tarjeta más de la rejilla, pero con entidad propia.
    expect(within(panel).getByText("Consolidado de documentos")).toBeInTheDocument();
    expect(
      within(panel).getByRole("button", { name: "Ver consolidado del expediente" }),
    ).toBeInTheDocument();
  });

  it("AC6 — las transformaciones RUNT vs nuevo sobreviven al rediseño", async () => {
    const conCambio: OtClientProcedure = {
      ...PROCEDURE,
      color: "ROJO",
      runtSnapshot: { color: "PLATA" },
      transformacionesDeclaradas: { color: true },
    };
    fetchOtClientProcedure.mockResolvedValue(conCambio);
    abrir(conCambio);

    const ficha = await screen.findByRole("table", { name: "Datos del trámite" });
    expect(within(ficha).getByText("Cambio de color")).toBeInTheDocument();

    expect(screen.getByText("Transformaciones declaradas frente al RUNT")).toBeInTheDocument();
    expect(screen.getByText("PLATA")).toBeInTheDocument();
    expect(screen.getByText("ROJO")).toBeInTheDocument();
    // El color transformado NO se repite además como característica suelta del vehículo.
    expect(screen.getAllByText("Color")).toHaveLength(1);
  });
});
