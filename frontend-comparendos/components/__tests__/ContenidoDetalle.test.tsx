import { useEffect } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ContenidoDetalle } from "@/components/detalle/ContenidoDetalle";
import { ToastProvider } from "@/components/Toast";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
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

// Las extensiones cuentan cuántas veces se montan: así se ve que se montan al acercarse y no al cargar.
const montajes = { dp: 0, pago: 0 };
function ExtDp({ ctx }: { ctx: ContextoDetalle }) {
  useEffect(() => {
    montajes.dp++;
  }, []);
  return (
    <p>
      extension DP
      <button type="button" onClick={ctx.recargar}>
        recargar desde DP
      </button>
    </p>
  );
}
function ExtPago() {
  useEffect(() => {
    montajes.pago++;
  }, []);
  return <p>extension Pago</p>;
}
const extensiones = { acciones: () => <button type="button">Enviar aviso</button>, dp: (ctx: ContextoDetalle) => <ExtDp ctx={ctx} />, pago: () => <ExtPago /> };

// IntersectionObserver simulado: la prueba decide qué secciones «se acercan».
const observadores: { cb: IntersectionObserverCallback; els: Element[] }[] = [];
class IOFalso {
  els: Element[] = [];
  constructor(public cb: IntersectionObserverCallback) {
    observadores.push(this);
  }
  observe(el: Element) {
    this.els.push(el);
  }
  disconnect() {}
  unobserve() {}
  takeRecords() {
    return [];
  }
}
function acercar(el: Element) {
  act(() => {
    for (const o of observadores) if (o.els.includes(el)) o.cb([{ target: el, isIntersecting: true } as IntersectionObserverEntry], o as unknown as IntersectionObserver);
  });
}

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
  observadores.length = 0;
  montajes.dp = 0;
  montajes.pago = 0;
  vi.stubGlobal("IntersectionObserver", IOFalso);
});
afterEach(() => {
  cleanup();
  apiFetch.mockReset();
  vi.unstubAllGlobals();
});

