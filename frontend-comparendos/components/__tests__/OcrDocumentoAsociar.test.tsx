import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import Pagina from "@/app/(app)/ocr/documentos/[id]/page";
import { ToastProvider } from "@/components/Toast";
import { ApiError } from "@/lib/api";

// Asociar un documento escaneado a un comparendo que ya existe: sin documento previo (modal «Asociar»), con documento
// previo (modal «Comparar documentos»), no asociable y conflicto 409 por cambio concurrente.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));
vi.mock("next/navigation", () => ({ useParams: () => ({ id: "d1" }) }));
vi.mock("@/components/detalle/useAbrirDetalle", () => ({ useAbrirDetalle: () => vi.fn() }));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({
    sesion: { usuario: { esSuperAdmin: false }, permisos: ["comparendos.ocr.ver", "comparendos.ocr.gestionar"] },
    cargando: false,
    error: null,
  }),
}));

const EVIDENCIA = { archivoId: "ev1", nombre: "actual.pdf", contentType: "application/pdf", tamanoBytes: 2048, cargadaEn: "2026-11-01T10:00:00Z" };

function documento(dup: Record<string, unknown> = {}) {
  return {
    id: "d1",
    paqueteId: "p1",
    nombreArchivo: "nuevo.pdf",
    tipoArchivo: "PDF",
    archivoId: "ar1",
    estado: "Duplicado",
    intentos: 1,
    proveedorModelo: null,
    numeroComparendoExtraido: "11001000000012345678",
    camposFaltantes: [],
    tipoFallo: null,
    mensajeError: null,
    reintentable: false,
    avisoNumero: null,
    numeroPorConfirmar: false,
    reprocesos: 0,
    maximoReprocesos: 3,
    motivoNoReprocesable: null,
    versionPrompt: null,
    comparendoId: null,
    procesadoEn: "2026-11-02T10:00:00Z",
    revisadoEn: null,
    motivoDescarte: null,
    duplicado: { tipo: "comparendo", id: "c1", numero: "11001000000012345678", fuente: "SIMIT", registradoEn: "2026-10-01T10:00:00Z", activo: true, pagado: false, asociable: true, evidenciaActual: null, motivoNoAsociable: null, ...dup },
    campos: [],
    ediciones: [],
    historialIntentos: [],
  };
}

function abrir(dup: Record<string, unknown> = {}, falloAsociar?: ApiError) {
  apiFetch.mockImplementation((ruta: string) => {
    return ruta === "/ocr/documentos/d1" ? Promise.resolve(documento(dup)) : falloAsociar ? Promise.reject(falloAsociar) : Promise.resolve({ numero: "11001000000012345678" });
  });
  render(
    <ToastProvider>
      <Pagina />
    </ToastProvider>,
  );
}
const posts = () => apiFetch.mock.calls.filter(([ruta]) => String(ruta).endsWith("/asociar"));

describe("asociar documento escaneado a comparendo existente", () => {
  beforeEach(() => {
    apiFetch.mockReset();
  });
  afterEach(cleanup);

  it("sin documento previo: ofrece asociar y envía reemplazar=false", async () => {
    abrir();
    fireEvent.click(await screen.findByRole("button", { name: /Asociar al comparendo/ }));
    const modal = await screen.findByRole("dialog");
    fireEvent.click(within(modal).getByRole("button", { name: "Asociar al comparendo" }));
    await waitFor(() => expect(posts()).toHaveLength(1));
    expect(posts()[0][1]).toMatchObject({ method: "POST", body: { reemplazar: false, evidenciaActualId: null } });
  });

  it("con documento previo: compara y Reemplazar envía reemplazar=true con el documento visto", async () => {
    abrir({ evidenciaActual: EVIDENCIA });
    expect(screen.queryByRole("button", { name: "Asociar al comparendo" })).toBeNull();
    fireEvent.click(await screen.findByRole("button", { name: "Comparar documentos" }));
    const modal = await screen.findByRole("dialog");
    expect(within(modal).getByText("actual.pdf")).toBeTruthy();
    expect(within(modal).getByText("nuevo.pdf")).toBeTruthy();
    fireEvent.click(within(modal).getByRole("button", { name: "Reemplazar" }));
    await waitFor(() => expect(posts()).toHaveLength(1));
    expect(posts()[0][1]).toMatchObject({ body: { reemplazar: true, evidenciaActualId: "ev1" } });
  });

  it("Descartar este abre el descarte y Escape cierra la comparación", async () => {
    abrir({ evidenciaActual: EVIDENCIA });
    fireEvent.click(await screen.findByRole("button", { name: "Comparar documentos" }));
    await screen.findByRole("dialog");
    fireEvent.keyDown(document, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    fireEvent.click(screen.getAllByRole("button", { name: "Comparar documentos" })[0]);
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Descartar este" }));
    expect(await screen.findByText("Descartar el documento")).toBeTruthy();
    expect(posts()).toHaveLength(0);
  });

  it("no asociable: explica el motivo y no ofrece asociar", async () => {
    abrir({ asociable: false, motivoNoAsociable: "El comparendo pertenece a otra fuente." });
    expect(await screen.findByText("El comparendo pertenece a otra fuente.")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Asociar al comparendo|Comparar documentos/ })).toBeNull();
    expect(screen.getAllByRole("button", { name: /Descartar/ }).length).toBeGreaterThan(0);
  });

  it("inactivo o pagado: el modal avisa que ya no recibe avisos", async () => {
    abrir({ activo: false, pagado: true });
    fireEvent.click(await screen.findByRole("button", { name: /Asociar al comparendo/ }));
    expect(await screen.findByText(/inactivo y pagado: ya no recibe avisos/)).toBeTruthy();
  });

  it("409 EVIDENCIA_CAMBIO: explica, cierra y vuelve a cargar", async () => {
    abrir({ evidenciaActual: EVIDENCIA }, new ApiError(409, "conflicto", "EVIDENCIA_CAMBIO"));
    fireEvent.click(await screen.findByRole("button", { name: "Comparar documentos" }));
    const cargasAntes = apiFetch.mock.calls.filter(([r]) => r === "/ocr/documentos/d1").length;
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Reemplazar" }));
    expect(await screen.findByText(/Otra persona cambió el documento del comparendo/)).toBeTruthy();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(apiFetch.mock.calls.filter(([r]) => r === "/ocr/documentos/d1").length).toBeGreaterThan(cargasAntes));
  });
});
