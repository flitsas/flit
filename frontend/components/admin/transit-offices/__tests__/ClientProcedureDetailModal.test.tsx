// HU #11930 — el detalle del trámite del OT es un modal, no un panel lateral.
// HU #12060 — y su cuerpo pasa a ser TRES acordeones independientes, con armazón propia: se fueron
// la navegación por pasos, la tarjeta lateral del vehículo y la sección «Datos comerciales».
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProcedureDetailModal } from "../ClientProcedureDetailModal";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { formatFechaHora } from "@/lib/format/date";

const fetchOtClientProcedure = vi.fn();
const fetchOtDocuments = vi.fn();

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedure: (...args: unknown[]) => fetchOtClientProcedure(...args),
  fetchOtDocuments: (...args: unknown[]) => fetchOtDocuments(...args),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
}));

const PROCEDURE: OtClientProcedure = {
  id: "proc-1",
  clientTenantId: "tenant-1",
  procedureTypeId: "tipo-1",
  procedureTypeName: "Traspaso",
  clientTenantName: "Empresa Demo",
  referenceNumber: "RAD-0001",
  status: "entregado",
  soatEstado: "vigente",
  createdAt: "2026-08-01T00:00:00Z",
  placa: "ABC123",
  vin: "VIN-9",
  marca: "Renault",
  linea: "Logan",
  clase: "AUTOMOVIL",
  cilindraje: "1600",
  numeroMotor: "MOT-1",
  actors: [
    { actorType: "comprador", documentType: "CC", documentNumber: "1", fullName: "Ana Compradora" },
    { actorType: "vendedor", documentType: "CC", documentNumber: "2", fullName: "Beto Vendedor" },
  ],
  comercial: { valorVenta: 45000000, causal: "COMPRAVENTA", metodoPago: "TRANSFERENCIA" },
};

const ACORDEONES = [
  "Detalles del trámite y vehículo",
      "Actores del trámite",
  "Documentos del Trámite",
];

function renderModal(procedure: OtClientProcedure = PROCEDURE, onClose = vi.fn()) {
  render(
    // El acordeón de documentos emite avisos (toast) al abrir el consolidado.
    <ToastProvider>
      <ClientProcedureDetailModal open procedure={procedure} onClose={onClose} />
    </ToastProvider>,
  );
  return { onClose };
}

/** Botón que despliega un acordeón, buscado por su rótulo exacto. */
function acordeon(titulo: string) {
  return screen.getByRole("button", { name: titulo });
}