describe("detalle del comparendo como página (rediseño S1)", () => {
  it("una sola columna: cabecera → barra → resumen → línea de tiempo → contraventor → DP → pago → datos", async () => {
    montar();
    const cabecera = await screen.findByTestId("detalle-cabecera");
    const orden = ["detalle-cabecera", "detalle-barra", "detalle-tarjetas", "detalle-timeline", "detalle-contraventor", "detalle-dp", "detalle-pago", "detalle-pestanas"].map((t) =>
      screen.getByTestId(t),
    );
    for (let i = 1; i < orden.length; i++) expect(orden[i - 1].compareDocumentPosition(orden[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(cabecera).toHaveTextContent("11001000000012345");
    expect(cabecera).toHaveTextContent("C29 — Exceso de velocidad");
    expect(cabecera).toHaveTextContent("Cobro coactivo");
    expect(cabecera).toHaveTextContent("6 días hábiles");
    expect(screen.getByTestId("detalle-acciones")).toHaveTextContent("Enviar aviso");
    // Siempre abiertas: los datos de cada sección se ven sin hacer clic, y ninguna se pliega.
    expect(within(screen.getByTestId("detalle-pago")).getByText("Sin pago registrado.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { expanded: false, name: "Pago" })).toBeNull();
  });

  it("las acciones de DP y Pago no se montan al cargar: se montan al acercarse su sección", async () => {
    montar();
    await screen.findByTestId("detalle-cabecera");
    expect(montajes).toEqual({ dp: 0, pago: 0 });
    const dp = screen.getByTestId("detalle-dp").querySelector("[data-acciones-montadas]")!;
    expect(dp).toHaveAttribute("data-acciones-montadas", "no");
    acercar(dp);
    expect(await screen.findByText("extension DP")).toBeInTheDocument();
    expect(montajes).toEqual({ dp: 1, pago: 0 });
  });

  it("?seccion=dp monta ya las acciones de DP, ubica la sección y deja el foco en su título", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    montar({ seccionInicial: "dp" });
    expect(await screen.findByText("extension DP")).toBeInTheDocument();
    expect(montajes.pago).toBe(0);
    await act(async () => vi.advanceTimersByTime(10));
    expect(document.activeElement?.id).toBe("detalle-dp-titulo");
    vi.useRealTimers();
  });

  it("la barra lleva a la sección y la marca como la que se está viendo", async () => {
    montar();
    await screen.findByTestId("detalle-cabecera");
    const barra = screen.getByTestId("detalle-barra");
    fireEvent.click(within(barra).getByRole("button", { name: /Pago/ }));
    expect(within(barra).getByRole("button", { name: /Pago/ })).toHaveAttribute("aria-current", "location");
    expect(await screen.findByText("extension Pago")).toBeInTheDocument();
  });

  it("la línea de tiempo muestra las 8 más recientes y «Ver los N» trae el resto", async () => {
    const eventos = Array.from({ length: 11 }, (_, i) => ({ id: `e${i}`, tipo: "observacion", ocurridoEn: `2026-11-${String(11 - i).padStart(2, "0")}T10:00:00Z`, actorTipo: "Usuario", actorNombre: "ana", descripcion: `evento ${i}`, relacionadoTipo: null, relacionadoId: null }));
    apiFetch.mockImplementation((ruta: string) => {
      if (ruta === "/comparendos/c1") return Promise.resolve(detalle);
      if (ruta.endsWith("/timeline")) return Promise.resolve(eventos);
      return Promise.resolve([]);
    });
    montar();
    await screen.findByText("evento 7");
    expect(screen.queryByText("evento 8")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Ver los 11 eventos" }));
    expect(screen.getByText("evento 10")).toBeInTheDocument();
  });

  it("dos saltos seguidos terminan en el último: el primero no la devuelve", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    montar();
    await screen.findByTestId("detalle-cabecera");
    const barra = screen.getByTestId("detalle-barra");
    const scroll = Element.prototype.scrollIntoView as unknown as ReturnType<typeof vi.fn>;
    scroll.mockClear();
    fireEvent.click(within(barra).getByRole("button", { name: /DP/ }));
    fireEvent.click(within(barra).getByRole("button", { name: /Pago/ }));
    await act(async () => vi.advanceTimersByTime(2000));
    const destinos = scroll.mock.contexts.map((el) => (el as Element).id);
    expect(destinos).toContain("detalle-pago");
    expect(destinos).not.toContain("detalle-dp");
    vi.useRealTimers();
  });

  it("si la gestora mueve la rueda, no se re-ubica después", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    montar();
    await screen.findByTestId("detalle-cabecera");
    // La sección quedó corrida 500 px: sin gesto, se re-ubicaría a los 700 ms.
    const dp = screen.getByTestId("detalle-dp");
    dp.getBoundingClientRect = () => ({ top: 500 }) as DOMRect;
    const scroll = Element.prototype.scrollIntoView as unknown as ReturnType<typeof vi.fn>;
    fireEvent.click(within(screen.getByTestId("detalle-barra")).getByRole("button", { name: /DP/ }));
    await act(async () => vi.advanceTimersByTime(10));
    scroll.mockClear();
    fireEvent.wheel(window);
    await act(async () => vi.advanceTimersByTime(2000));
    expect(scroll).not.toHaveBeenCalled();
    vi.useRealTimers();
  });

  it("recargar el detalle no vuelve a montar las acciones (no repite llamadas)", async () => {
    montar({ seccionInicial: "dp" });
    await screen.findByText("extension DP");
    fireEvent.click(screen.getByRole("button", { name: "recargar desde DP" }));
    await waitFor(() => expect(apiFetch.mock.calls.filter(([r]) => r === "/comparendos/c1").length).toBe(2));
    expect(montajes.dp).toBe(1);
  });

  it("los historiales largos muestran los primeros y «Ver los N»", async () => {
    detalle.historialContraventor = Array.from({ length: 4 }, (_, i) => ({ id: `h${i}`, origen: "Manual", motivo: null, datosAnteriores: null, datosNuevos: JSON.stringify({ nombres: `Conductor ${i}` }), actorNombre: null, registradoEn: "2026-11-01T10:00:00Z" }));
    try {
      montar();
      await screen.findByText(/Conductor 1/);
      expect(screen.queryByText(/Conductor 2/)).toBeNull();
      fireEvent.click(screen.getByRole("button", { name: "Ver los 4 cambios de conductor" }));
      expect(screen.getByText(/Conductor 3/)).toBeInTheDocument();
    } finally {
      detalle.historialContraventor = [];
    }
  });

  it("los estados largos van como texto que parte línea, no como pill", async () => {
    detalle.contraventor = { nombres: "Ana", tipoDocumento: "CC", numeroDocumento: "", documentoPendiente: true, correo: null, telefono: null, direccion: null, ciudad: null, sucursal: null, tipoContrato: null, origen: "Api" } as unknown as DetalleComparendo["contraventor"];
    try {
    montar();
    const texto = await screen.findByText("Pendiente: la consulta automática no lo trae");
    expect(texto.closest("[data-estado-texto]")).not.toBeNull();
    expect(texto.closest(".kit-etiqueta")).toBeNull();
    } finally {
      detalle.contraventor = null;
    }
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
