import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import Pagina from "@/app/(app)/ocr/paquetes/[id]/page";
import { ToastProvider } from "@/components/Toast";

// Rediseño S3: las cifras del paquete OCR filtran los documentos (antes un <select>). El filtro vive en ?estado= y la
// consulta a la API es la de siempre (`query: { estado }`). «En cola» suma Pendiente y En procesamiento: al activarla
// se elige cuál con dos chips.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));

const replace = vi.fn();
let query = "";
vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "p1" }),
  useRouter: () => ({ push: vi.fn(), replace, back: vi.fn() }),
  useSearchParams: () => new URLSearchParams(query),
  usePathname: () => "/ocr/paquetes/p1",
}));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({
    sesion: { usuario: { esSuperAdmin: false }, permisos: ["comparendos.ocr.ver", "comparendos.ocr.gestionar"] },
    cargando: false,
    error: null,
  }),
}));

const PAQUETE = {
  id: "p1",
  nombre: "Lote de prueba",
  estado: "EnProcesamiento",
  cantidadDocumentos: 6,
  cantidadPendientes: 2,
  cantidadCompletos: 1,
  cantidadIncompletos: 1,
  cantidadFallidos: 1,
  cantidadDuplicados: 0,
  cantidadRegistrados: 1,
  cantidadDescartados: 0,
  creadoEn: "2026-11-01T10:00:00Z",
  procesamientoIniciadoEn: "2026-11-01T10:01:00Z",
  cerradoEn: null,
};

function abrir(estado = "", paquete: Record<string, unknown> = PAQUETE, documentos: unknown[] = []) {
  query = estado ? `estado=${estado}` : "";
  apiFetch.mockResolvedValue({ paquete, documentos, completosPorConfirmar: 0 });
  render(
    <ToastProvider>
      <Pagina />
    </ToastProvider>,
  );
}

const grupo = () => screen.findByRole("group", { name: "Filtrar documentos por estado" });
const tarjeta = async (nombre: RegExp) => within(await grupo()).getByRole("button", { name: nombre });
const consultas = () => apiFetch.mock.calls.filter(([ruta]) => ruta === "/ocr/paquetes/p1").map(([, op]) => op?.query);

describe("filtro del paquete OCR con las cifras (rediseño S3)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
    replace.mockReset();
  });
  afterEach(cleanup);

  it("sin ?estado consulta todo y marca «Documentos»", async () => {
    abrir();
    expect(await tarjeta(/^Documentos/)).toHaveAttribute("aria-pressed", "true");
    expect(consultas()).toContainEqual({ estado: "" });
  });

  it("?estado=Fallido llega a la consulta y marca «Fallidos»", async () => {
    abrir("Fallido");
    expect(await tarjeta(/^Fallidos/)).toHaveAttribute("aria-pressed", "true");
    expect(await tarjeta(/^Documentos/)).toHaveAttribute("aria-pressed", "false");
    expect(consultas()).toContainEqual({ estado: "Fallido" });
  });

  it("pulsar una cifra pone el estado en la URL y pulsarla de nuevo lo quita", async () => {
    abrir();
    fireEvent.click(await tarjeta(/^Incompletos/));
    expect(replace).toHaveBeenLastCalledWith("/ocr/paquetes/p1?estado=Incompleto", { scroll: false });
    cleanup();

    abrir("Incompleto");
    fireEvent.click(await tarjeta(/^Incompletos/));
    expect(replace).toHaveBeenLastCalledWith("/ocr/paquetes/p1", { scroll: false });
  });

  it("«En cola» filtra por Pendiente y deja elegir En procesamiento", async () => {
    abrir();
    expect(screen.queryByRole("group", { name: "Documentos en cola" })).toBeNull();
    fireEvent.click(await tarjeta(/^En cola/));
    expect(replace).toHaveBeenLastCalledWith("/ocr/paquetes/p1?estado=Pendiente", { scroll: false });
    cleanup();

    abrir("Pendiente");
    expect(await tarjeta(/^En cola/)).toHaveAttribute("aria-pressed", "true");
    expect(consultas()).toContainEqual({ estado: "Pendiente" });
    const chips = await screen.findByRole("group", { name: "Documentos en cola" });
    expect(within(chips).getByRole("button", { name: "Pendiente" })).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(within(chips).getByRole("button", { name: "En procesamiento" }));
    expect(replace).toHaveBeenLastCalledWith("/ocr/paquetes/p1?estado=EnProcesamiento", { scroll: false });
    cleanup();

    abrir("EnProcesamiento");
    expect(consultas()).toContainEqual({ estado: "EnProcesamiento" });
    fireEvent.click(await tarjeta(/^En cola/));
    expect(replace).toHaveBeenLastCalledWith("/ocr/paquetes/p1", { scroll: false });
  });

  it("«Asociados» muestra su contador (0 si la API no lo manda) y filtra por ?estado=Asociado", async () => {
    abrir("", { ...PAQUETE, cantidadAsociados: 3 });
    expect(await tarjeta(/^Asociados\s*3/)).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(await tarjeta(/^Asociados/));
    expect(replace).toHaveBeenLastCalledWith("/ocr/paquetes/p1?estado=Asociado", { scroll: false });
    cleanup();

    abrir("Asociado");
    expect(await tarjeta(/^Asociados\s*0/)).toHaveAttribute("aria-pressed", "true");
    expect(consultas()).toContainEqual({ estado: "Asociado" });
  });

  it("el motivo de un documento asociado va en tono informativo, no de advertencia", async () => {
    abrir("", PAQUETE, [
      { id: "d1", nombreArchivo: "a.pdf", tipoArchivo: "PDF", estado: "Asociado", numeroComparendoExtraido: "1", camposFaltantes: [], tipoFallo: null, mensajeError: null, fuenteDuplicado: null, comparendoId: "c1", intentos: 1, procesadoEn: null, reprocesable: false },
    ]);
    const motivo = await screen.findByText("Documento agregado a un comparendo existente");
    expect(motivo.className).toContain("badge-info-fg");
    expect(motivo.className).not.toContain("badge-warning-fg");
  });
});
