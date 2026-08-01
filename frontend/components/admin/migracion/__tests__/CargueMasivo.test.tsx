import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { MigracionRespuesta } from "@/lib/migracion/types";

/**
 * El comportamiento del cargue masivo que no se puede comprobar leyendo el código: que la cola es
 * secuencial, que un fallo no tumba el resto, y que al volver a la página se recupera el avance
 * contrastado con el servidor.
 */
const migrarTramite = vi.fn();
const consultarEstado = vi.fn();

vi.mock("@/lib/migracion/client", async () => {
  const real = await vi.importActual<typeof import("@/lib/migracion/client")>(
    "@/lib/migracion/client",
  );
  return {
    ...real,
    migrarTramite: (...args: unknown[]) => migrarTramite(...args),
    consultarEstado: (...args: unknown[]) => consultarEstado(...args),
  };
});

const { CargueMasivo } = await import("@/components/admin/migracion/CargueMasivo");

function respuestaOk(v1Id: number): MigracionRespuesta {
  return {
    origen: {
      tramite: "transfer",
      tablaV1: "transfers",
      tipoV2: "TRASPASO",
      lote: "l1",
      baseV1: "v1 @ h:5432",
      baseV2: "v2 @ h:5432",
      v1Id,
      dryRun: false,
    },
    yaMigrado: null,
    instancias: [],
    destino: null,
    conProblemas: false,
  };
}

/** Sube un CSV por el input de archivo, que es como entra el lote de verdad. */
async function cargarCsv(usuario: ReturnType<typeof userEvent.setup>, contenido: string) {
  const archivo = new File([contenido], "ola.csv", { type: "text/csv" });
  const input = document.querySelector('input[type="file"]') as HTMLInputElement;
  await usuario.upload(input, archivo);
}

describe("CargueMasivo", () => {
  beforeEach(() => {
    window.localStorage.clear();
    migrarTramite.mockReset();
    consultarEstado.mockReset();
    consultarEstado.mockResolvedValue({ tramite: "transfer", tablaV1: "transfers", items: [] });
  });

  it("muestra las filas válidas del archivo y señala las que no lo son", async () => {
    const usuario = userEvent.setup();
    render(<CargueMasivo />);

    await cargarCsv(usuario, "tipo,id\ntraspaso,26350\nbasura,999\nmatricula,7426");

    expect(await screen.findByText("26350")).toBeInTheDocument();
    expect(screen.getByText("7426")).toBeInTheDocument();
    expect(screen.getByText(/1 fila del archivo no se puede migrar/i)).toBeInTheDocument();
  });

  /**
   * El host solo admite dos migraciones a la vez: lanzar el lote en paralelo produciría 429 que no
   * son errores reales. Se comprueba que nunca hay dos peticiones en vuelo.
   */
  it("migra de a una, nunca en paralelo", async () => {
    const usuario = userEvent.setup();
    let enVuelo = 0;
    let maximo = 0;

    migrarTramite.mockImplementation(async (p: { v1Id: number }) => {
      enVuelo++;
      maximo = Math.max(maximo, enVuelo);
      await new Promise((r) => setTimeout(r, 5));
      enVuelo--;
      return respuestaOk(p.v1Id);
    });

    render(<CargueMasivo />);
    await cargarCsv(usuario, "tipo,id\ntraspaso,1\ntraspaso,2\ntraspaso,3");

    await usuario.click(await screen.findByRole("button", { name: /Simular 3 seleccionados/i }));

    await waitFor(() => expect(migrarTramite).toHaveBeenCalledTimes(3));
    expect(maximo).toBe(1);
  });

  /** Que el tercero falle no es motivo para no intentar los demás. */
  it("un fallo no detiene la cola", async () => {
    const usuario = userEvent.setup();

    migrarTramite.mockImplementation(async (p: { v1Id: number }) => {
      if (p.v1Id === 2) {
        throw new Error("el migrador dijo que no");
      }
      return respuestaOk(p.v1Id);
    });

    render(<CargueMasivo />);
    await cargarCsv(usuario, "tipo,id\ntraspaso,1\ntraspaso,2\ntraspaso,3");

    await usuario.click(await screen.findByRole("button", { name: /Simular 3 seleccionados/i }));

    await waitFor(() => expect(migrarTramite).toHaveBeenCalledTimes(3));
    expect(await screen.findByText("el migrador dijo que no")).toBeInTheDocument();
    expect(screen.getAllByText("Migrado")).toHaveLength(2);
  });

  it("recupera el avance al volver a la pantalla", async () => {
    window.localStorage.setItem(
      "flit:migracion:progreso",
      JSON.stringify({
        version: 1,
        archivo: "ola.csv",
        creadoEl: "2026-08-01T00:00:00Z",
        instancias: [],
        dryRun: false,
        filas: [
          { tramite: "transfer", v1Id: 26350, fila: 2, estado: "migrado" },
          { tramite: "transfer", v1Id: 7426, fila: 3, estado: "pendiente" },
        ],
      }),
    );

    render(<CargueMasivo />);

    expect(await screen.findByText("ola.csv")).toBeInTheDocument();
    expect(screen.getByText("Migrado")).toBeInTheDocument();
    expect(screen.getByText("Pendiente")).toBeInTheDocument();
  });

  /**
   * El caso que justifica reconciliar: la conexión se cortó con una migración en vuelo, así que
   * aquí figura "en_curso" pero el servidor la terminó. Debe corregirse ANTES de pintarse, para que
   * nadie la vuelva a encolar.
   */
  it("corrige contra el servidor lo que el navegador creía pendiente", async () => {
    consultarEstado.mockResolvedValue({
      tramite: "transfer",
      tablaV1: "transfers",
      items: [
        { v1Id: 26350, migrado: true, destino: null, lote: "l1", estadoFinal: "aprobado", migradoEl: null, avisos: [] },
      ],
    });

    window.localStorage.setItem(
      "flit:migracion:progreso",
      JSON.stringify({
        version: 1,
        archivo: "ola.csv",
        creadoEl: "2026-08-01T00:00:00Z",
        instancias: [],
        dryRun: false,
        filas: [{ tramite: "transfer", v1Id: 26350, fila: 2, estado: "en_curso" }],
      }),
    );

    render(<CargueMasivo />);

    expect(await screen.findByText("Ya estaba migrado")).toBeInTheDocument();
    expect(screen.queryByText("Migrando…")).not.toBeInTheDocument();
  });
});
