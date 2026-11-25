import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import Pagina from "@/app/(app)/ocr/paquetes/[id]/page";
import { ToastProvider } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS } from "@/components/ui/estilos";
import { ApiError } from "@/lib/api";

// GDC-EVID §9/§10: «Asociar los N» confirma con la vista previa del servidor y ENCOLA; un trabajador del servidor asocia en
// segundo plano. La franja del paquete muestra el avance (se actualiza sola solo mientras hay lote) y, al terminar, el
// resumen hasta que la gestora lo cierre (recordado por lote). «Revisar reemplazos (n)» abre el primero de la cola.
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
let permisos: string[] = [];
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
  enCurso: null as unknown,
};
const LOTE = {
  loteId: "l1",
  porAsociar: 36,
  total: 48,
  asociados: 12,
  noAsociados: [] as { documentoId: string; nombreArchivo: string; motivo: string }[],
  correosEnCamino: 11,
  sinCorreo: [] as { motivo: string; cantidad: number }[],
  solicitadoPor: "admin@empresa.local",
  solicitadoEn: "2026-11-25T15:00:00Z",
  terminadoEn: null as string | null,
};
const TERMINADO = {
  ...LOTE,
  porAsociar: 0,
  asociados: 45,
  correosEnCamino: 43,
  sinCorreo: [{ motivo: "El conductor no tiene correo registrado", cantidad: 2 }],
  noAsociados: [{ documentoId: "z", nombreArchivo: "z.pdf", motivo: "Otra persona le puso un documento mientras tanto" }],
  terminadoEn: "2026-11-25T15:03:00Z",
};
const doc = (id: string, revision: string | null) => ({
  id, nombreArchivo: `${id}.pdf`, estado: "Duplicado", numeroComparendoExtraido: "1", camposFaltantes: [], tipoFallo: null, mensajeError: null,
  fuenteDuplicado: "SIMIT", comparendoId: null, tipoArchivo: "PDF", intentos: 1, procesadoEn: null, reprocesable: false, revision,
});

let vista: () => Promise<unknown> = () => Promise.resolve(VISTA);
let post: () => Promise<unknown> = () => Promise.resolve({ encolados: 55, loteId: "l1" });
function abrir(paquete: Record<string, unknown> = PAQUETE) {
  apiFetch.mockImplementation((ruta: string) => {
    if (ruta === "/ocr/paquetes/p1/asociacion-lote") return vista();
    if (ruta === "/ocr/paquetes/p1/asociar-lote") return post();
    return Promise.resolve({ paquete, documentos: [doc("d1", null), doc("r1", "reemplazo"), doc("n1", "numeroDistinto"), doc("r2", "reemplazo")], completosPorConfirmar: 0 });
  });
  return render(
    <ToastProvider>
      <Pagina />
    </ToastProvider>,
  );
}
const posts = () => apiFetch.mock.calls.filter(([r]) => r === "/ocr/paquetes/p1/asociar-lote");
const previas = () => apiFetch.mock.calls.filter(([r]) => r === "/ocr/paquetes/p1/asociacion-lote").length;
const confirmar = async () => {
  fireEvent.click(await screen.findByRole("button", { name: "Asociar los 55" }));
  fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Asociar y enviar" }));
};

