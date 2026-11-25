import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import Pagina from "@/app/(app)/ocr/paquetes/[id]/page";
import { ToastProvider } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS } from "@/components/ui/estilos";

// GDC-EVID §9: «Asociar los N» con la vista previa del servidor, tandas de máx. 50 hasta que no queden, resultado; y
// «Revisar reemplazos (n)» que abre el primero de la cola. El API de S1 se simula por ruta.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));
const push = vi.fn();
const replace = vi.fn();
let query = "";
vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "p1" }),
  useRouter: () => ({ push, replace, back: vi.fn() }),
  useSearchParams: () => new URLSearchParams(query),
  usePathname: () => "/ocr/paquetes/p1",
}));
let permisos = ["comparendos.ocr.ver", "comparendos.ocr.gestionar"];
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({ sesion: { usuario: { esSuperAdmin: false }, permisos }, cargando: false, error: null }),
}));

const PAQUETE = {
  id: "p1",
  nombre: "Escaneos noviembre",
  estado: "Procesado",
  cantidadDocumentos: 60,
  cantidadPendientes: 0,
  cantidadCompletos: 0,
  cantidadIncompletos: 0,
  cantidadFallidos: 0,
  cantidadDuplicados: 60,
  cantidadRegistrados: 0,
  cantidadAsociados: 0,
  cantidadDescartados: 0,
  creadoEn: "2026-11-01T10:00:00Z",
  procesamientoIniciadoEn: "2026-11-01T10:01:00Z",
  cerradoEn: null,
};
const VISTA = {
  asociables: 55,
  correos: { saldran: 53, conductores: 40, omitidos: [{ motivo: "El conductor no tiene correo registrado", cantidad: 2 }] },
  porRevisar: { reemplazos: 4, numeroDistinto: 1 },
};
const doc = (id: string, revision: string | null) => ({
  id, nombreArchivo: `${id}.pdf`, estado: "Duplicado", numeroComparendoExtraido: "1", camposFaltantes: [], tipoFallo: null, mensajeError: null,
  fuenteDuplicado: "SIMIT", comparendoId: null, tipoArchivo: "PDF", intentos: 1, procesadoEn: null, reprocesable: false, revision,
});
const asoc = (n: number, pref: string) => Array.from({ length: n }, (_, i) => ({ documentoId: `${pref}${i}`, comparendoId: "c", numero: "1", correo: { estado: "Pendiente", motivo: null } }));

let tandas: unknown[] = [];
function abrir(paquete: Record<string, unknown> = PAQUETE, vista: unknown = VISTA) {
  apiFetch.mockImplementation((ruta: string) => {
    if (ruta === "/ocr/paquetes/p1/asociacion-lote") return Promise.resolve(vista);
    if (ruta === "/ocr/paquetes/p1/asociar-lote") return Promise.resolve(tandas.shift());
    return Promise.resolve({ paquete, documentos: [doc("d1", null), doc("r1", "reemplazo"), doc("n1", "numeroDistinto"), doc("r2", "reemplazo")], completosPorConfirmar: 0 });
  });
  render(
    <ToastProvider>
      <Pagina />
    </ToastProvider>,
  );
}
const posts = () => apiFetch.mock.calls.filter(([r]) => r === "/ocr/paquetes/p1/asociar-lote");

