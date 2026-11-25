import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import Pagina from "@/app/(app)/ocr/page";
import { ToastProvider } from "@/components/Toast";

// GDC-EVID §10: el paquete ya no cuenta los PorAsociar en cantidadDuplicados, pero el total global sí. En la fila de cada
// paquete la columna «Duplicados» suma los dos para que cuadre con el total mientras un lote avanza.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
}));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
  usePathname: () => "/ocr",
  useParams: () => ({}),
}));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({
    sesion: {
      usuario: { esSuperAdmin: false },
      permisos: ["comparendos.ocr.ver", "comparendos.ocr.gestionar"],
      empresa: { id: "t1", nombre: "Empresa Demo", capacidades: { derechosPeticion: false, consultaContraventor: false, cambioContraventor: false, ocr: true, reportes: false } },
    },
    cargando: false,
    error: null,
  }),
}));

const paquete = (o: Record<string, unknown>) => ({
  id: "p1aaaaaaaa", tenantId: "t1", nombre: "Escaneos noviembre", estado: "Procesado", cantidadDocumentos: 10, cantidadPendientes: 0,
  cantidadCompletos: 0, cantidadIncompletos: 0, cantidadFallidos: 0, cantidadDuplicados: 4, cantidadRegistrados: 0, cantidadDescartados: 0,
  procesados: 10, creadoPor: "u1", creadoPorNombre: "Admin", creadoEn: "2026-11-25T10:00:00Z", procesamientoIniciadoEn: null, cerradoEn: null, ...o,
});

function abrir(items: unknown[]) {
  apiFetch.mockImplementation((ruta: string) =>
    Promise.resolve(ruta === "/ocr/paquetes" ? { contadores: null, items } : []),
  );
  render(
    <ToastProvider>
      <Pagina />
    </ToastProvider>,
  );
}
const celdaDuplicados = async (nombre: string) => {
  const fila = (await screen.findByText(nombre)).closest("tr")!;
  const cabeceras = [...fila.closest("table")!.querySelectorAll("th")].map((th) => th.textContent?.trim());
  return within(fila).getAllByRole("cell")[cabeceras.indexOf("Duplicados")].textContent;
};

describe("listado de paquetes: duplicados incluyen los que se están asociando", () => {
  afterEach(cleanup);

  it("suma cantidadDuplicados + cantidadPorAsociar en la fila", async () => {
    abrir([paquete({ cantidadDuplicados: 4, cantidadPorAsociar: 5 })]);
    expect(await celdaDuplicados("Escaneos noviembre")).toBe("9");
  });

  it("sin el campo (API anterior) muestra solo los duplicados", async () => {
    abrir([paquete({ cantidadDuplicados: 4 })]);
    expect(await celdaDuplicados("Escaneos noviembre")).toBe("4");
  });
});