beforeEach(() => {
  apiFetch.mockReset();
  push.mockReset();
  replace.mockReset();
  query = "";
  permisos = ["comparendos.ocr.ver", "comparendos.ocr.gestionar"];
  vista = () => Promise.resolve(VISTA);
  post = () => Promise.resolve({ encolados: 55, loteId: "l1" });
  window.localStorage.clear();
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("confirmar y encolar", () => {
  it("vista previa: cuenta documentos y correos, omitidos con motivo y reemplazos aparte; cancelar no envía", async () => {
    abrir();
    fireEvent.click(await screen.findByRole("button", { name: "Asociar los 55" }));
    const m = await screen.findByRole("dialog");
    expect(within(m).getByText("Se asociarán 55 documentos a sus comparendos.")).toBeTruthy();
    expect(within(m).getByText("Saldrán 53 correos a 40 conductores (cada conductor recibe uno por comparendo).")).toBeTruthy();
    expect(within(m).getByText("El conductor no tiene correo registrado (2)")).toBeTruthy();
    expect(within(m).getByText("Quedan 4 reemplazos para revisar uno por uno.")).toBeTruthy();
    fireEvent.click(within(m).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(posts()).toHaveLength(0);
  });

  it("asociar y enviar: un solo POST, se cierra el modal y avisa que sigue en segundo plano", async () => {
    abrir();
    await confirmar();
    expect(await screen.findByText("Asociando 55 documentos en segundo plano; puedes seguir trabajando.")).toBeTruthy();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(posts()).toHaveLength(1);
    expect(posts()[0][1]).toMatchObject({ method: "POST" });
  });

  it("doble clic: el botón queda deshabilitado mientras envía", async () => {
    let soltar: (v: unknown) => void = () => {};
    post = () => new Promise((r) => (soltar = r));
    abrir();
    fireEvent.click(await screen.findByRole("button", { name: "Asociar los 55" }));
    const m = await screen.findByRole("dialog");
    fireEvent.click(within(m).getByRole("button", { name: "Asociar y enviar" }));
    fireEvent.click(within(m).getByRole("button", { name: "Enviando…" }));
    expect(posts()).toHaveLength(1);
    soltar({ encolados: 55, loteId: "l1" });
    expect(await screen.findByText(/en segundo plano/)).toBeTruthy();
  });

  it("si el POST falla, lo dice y el modal sigue abierto para reintentar", async () => {
    post = () => Promise.reject(new ApiError(500, "No se pudo iniciar la asociación. Intenta de nuevo."));
    abrir();
    await confirmar();
    expect(await screen.findByText("No se pudo iniciar la asociación. Intenta de nuevo.")).toBeTruthy();
    expect((within(screen.getByRole("dialog")).getByRole("button", { name: "Asociar y enviar" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("encola 0 y no hay lote del paquete: «No había documentos por asociar.»", async () => {
    post = () => Promise.resolve({ encolados: 0, loteId: null });
    abrir();
    await confirmar();
    expect(await screen.findByText("No había documentos por asociar.")).toBeTruthy();
    expect(screen.queryByText(/Asociando 0/)).toBeNull();
  });

  it("encola 0 pero otro clic ya creó el lote: sin aviso, lo muestra la franja", async () => {
    post = () => Promise.resolve({ encolados: 0, loteId: null });
    let n = 0;
    vista = () => Promise.resolve(n++ === 0 ? VISTA : { ...VISTA, asociables: 0, enCurso: LOTE });
    abrir();
    await confirmar();
    expect(await screen.findByText("Asociando 12 de 48…")).toBeTruthy();
    expect(screen.queryByText("No había documentos por asociar.")).toBeNull();
    expect(screen.queryByText(/Asociando 0/)).toBeNull();
  });
});

describe("franja del lote", () => {
  it("en curso: avance, filtro «Asociando» y se actualiza sola cada pocos segundos", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] });
    vista = () => Promise.resolve({ ...VISTA, enCurso: LOTE });
    abrir({ ...PAQUETE, cantidadPorAsociar: 36 });
    expect(await screen.findByText("Asociando 12 de 48…")).toBeTruthy();
    expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("12");
    expect(within(screen.getByRole("group", { name: "Filtrar documentos por estado" })).getByRole("button", { name: /Asociando/ })).toBeTruthy();
    const antes = previas();
    await act(async () => {
      vi.advanceTimersByTime(4100);
    });
    await waitFor(() => expect(previas()).toBeGreaterThan(antes));
  });

  it("sin lote en curso no consulta seguido", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] });
    abrir();
    await screen.findByRole("button", { name: "Asociar los 55" });
    const antes = apiFetch.mock.calls.length;
    await act(async () => {
      vi.advanceTimersByTime(20000);
    });
    expect(apiFetch.mock.calls.length).toBe(antes);
  });

  it("terminado: resumen con correos, sin correo y no asociados; los enlaces filtran", async () => {
    vista = () => Promise.resolve({ ...VISTA, enCurso: TERMINADO });
    abrir();
    expect(await screen.findByText("Asociación terminada")).toBeTruthy();
    expect(screen.getByText("Se asociaron 45 documentos a sus comparendos.")).toBeTruthy();
    expect(screen.getByText("43 correos con el documento van en camino a los conductores.")).toBeTruthy();
    expect(screen.getByText("El conductor no tiene correo registrado (2)")).toBeTruthy();
    expect(screen.getByText("z.pdf: Otra persona le puso un documento mientras tanto")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Ver los no asociados" }));
    expect(replace).toHaveBeenLastCalledWith("/ocr/paquetes/p1?estado=Duplicado", { scroll: false });
    fireEvent.click(screen.getByRole("button", { name: "Ver los asociados" }));
    expect(replace).toHaveBeenLastCalledWith("/ocr/paquetes/p1?estado=Asociado", { scroll: false });
  });

  it("el resumen se queda hasta cerrarlo y, cerrado, no vuelve a salir para ese lote", async () => {
    vista = () => Promise.resolve({ ...VISTA, enCurso: TERMINADO });
    abrir();
    fireEvent.click(await screen.findByRole("button", { name: "Cerrar resumen" }));
    await waitFor(() => expect(screen.queryByText("Asociación terminada")).toBeNull());
    cleanup();
    abrir();
    await screen.findByRole("button", { name: "Asociar los 55" });
    expect(screen.queryByText("Asociación terminada")).toBeNull();
    cleanup();
    vista = () => Promise.resolve({ ...VISTA, enCurso: { ...TERMINADO, loteId: "l2" } });
    abrir();
    expect(await screen.findByText("Asociación terminada")).toBeTruthy();
  });

  it("el resumen se ve también sin permiso de gestión y con el paquete cerrado (sin botones de lote)", async () => {
    permisos = ["comparendos.ocr.ver"];
    vista = () => Promise.resolve({ ...VISTA, enCurso: TERMINADO });
    abrir({ ...PAQUETE, estado: "Cerrado", cerradoEn: "2026-11-25T15:04:00Z" });
    expect(await screen.findByText("Asociación terminada")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Asociar los|Revisar reemplazos/ })).toBeNull();
  });
});