describe("asociar en lote y cola de reemplazos (paquete)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
    push.mockReset();
    replace.mockReset();
    query = "";
    permisos = ["comparendos.ocr.ver", "comparendos.ocr.gestionar"];
  });
  afterEach(cleanup);

  it("vista previa: cuenta documentos y correos, omitidos con motivo y reemplazos aparte", async () => {
    abrir();
    fireEvent.click(await screen.findByRole("button", { name: "Asociar los 55" }));
    const m = await screen.findByRole("dialog");
    expect(within(m).getByText("Se asociarán 55 documentos a sus comparendos.")).toBeTruthy();
    expect(within(m).getByText("Saldrán 53 correos a 40 conductores (cada conductor recibe uno por comparendo).")).toBeTruthy();
    expect(within(m).getByText("El conductor no tiene correo registrado (2)")).toBeTruthy();
    expect(within(m).getByText("Quedan 4 reemplazos para revisar uno por uno.")).toBeTruthy();
    expect(posts()).toHaveLength(0);
    fireEvent.click(within(m).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(posts()).toHaveLength(0);
  });

  it("confirmar: pide tandas hasta que no queden y muestra el resultado", async () => {
    tandas = [
      { asociados: asoc(50, "a"), omitidos: [], restantes: 5 },
      { asociados: asoc(4, "b"), omitidos: [{ documentoId: "z", nombreArchivo: "z.pdf", codigo: "EVIDENCIA_EXISTENTE", motivo: "Otra persona ya le puso documento" }], restantes: 0 },
    ];
    abrir();
    fireEvent.click(await screen.findByRole("button", { name: "Asociar los 55" }));
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Asociar y enviar" }));
    expect(await screen.findByText("Resultado de la asociación")).toBeTruthy();
    expect(posts()).toHaveLength(2);
    const m = screen.getByRole("dialog");
    expect(within(m).getByText("Se asociaron 54 documentos a sus comparendos.")).toBeTruthy();
    expect(within(m).getByText(/54 correos con el documento van en camino/)).toBeTruthy();
    expect(within(m).getByText("z.pdf: Otra persona ya le puso documento")).toBeTruthy();
    const cargasAntes = apiFetch.mock.calls.filter(([r]) => r === "/ocr/paquetes/p1").length;
    fireEvent.click(within(m).getByRole("button", { name: "Listo" }));
    await waitFor(() => expect(apiFetch.mock.calls.filter(([r]) => r === "/ocr/paquetes/p1").length).toBeGreaterThan(cargasAntes));
  });

  it("revisar reemplazos: abre el primero de la cola con el avance en la URL", async () => {
    abrir();
    fireEvent.click(await screen.findByRole("button", { name: "Revisar reemplazos (4)" }));
    await waitFor(() => expect(push).toHaveBeenCalledTimes(1));
    const url = String(push.mock.calls[0][0]);
    expect(url.startsWith("/ocr/documentos/r1?")).toBe(true);
    expect(new URLSearchParams(url.split("?")[1]).get("total")).toBe("2");
    expect(new URLSearchParams(url.split("?")[1]).get("cola")).toBe("reemplazos");
  });

  it("sin permiso de gestión o paquete cerrado: ni vista previa ni botones", async () => {
    permisos = ["comparendos.ocr.ver"];
    abrir();
    await screen.findByRole("group", { name: "Filtrar documentos por estado" });
    expect(screen.queryByRole("button", { name: /Asociar los/ })).toBeNull();
    expect(apiFetch.mock.calls.some(([r]) => r === "/ocr/paquetes/p1/asociacion-lote")).toBe(false);
    cleanup();
    permisos = ["comparendos.ocr.ver", "comparendos.ocr.gestionar"];
    abrir({ ...PAQUETE, estado: "Cerrado", cerradoEn: "2026-11-02T10:00:00Z" });
    await screen.findByRole("group", { name: "Filtrar documentos por estado" });
    expect(screen.queryByRole("button", { name: /Asociar los|Revisar reemplazos/ })).toBeNull();
  });

  it("al volver de la cola muestra el resumen", async () => {
    query = "cola=terminada&reemplazados=3&descartados=1";
    abrir();
    expect(await screen.findByText("Terminaste la revisión de reemplazos: 3 reemplazados y 1 descartado.")).toBeTruthy();
  });
});

