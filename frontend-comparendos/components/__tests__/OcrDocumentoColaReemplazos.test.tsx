import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import Pagina from "@/app/(app)/ocr/documentos/[id]/page";
import { ToastProvider } from "@/components/Toast";

// GDC-EVID §9: con ?cola=reemplazos&paquete=… el documento muestra «n de m» y, al reemplazar o descartar, pasa solo al
// siguiente reemplazo pendiente; al terminar vuelve al paquete con el resumen. Sin cola, la pantalla no navega sola.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));
const push = vi.fn();
let query = "";
vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "r1" }),
  useRouter: () => ({ push, replace: vi.fn() }),
  useSearchParams: () => new URLSearchParams(query),
}));
vi.mock("@/components/detalle/useAbrirDetalle", () => ({ useAbrirDetalle: () => vi.fn() }));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({ sesion: { usuario: { esSuperAdmin: false }, permisos: ["comparendos.ocr.ver", "comparendos.ocr.gestionar"] }, cargando: false, error: null }),
}));

const EVIDENCIA = { archivoId: "ev1", nombre: "actual.pdf", contentType: "application/pdf", tamanoBytes: 2048, cargadaEn: "2026-11-01T10:00:00Z" };
const DOC = {
  id: "r1", paqueteId: "p1", nombreArchivo: "nuevo.pdf", tipoArchivo: "PDF", archivoId: "ar1", estado: "Duplicado", intentos: 1, proveedorModelo: null,
  numeroComparendoExtraido: "11001000000012345678", camposFaltantes: [], tipoFallo: null, mensajeError: null, reintentable: false, avisoNumero: null,
  numeroPorConfirmar: false, reprocesos: 0, maximoReprocesos: 3, motivoNoReprocesable: null, versionPrompt: null, comparendoId: null,
  procesadoEn: "2026-11-02T10:00:00Z", revisadoEn: null, motivoDescarte: null,
  duplicado: { tipo: "comparendo", id: "c1", numero: "11001000000012345678", fuente: "SIMIT", registradoEn: "2026-10-01T10:00:00Z", activo: true, pagado: false, asociable: true, evidenciaActual: EVIDENCIA, motivoNoAsociable: null, correoAlAsociar: { seEnviara: true, motivoNo: null } },
  campos: [], ediciones: [], historialIntentos: [],
};
let pendientes: string[] = [];
function abrir(q: string) {
  query = q;
  apiFetch.mockImplementation((ruta: string) => {
    if (ruta === "/ocr/documentos/r1") return Promise.resolve(DOC);
    if (ruta === "/ocr/paquetes/p1") return Promise.resolve({ documentos: pendientes.map((id) => ({ id, estado: "Duplicado", revision: "reemplazo" })) });
    if (ruta.endsWith("/asociar")) return Promise.resolve({ numero: "11001000000012345678", correo: { estado: "Enviado", motivo: null } });
    if (ruta.endsWith("/descartar")) return Promise.resolve({});
    return Promise.resolve({});
  });
  render(
    <ToastProvider>
      <Pagina />
    </ToastProvider>,
  );
}
const COLA = "cola=reemplazos&paquete=p1&hechos=1&total=7&reemplazados=1&descartados=0";
const destino = () => String(push.mock.calls[0][0]);
const params = (u: string) => new URLSearchParams(u.split("?")[1]);

describe("cola de reemplazos (documento)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
    push.mockReset();
  });
  afterEach(cleanup);

  it("muestra «2 de 7» y la salida al paquete", async () => {
    pendientes = ["r1", "r2"];
    abrir(COLA);
    expect(await screen.findByText("Revisión de reemplazos: 2 de 7")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Salir de la revisión" }).getAttribute("href")).toBe("/ocr/paquetes/p1");
  });

  it("reemplazar pasa solo al siguiente pendiente", async () => {
    pendientes = ["r1", "r2", "r3"];
    abrir(COLA);
    fireEvent.click(await screen.findByRole("button", { name: "Comparar documentos" }));
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Reemplazar" }));
    await waitFor(() => expect(push).toHaveBeenCalledTimes(1));
    expect(destino().startsWith("/ocr/documentos/r2?")).toBe(true);
    expect(Object.fromEntries(params(destino()))).toMatchObject({ hechos: "2", total: "4", reemplazados: "2", descartados: "0" });
  });

  it("descartar el último vuelve al paquete con el resumen", async () => {
    pendientes = ["r1"];
    abrir(COLA);
    fireEvent.click(await screen.findByRole("button", { name: "Comparar documentos" }));
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Descartar este" }));
    const m = await screen.findByRole("dialog");
    fireEvent.change(within(m).getByRole("textbox"), { target: { value: "Ya estaba bien el anterior" } });
    fireEvent.click(within(m).getByRole("button", { name: "Descartar" }));
    await waitFor(() => expect(push).toHaveBeenCalledTimes(1));
    expect(destino()).toBe("/ocr/paquetes/p1?cola=terminada&reemplazados=1&descartados=1");
  });

  it("sin cola: ni banner ni navegación al reemplazar", async () => {
    pendientes = ["r1", "r2"];
    abrir("");
    fireEvent.click(await screen.findByRole("button", { name: "Comparar documentos" }));
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Reemplazar" }));
    await waitFor(() => expect(apiFetch.mock.calls.some(([r]) => String(r).endsWith("/asociar"))).toBe(true));
    // Esperar el aviso del correo: lo que venga después del reemplazo ya ocurrió.
    expect(await screen.findByText(/reemplazado en el comparendo .* y enviado al conductor/)).toBeTruthy();
    expect(screen.queryByText(/Revisión de reemplazos/)).toBeNull();
    expect(screen.queryByText(/No pudimos abrir el siguiente/)).toBeNull();
    expect(apiFetch.mock.calls.some(([r]) => r === "/ocr/paquetes/p1")).toBe(false);
    expect(push).not.toHaveBeenCalled();
  });
});

describe("cola: documento que ya no está por revisar", () => {
  afterEach(cleanup);
  it("lo explica y deja ir al siguiente sin contar reemplazo ni descarte", async () => {
    apiFetch.mockReset();
    push.mockReset();
    query = COLA;
    pendientes = ["r2"];
    apiFetch.mockImplementation((ruta: string) => {
      if (ruta === "/ocr/documentos/r1") return Promise.resolve({ ...DOC, estado: "Asociado", comparendoId: "c1" });
      if (ruta === "/ocr/paquetes/p1") return Promise.resolve({ documentos: pendientes.map((id) => ({ id, estado: "Duplicado", revision: "reemplazo" })) });
      return Promise.resolve({});
    });
    render(
      <ToastProvider>
        <Pagina />
      </ToastProvider>,
    );
    expect(await screen.findByText(/ya no está por revisar/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Ir al siguiente" }));
    await waitFor(() => expect(push).toHaveBeenCalledTimes(1));
    expect(Object.fromEntries(params(destino()))).toMatchObject({ hechos: "2", total: "3", reemplazados: "1", descartados: "0" });
    expect(destino().startsWith("/ocr/documentos/r2?")).toBe(true);
  });
});