describe("ClientProcedureDetailModal (HU #11930 · rediseño HU #12060)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchOtClientProcedure.mockResolvedValue(PROCEDURE);
    fetchOtDocuments.mockResolvedValue({ data: [{ id: "d1", tipo: "fur", filename: "fur.pdf" }] });
  });

  it("AC1 — ninguna pieza del detalle del OT importa del detalle del gestor (salvo timeline HU #14157)", () => {
    // Guarda de aislamiento: el valor de este rediseño es que el gestor NO se mueva cuando se mueva
    // el OT, y eso solo se sostiene si no queda ni un import cruzado. Se comprueba sobre el código
    // fuente porque es una propiedad del módulo, no algo observable en el DOM.
    // HU #14157 — excepción explícita: TimelineTrackPanel + timeline-mappers (paridad de línea de tiempo).
    const permitidos =
      /operacion\/detalle\/(TimelineTrackPanel|timeline-mappers)["']/;
    const raiz = join(__dirname, "..");
    const ficheros = [
      join(raiz, "ClientProcedureDetailModal.tsx"),
      ...readdirSync(join(raiz, "detalle"))
        .filter((f) => f.endsWith(".tsx") || f.endsWith(".ts"))
        .map((f) => join(raiz, "detalle", f)),
    ];

    const culpables = ficheros.filter((f) => {
      const src = readFileSync(f, "utf8");
      const matches = src.match(/from\s+["'][^"']*operacion\/detalle[^"']*["']/g) ?? [];
      return matches.some((m) => !permitidos.test(m));
    });
    expect(culpables).toEqual([]);
  });

  it("AC3 — el cuerpo son los tres acordeones del prototipo", async () => {
    renderModal();

    await screen.findByRole("dialog");
    for (const titulo of ACORDEONES) {
      expect(acordeon(titulo)).toBeInTheDocument();
    }
  });

  it("AC4 — no queda navegación por pasos ni tarjeta lateral del vehículo", async () => {
    renderModal();

    await screen.findByRole("dialog");
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
    expect(screen.queryAllByRole("tablist")).toHaveLength(0);
    expect(screen.queryByRole("complementary", { name: /vehículo/i })).not.toBeInTheDocument();
  });

  it("AC5 — la sección «Datos comerciales» desapareció del detalle", async () => {
    renderModal();

    await screen.findByRole("dialog");
    // El trámite de la prueba SÍ trae datos comerciales: si la sección existiera, se vería.
    expect(screen.queryByText(/datos comerciales/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/operación comercial/i)).not.toBeInTheDocument();
    expect(screen.queryByText("Compraventa")).not.toBeInTheDocument();
  });

  it("AC6 — cada acordeón se abre y cierra sin afectar a los otros dos", async () => {
    renderModal();
    const user = userEvent.setup();

    // Se entró por el detalle: abre el primero y solo el primero.
    await waitFor(() => expect(acordeon(ACORDEONES[0]!)).toHaveAttribute("aria-expanded", "true"));
    expect(acordeon(ACORDEONES[1]!)).toHaveAttribute("aria-expanded", "false");
    expect(acordeon(ACORDEONES[2]!)).toHaveAttribute("aria-expanded", "false");

    // Abrir el de actores no cierra el de vehículo: se pueden cotejar a la vez.
    await user.click(acordeon(ACORDEONES[1]!));
    expect(acordeon(ACORDEONES[0]!)).toHaveAttribute("aria-expanded", "true");
    expect(acordeon(ACORDEONES[1]!)).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("Ana Compradora")).toBeInTheDocument();

    // Y cerrar el primero deja al segundo abierto.
    await user.click(acordeon(ACORDEONES[0]!));
    expect(acordeon(ACORDEONES[0]!)).toHaveAttribute("aria-expanded", "false");
    expect(acordeon(ACORDEONES[1]!)).toHaveAttribute("aria-expanded", "true");
    expect(screen.queryByRole("table", { name: "Datos del trámite" })).not.toBeInTheDocument();
  });

  it("se presenta como diálogo con radicado, tipo, placa y estado en el encabezado", async () => {
    renderModal();

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("heading", { level: 2 })).toHaveTextContent(
      "Gestión y Aprobación del Trámite",
    );
    expect(screen.getAllByText("RAD-0001").length).toBeGreaterThan(0);
    expect(dialog).toHaveTextContent("ABC123");
    expect(dialog).toHaveTextContent("VIN-9");
    // ADR-0059 — el organismo ve el nombre real del estado, sin sufijo «OT».
    expect(screen.getAllByText("Entregado").length).toBeGreaterThan(0);
  });

  it("muestra las especificaciones técnicas y omite las que el trámite no tiene", async () => {
    renderModal();

    // La ficha se reparte en tandas de cuatro columnas; basta con que exista.
    expect(
      (await screen.findAllByRole("table", { name: /^Especificaciones técnicas/ })).length,
    ).toBeGreaterThan(0);
    expect(screen.getByText("1600 cc")).toBeInTheDocument();
    expect(screen.getByText("MOT-1")).toBeInTheDocument();
    // Sin número de chasis capturado, la fila no se pinta (no se inventa ni se rellena con otra).
    expect(screen.queryByText("Nº Chasis")).not.toBeInTheDocument();
  });

  it("cierra con el control de cierre y con Escape", async () => {
    const { onClose } = renderModal();
    const user = userEvent.setup();

    await user.click(await screen.findByRole("button", { name: "Cerrar" }));
    expect(onClose).toHaveBeenCalledTimes(1);

    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it("abre el acordeón de documentos cuando se entra por «ver documentos»", async () => {
    render(
      <ToastProvider>
        <ClientProcedureDetailModal
          open
          procedure={PROCEDURE}
          onClose={vi.fn()}
          initialSection="documentos"
        />
      </ToastProvider>,
    );

    expect(await screen.findByTestId("ot-detalle-documentos")).toBeInTheDocument();
    await waitFor(() => expect(acordeon(ACORDEONES[2]!)).toHaveAttribute("aria-expanded", "true"));
    expect(acordeon(ACORDEONES[0]!)).toHaveAttribute("aria-expanded", "false");
  });

  it("Bug #11585 — sin pendientes no monta el bloque de pendientes", async () => {
    renderModal();

    await screen.findByRole("table", { name: "Datos del trámite" });
    expect(screen.queryByText(/Pendientes antes de decidir/)).not.toBeInTheDocument();
  });

  it("Bug #11585 — con expediente vacío y SOAT no vigente sí los enumera", async () => {
    const conPendientes = { ...PROCEDURE, soatEstado: "vencido" };
    fetchOtClientProcedure.mockResolvedValue(conPendientes);
    fetchOtDocuments.mockResolvedValue({ data: [] });
    renderModal(conPendientes);

    expect(await screen.findByText(/Pendientes antes de decidir/)).toBeInTheDocument();
    expect(screen.getByText(/SOAT RUNT no vigente/i)).toBeInTheDocument();
    expect(screen.getByText(/expediente aún no tiene documentos/i)).toBeInTheDocument();
  });

  it("si el refresco del detalle falla, avisa y conserva los datos de la bandeja", async () => {
    fetchOtClientProcedure.mockRejectedValue(new Error("boom"));
    renderModal();

    await waitFor(() =>
      expect(screen.getByText(/se muestran los datos de la bandeja/i)).toBeInTheDocument(),
    );
    expect(screen.getAllByText("RAD-0001").length).toBeGreaterThan(0);
  });

  it("HU #14157 AC2 — botón Línea de Tiempo muestra el panel y al desactivar lo oculta", async () => {
    const conHistorial: OtClientProcedure = {
      ...PROCEDURE,
      statusHistory: [
        {
          fromStatus: "entregado",
          toStatus: "aprobado",
          changedAt: "2026-10-28T15:21:00Z",
          reason: "Aprobado por OT",
          changedByName: "Jose Manuel Pimienta",
          changedByEmail: "jose.pimienta@flitsas.com",
          changedByCompania: "Empresa Demo FLIT",
        },
      ],
    };
    fetchOtClientProcedure.mockResolvedValue(conHistorial);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <ClientProcedureDetailModal open procedure={conHistorial} onClose={vi.fn()} />
      </ToastProvider>,
    );

    await screen.findByRole("dialog");
    expect(screen.queryByText("Línea de tiempo del trámite")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Línea de Tiempo del Trámite" }));
    expect(await screen.findByText("Línea de tiempo del trámite")).toBeInTheDocument();
    expect(screen.getByText("Jose Manuel Pimienta")).toBeInTheDocument();
    expect(screen.getByText("jose.pimienta@flitsas.com")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Línea de Tiempo del Trámite" }));
    expect(screen.queryByText("Línea de tiempo del trámite")).not.toBeInTheDocument();
    expect(acordeon(ACORDEONES[0]!)).toBeInTheDocument();
  });

  it("HU #14157 AC1 — initialPanel timeline abre con el historial visible", async () => {
    const conHistorial: OtClientProcedure = {
      ...PROCEDURE,
      statusHistory: [
        {
          fromStatus: null,
          toStatus: "borrador",
          changedAt: "2026-10-28T15:21:00Z",
          reason: "Borrador",
          changedByName: "Jose Manuel Pimienta",
          changedByEmail: "jose.pimienta@flitsas.com",
          changedByCompania: "Empresa Demo FLIT",
        },
      ],
    };
    fetchOtClientProcedure.mockResolvedValue(conHistorial);
    render(
      <ToastProvider>
        <ClientProcedureDetailModal
          open
          procedure={conHistorial}
          onClose={vi.fn()}
          initialPanel="timeline"
        />
      </ToastProvider>,
    );

    expect(await screen.findByText("Línea de tiempo del trámite")).toBeInTheDocument();
    expect(screen.getByText("Jose Manuel Pimienta")).toBeInTheDocument();
  });

  it("HU #14157 AC4 — historial vacío muestra estado vacío claro", async () => {
    const sinHistorial = { ...PROCEDURE, statusHistory: [] };
    fetchOtClientProcedure.mockResolvedValue(sinHistorial);
    const user = userEvent.setup();
    render(
      <ToastProvider>
        <ClientProcedureDetailModal open procedure={sinHistorial} onClose={vi.fn()} />
      </ToastProvider>,
    );

    await user.click(await screen.findByRole("button", { name: "Línea de Tiempo del Trámite" }));
    expect(
      await screen.findByText(/aún no tiene cambios de estado registrados/i),
    ).toBeInTheDocument();
  });

  it("HU #14157 AC3 — fallo al cargar detalle con timeline muestra error recuperable", async () => {
    fetchOtClientProcedure.mockRejectedValue(new Error("boom"));
    render(
      <ToastProvider>
        <ClientProcedureDetailModal
          open
          procedure={PROCEDURE}
          onClose={vi.fn()}
          initialPanel="timeline"
        />
      </ToastProvider>,
    );

    expect(await screen.findByText(/No se pudo cargar el historial de estados/i)).toBeInTheDocument();
    expect(acordeon(ACORDEONES[0]!)).toBeInTheDocument();
  });

  it("HU #14182 AC1 — un trámite visible muestra sus modificaciones, la más reciente primero", async () => {
    const reciente = "2026-10-28T20:21:00.000Z";
    const enBorrador = "2026-10-02T13:00:00.000Z";
    const conMods: OtClientProcedure = {
      ...PROCEDURE,
      statusHistory: [
        {
          fromStatus: "borrador",
          toStatus: "entregado",
          changedAt: reciente,
          reason: "Enviado",
          changedByName: "Solo En Timeline",
          changedByEmail: "solo-timeline@flit.test",
          changedByCompania: "OT Demo",
        },
      ],
      modificaciones: [
        {
          etiqueta: "Estado",
          campo: "estado",
          anterior: "borrador",
          nuevo: "entregado",
          changedAt: reciente,
          actorNombre: "Ana Gómez",
          actorCompania: "Empresa Demo",
        },
        {
          etiqueta: "Color",
          campo: "color",
          anterior: "",
          nuevo: "Rojo",
          changedAt: enBorrador,
          actorNombre: null,
          actorCompania: null,
        },
      ],
    };
    fetchOtClientProcedure.mockResolvedValue(conMods);
    render(
      <ToastProvider>
        <ClientProcedureDetailModal
          open
          procedure={conMods}
          onClose={vi.fn()}
          initialPanel="timeline"
        />
      </ToastProvider>,
    );

    const lista = await screen.findByRole("list", { name: "Línea de tiempo del trámite" });
    const items = within(lista).getAllByRole("listitem");
    expect(items).toHaveLength(2);

    // El carril va del más antiguo al vigente, con las mismas tarjetas del gestor.
    expect(items[0]).toHaveTextContent("Color");
    expect(items[0]).toHaveTextContent("Gestor");
    expect(items[0]).toHaveTextContent("Fecha y hora");
    expect(items[0]).toHaveTextContent(formatFechaHora(enBorrador));
    expect(items[0]).toHaveTextContent("Sistema");
    expect(items[0]).toHaveTextContent("Rojo");

    expect(items[1]).toHaveTextContent("Entregado");
    expect(items[1]).toHaveTextContent("Ana Gómez");
    expect(items[1]).toHaveTextContent("Empresa Demo");
    expect(items[1]).toHaveTextContent("Entregado desde Borrador");
    expect(items[1]).toHaveTextContent("Vigente");

    // La misma transición ya está en modificaciones: el carril de estados no la repite.
    expect(screen.queryByText("solo-timeline@flit.test")).not.toBeInTheDocument();
    expect(screen.queryByText("Solo En Timeline")).not.toBeInTheDocument();
  });

  it("HU #14182 AC2 — sin modificaciones no hay entradas ni una consulta de historial aparte", async () => {
    const sinMods: OtClientProcedure = { ...PROCEDURE, statusHistory: [], modificaciones: [] };
    fetchOtClientProcedure.mockResolvedValue(sinMods);
    render(
      <ToastProvider>
        <ClientProcedureDetailModal
          open
          procedure={sinMods}
          onClose={vi.fn()}
          initialPanel="timeline"
        />
      </ToastProvider>,
    );

    expect(await screen.findByText("Todavía no hay modificaciones")).toBeInTheDocument();
    expect(
      screen.queryByRole("list", { name: "Modificaciones del trámite" }),
    ).not.toBeInTheDocument();

    const src = readFileSync(join(__dirname, "../ClientProcedureDetailModal.tsx"), "utf8");
    expect(src).not.toMatch(/status-history/);
    expect(JSON.stringify(fetchOtClientProcedure.mock.calls)).not.toMatch(/status-history/i);
    expect(JSON.stringify(fetchOtDocuments.mock.calls)).not.toMatch(/status-history/i);
  });
});