describe("foco durante el lote", () => {
  afterEach(cleanup);
  it("al confirmar, el foco pasa al avance (el botón se deshabilita) y Escape no cierra mientras asocia", async () => {
    apiFetch.mockReset();
    permisos = ["comparendos.ocr.ver", "comparendos.ocr.gestionar"];
    query = "";
    apiFetch.mockImplementation((ruta: string) => {
      if (ruta === "/ocr/paquetes/p1/asociacion-lote") return Promise.resolve(VISTA);
      if (ruta === "/ocr/paquetes/p1/asociar-lote") return new Promise(() => {});
      return Promise.resolve({ paquete: PAQUETE, documentos: [], completosPorConfirmar: 0 });
    });
    render(
      <ToastProvider>
        <Pagina />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("button", { name: "Asociar los 55" }));
    const m = await screen.findByRole("dialog");
    fireEvent.click(within(m).getByRole("button", { name: "Asociar y enviar" }));
    const barra = await screen.findByRole("progressbar");
    await waitFor(() => expect(document.activeElement).toBe(barra.parentElement));
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.getByRole("dialog")).toBeTruthy();
  });
});

describe("lote cortado y doble clic", () => {
  afterEach(cleanup);
  const montar = (asociarLote: () => Promise<unknown>) => {
    apiFetch.mockReset();
    permisos = ["comparendos.ocr.ver", "comparendos.ocr.gestionar"];
    query = "";
    apiFetch.mockImplementation((ruta: string) => {
      if (ruta === "/ocr/paquetes/p1/asociacion-lote") return Promise.resolve(VISTA);
      if (ruta === "/ocr/paquetes/p1/asociar-lote") return asociarLote();
      return Promise.resolve({ paquete: PAQUETE, documentos: [], completosPorConfirmar: 0 });
    });
    render(
      <ToastProvider>
        <Pagina />
      </ToastProvider>,
    );
  };

  it("si una tanda falla, el resultado conserva lo asociado y explica por qué paró", async () => {
    let n = 0;
    montar(() => (n++ === 0 ? Promise.resolve({ asociados: asoc(50, "a"), omitidos: [], restantes: 5 }) : Promise.reject(new Error("Se perdió la conexión. Intenta de nuevo."))));
    fireEvent.click(await screen.findByRole("button", { name: "Asociar los 55" }));
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Asociar y enviar" }));
    expect(await screen.findByText("Resultado de la asociación")).toBeTruthy();
    expect(screen.getByText("Se asociaron 50 documentos a sus comparendos.")).toBeTruthy();
    expect(screen.getByRole("alert").textContent).toBe("Se perdió la conexión. Intenta de nuevo.");
  });

  it("doble clic en confirmar pide una sola tanda", async () => {
    let resolver: (v: unknown) => void = () => {};
    montar(() => new Promise((r) => (resolver = r)));
    fireEvent.click(await screen.findByRole("button", { name: "Asociar los 55" }));
    const boton = within(await screen.findByRole("dialog")).getByRole("button", { name: "Asociar y enviar" });
    fireEvent.click(boton);
    fireEvent.click(boton);
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Asociando…" }));
    expect(posts()).toHaveLength(1);
    resolver({ asociados: asoc(55, "a"), omitidos: [], restantes: 0 });
    expect(await screen.findByText("Resultado de la asociación")).toBeTruthy();
    expect(posts()).toHaveLength(1);
  });
});