describe("cabecera y cola de reemplazos", () => {
  it("solo asociables: «Asociar los N» es el primario", async () => {
    abrir();
    expect((await screen.findByRole("button", { name: "Asociar los 55" })).className).toBe(BOTON_PRIMARIO_CLS);
  });

  it("con completos por registrar: «Registrar» primario y «Asociar» secundario", async () => {
    abrir({ ...PAQUETE, cantidadCompletos: 3 });
    const asociar = await screen.findByRole("button", { name: "Asociar los 55" });
    expect(screen.getByRole("button", { name: "Registrar los 3 completos" }).className).toBe(BOTON_PRIMARIO_CLS);
    expect(asociar.className).not.toBe(BOTON_PRIMARIO_CLS);
  });

  it("mientras carga la vista previa reserva el lugar con «Calculando…» deshabilitado", async () => {
    let soltar: (v: unknown) => void = () => {};
    vista = () => new Promise((r) => (soltar = r));
    abrir();
    // Con la máquina cargada (suites en paralelo) la primera carga puede pasar de 1 s: margen explícito.
    const reserva = await screen.findByRole("button", { name: "Calculando…" }, { timeout: 4000 });
    expect((reserva as HTMLButtonElement).disabled).toBe(true);
    soltar(VISTA);
    expect(await screen.findByRole("button", { name: "Asociar los 55" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Calculando…" })).toBeNull();
  });

  it("sin permiso de gestión o paquete cerrado: sin botones de lote", async () => {
    permisos = ["comparendos.ocr.ver"];
    abrir();
    await screen.findByRole("group", { name: "Filtrar documentos por estado" });
    expect(screen.queryByRole("button", { name: /Asociar los|Revisar reemplazos|Calculando/ })).toBeNull();
    cleanup();
    permisos = ["comparendos.ocr.ver", "comparendos.ocr.gestionar"];
    abrir({ ...PAQUETE, estado: "Cerrado", cerradoEn: "2026-11-02T10:00:00Z" });
    await screen.findByRole("group", { name: "Filtrar documentos por estado" });
    expect(screen.queryByRole("button", { name: /Asociar los|Revisar reemplazos|Calculando/ })).toBeNull();
  });

  it("revisar reemplazos: abre el primero de la cola con el avance en la URL", async () => {
    abrir();
    fireEvent.click(await screen.findByRole("button", { name: "Revisar reemplazos (4)" }));
    await waitFor(() => expect(push).toHaveBeenCalledTimes(1));
    const url = String(push.mock.calls[0][0]);
    expect(url.startsWith("/ocr/documentos/r1?")).toBe(true);
    expect(new URLSearchParams(url.split("?")[1]).get("total")).toBe("2");
  });

  it("al volver de la cola muestra el resumen de la revisión", async () => {
    query = "cola=terminada&reemplazados=3&descartados=1";
    abrir();
    expect(await screen.findByText("Terminaste la revisión de reemplazos: 3 reemplazados y 1 descartado.")).toBeTruthy();
  });
});

describe("tarjeta «Asociando» desde el contador del paquete", () => {
  it("se muestra aunque falle la consulta del lote, y sigue actualizándose", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] });
    vista = () => Promise.reject(new ApiError(500, "No disponible"));
    abrir({ ...PAQUETE, cantidadPorAsociar: 5 });
    const grupo = await screen.findByRole("group", { name: "Filtrar documentos por estado" });
    const tarjeta = await within(grupo).findByRole("button", { name: /Asociando/ });
    expect(tarjeta.textContent).toContain("5");
    expect(screen.queryByText(/Asociando \d+ de/)).toBeNull();
    const cargas = () => apiFetch.mock.calls.filter(([r]) => r === "/ocr/paquetes/p1").length;
    const antes = cargas();
    await act(async () => {
      vi.advanceTimersByTime(4100);
    });
    await waitFor(() => expect(cargas()).toBeGreaterThan(antes));
  });

  it("sin documentos por asociar no hay tarjeta «Asociando»", async () => {
    abrir({ ...PAQUETE, cantidadPorAsociar: 0 });
    const grupo = await screen.findByRole("group", { name: "Filtrar documentos por estado" });
    await screen.findByRole("button", { name: "Asociar los 55" });
    expect(within(grupo).queryByRole("button", { name: /Asociando/ })).toBeNull();
  });
});
