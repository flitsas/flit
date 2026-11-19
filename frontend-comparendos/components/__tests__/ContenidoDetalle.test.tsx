import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { ContenidoDetalle } from "@/components/detalle/ContenidoDetalle";
import { ToastProvider } from "@/components/Toast";
import type { DetalleComparendo } from "@/lib/tipos";

// Rediseño S1: el detalle como página. Nada sale a la red: la API está simulada.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));
const sesion = { current: { usuario: { id: "u1", esSuperAdmin: false }, permisos: ["comparendos.comparendo.observar"], empresa: null } as unknown };
vi.mock("@/components/ComparendosShell", () => ({ useSesion: () => ({ sesion: sesion.current, cargando: false, error: null }) }));
vi.mock("next/link", () => ({ default: ({ href, children, ...p }: { href: string; children: React.ReactNode }) => <a href={href} {...p}>{children}</a> }));

const detalle = {
  id: "c1",
  numero: "11001000000012345",
  placa: "ABC123",
  estadoProceso: "SinProcesar",
  ultimoHito: "Ninguno",
  situacion: { estadoLegal: "EnTiempo", diasHabilesRestantes: 9, proximoAVencer: false, ventanaDescuento: "Descuento50", finDescuentoVigente: "2026-12-12", diasHabilesDescuento: 6 },
  plazos: { limiteGestion: null, limite50: null, limite25: null, caducidad: null, prescripcion: null },
  fuente: "Simit",
  fuentePrincipal: null,
  vehiculo: { placa: "ABC123", tipoVehiculo: "Automóvil", servicioVehiculo: "Particular" },
  compania: { nitConsultado: null, titular: null, tipoDocumentoInfractor: null, numeroDocumentoInfractor: null, nitMonitoreado: null, placaMonitoreada: null },
  infraccion: { codigo: "C29", descripcion: "Exceso de velocidad", direccion: null, fechaImposicion: "2026-11-01", horaImposicion: null, fechaNotificacion: null, esFotomulta: true },
  organismo: { id: "o1", codigo: "11001", nombre: "Secretaría de Movilidad", municipio: "Bogotá", departamento: null, tieneCorreo: true },
  secretariaTexto: null,
  valores: { valorCapital: 600000, valorAdicional: 0, valorTotal: 650000 },
  estadoFuente: null,
  enCobroCoactivo: true,
  evidencia: { archivoEvidenciaId: null, fotoEvidenciaUrl: null },
  activo: true,
  motivoInactivacion: null,
  detalleInactivacion: null,
  estadoCambioContraventor: "SinSolicitud",
  estadoConsultaContraventor: "Error",
  errorConsultaContraventor: "respuesta cruda",
  pagado: false,
  contraventor: null,
  historialContraventor: [],
  documentos: [],
  solicitudesCambio: [],
  dp: null,
  pago: null,
  fuentes: [],
} as unknown as DetalleComparendo;

const extensiones = { acciones: () => <button type="button">Enviar aviso</button>, dp: () => <p>extension DP</p> };

function montar(props: Partial<Parameters<typeof ContenidoDetalle>[0]> = {}) {
  return render(
    <ToastProvider>
      <ContenidoDetalle id="c1" extensiones={extensiones} {...props} />
    </ToastProvider>,
  );
}

beforeEach(() => {
  sesion.current = { usuario: { id: "u1", esSuperAdmin: false }, permisos: ["comparendos.comparendo.observar"], empresa: null };
  apiFetch.mockImplementation((ruta: string) => {
    if (ruta === "/comparendos/c1") return Promise.resolve(detalle);
    if (ruta.endsWith("/timeline")) return Promise.resolve([]);
    if (ruta.endsWith("/cambios")) return Promise.resolve([]);
    return Promise.reject(new Error(`ruta inesperada ${ruta}`));
  });
  Element.prototype.scrollIntoView = vi.fn();
});
afterEach(() => {
  cleanup();
  apiFetch.mockReset();
});

describe("detalle del comparendo como página (rediseño S1)", () => {
  it("ordena cabecera → acciones → tarjetas → línea de tiempo → pestañas", async () => {
    montar();
    const cabecera = await screen.findByTestId("detalle-cabecera");
    const orden = ["detalle-cabecera", "detalle-tarjetas", "detalle-timeline", "detalle-gestion", "detalle-pestanas"].map((t) => screen.getByTestId(t));
    for (let i = 1; i < orden.length; i++) expect(orden[i - 1].compareDocumentPosition(orden[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(cabecera).toHaveTextContent("11001000000012345");
    expect(cabecera).toHaveTextContent("C29 — Exceso de velocidad");
    expect(cabecera).toHaveTextContent("Cobro coactivo");
    expect(cabecera).toHaveTextContent("6 días hábiles");
    expect(screen.getByTestId("detalle-acciones")).toHaveTextContent("Enviar aviso");
  });

  it("el SuperAdmin solo lee: sin acciones ni extensiones, pero ve lo técnico", async () => {
    sesion.current = { usuario: { id: "u0", esSuperAdmin: true }, permisos: [], empresa: null };
    montar({ seccionInicial: "dp" });
    await screen.findByTestId("detalle-cabecera");
    expect(screen.queryByTestId("detalle-acciones")).toBeNull();
    expect(screen.queryByText("extension DP")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Contraventor" }));
    expect(screen.getByText("Qué respondió la consulta (solo FLIT)")).toBeInTheDocument();
  });

  it("la gestora no ve la respuesta técnica de la consulta", async () => {
    montar({ seccionInicial: "contraventor" });
    await screen.findByTestId("detalle-cabecera");
    expect(screen.queryByText("Qué respondió la consulta (solo FLIT)")).toBeNull();
  });

  it("la sección inicial llega abierta y las demás cerradas, sin montar sus extensiones", async () => {
    montar({ seccionInicial: "dp" });
    expect(await screen.findByText("extension DP")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Pago" })).toHaveAttribute("aria-expanded", "false");
  });

  it("la tarjeta de DP abre su sección", async () => {
    montar();
    await screen.findByTestId("detalle-cabecera");
    expect(screen.queryByText("extension DP")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Ir al derecho de petición" }));
    expect(await screen.findByText("extension DP")).toBeInTheDocument();
  });

  it("el historial de cambios se pide solo al abrir su pestaña", async () => {
    montar();
    await screen.findByTestId("detalle-cabecera");
    expect(apiFetch.mock.calls.some(([r]) => String(r).endsWith("/cambios"))).toBe(false);
    fireEvent.click(screen.getByRole("tab", { name: /Historial de cambios/ }));
    await waitFor(() => expect(apiFetch.mock.calls.some(([r]) => String(r).endsWith("/cambios"))).toBe(true));
  });
});