describe("cabecera: un solo primario y sin salto al cargar", () => {
  afterEach(cleanup);
  const montar = (paquete: Record<string, unknown>, vista: () => Promise<unknown>) => {
    apiFetch.mockReset();
    permisos = ["comparendos.ocr.ver", "comparendos.ocr.gestionar"];
    query = "";
    apiFetch.mockImplementation((ruta: string) => {
      if (ruta === "/ocr/paquetes/p1/asociacion-lote") return vista();
      return Promise.resolve({ paquete, documentos: [], completosPorConfirmar: 0 });
    });
    render(
      <ToastProvider>
        <Pagina />
      </ToastProvider>,
    );
  };

  it("solo asociables: «Asociar los N» es el primario", async () => {
    montar(PAQUETE, () => Promise.resolve(VISTA));
    expect((await screen.findByRole("button", { name: "Asociar los 55" })).className).toBe(BOTON_PRIMARIO_CLS);
  });

  it("con completos por registrar: «Registrar» primario y «Asociar» secundario", async () => {
    montar({ ...PAQUETE, cantidadCompletos: 3 }, () => Promise.resolve(VISTA));
    const asociar = await screen.findByRole("button", { name: "Asociar los 55" });
    expect(screen.getByRole("button", { name: "Registrar los 3 completos" }).className).toBe(BOTON_PRIMARIO_CLS);
    expect(asociar.className).not.toBe(BOTON_PRIMARIO_CLS);
  });

  it("mientras carga la vista previa reserva el lugar con «Calculando…» deshabilitado", async () => {
    let soltar: (v: unknown) => void = () => {};
    montar(PAQUETE, () => new Promise((r) => (soltar = r)));
    const reserva = await screen.findByRole("button", { name: "Calculando…" });
    expect((reserva as HTMLButtonElement).disabled).toBe(true);
    soltar(VISTA);
    expect(await screen.findByRole("button", { name: "Asociar los 55" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Calculando…" })).toBeNull();
  });
});

describe("cerrar mientras asocia", () => {
  afterEach(cleanup);
  const cerrarVentana = () => {
    const e = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(e);
    return e.defaultPrevented;
  };
  const iniciar = async (asociarLote: () => Promise<unknown>) => {
    apiFetch.mockReset();
    permisos = ["comparendos.ocr.ver", "comparendos.ocr.gestionar"];
    query = "";
    apiFetch.mockImplementation((ruta: string) => {
      if (ruta === "/ocr/paquetes/p1/asociacion-lote") return Promise.resolve(VISTA);
      if (ruta === "/ocr/paquetes/p1/asociar-lote") return asociarLote();
      return Promise.resolve({ paquete: PAQUETE, documentos: [], completosPorConfirmar: 0 });
    });
    const r = render(
      <ToastProvider>
        <Pagina />
      </ToastProvider>,
    );
    fireEvent.click(await screen.findByRole("button", { name: "Asociar los 55" }));
    const m = await screen.findByRole("dialog");
    const panel = within(m).getByRole("button", { name: "Cerrar" }).closest('div[tabindex="-1"]')!;
    expect(panel.className).not.toContain("aria-label=Cerrar]]:invisible");
    expect(cerrarVentana()).toBe(false);
    fireEvent.click(within(m).getByRole("button", { name: "Asociar y enviar" }));
    return { ...r, panel };
  };

  it("pregunta antes de cerrar la pestaña, oculta la X y lo explica; al terminar deja de preguntar", async () => {
    let soltar: (v: unknown) => void = () => {};
    const { panel } = await iniciar(() => new Promise((r) => (soltar = r)));
    expect(await screen.findByText("Si cierras esta ventana, lo ya asociado se conserva y lo que falte podrás asociarlo después.")).toBeTruthy();
    expect(cerrarVentana()).toBe(true);
    expect(panel.className).toContain("[&_button[aria-label=Cerrar]]:invisible");
    soltar({ asociados: asoc(55, "a"), omitidos: [], restantes: 0 });
    expect(await screen.findByText("Resultado de la asociación")).toBeTruthy();
    expect(cerrarVentana()).toBe(false);
  });

  it("si se sale de la pantalla a mitad, deja de preguntar y termina sin romper", async () => {
    let soltar: (v: unknown) => void = () => {};
    const { unmount } = await iniciar(() => new Promise((r) => (soltar = r)));
    await screen.findByRole("progressbar");
    expect(cerrarVentana()).toBe(true);
    unmount();
    expect(cerrarVentana()).toBe(false);
    soltar({ asociados: asoc(55, "a"), omitidos: [], restantes: 0 });
    await new Promise((r) => setTimeout(r, 0));
  });
});
