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

function respuestaOk(v1Id: number, dryRun = false): MigracionRespuesta {
  return {
    origen: {
      tramite: "transfer",
      tablaV1: "transfers",
      tipoV2: "TRASPASO",
      lote: "l1",
      baseV1: "v1 @ h:5432",
      baseV2: "v2 @ h:5432",
      v1Id,
      dryRun,
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

    await usuario.click(await screen.findByRole("button", { name: /Simular 3 trámites/i }));

    await waitFor(() => expect(migrarTramite).toHaveBeenCalledTimes(3));
    expect(maximo).toBe(1);
  });

  /**
   * El flujo que la propia ayuda recomienda —simular las veinte y luego migrarlas— y que estuvo
   * roto: al dar la simulación por terminada, el lote quedaba bloqueado y había que descartarlo y
   * volver a cargar el archivo para migrar de verdad.
   */
  it("tras simular, las filas siguen disponibles para migrar de verdad", async () => {
    const usuario = userEvent.setup();
    migrarTramite.mockImplementation(async (p: { v1Id: number; dryRun: boolean }) =>
      respuestaOk(p.v1Id, p.dryRun),
    );

    render(<CargueMasivo />);
    await cargarCsv(usuario, "tipo,id\ntraspaso,1\ntraspaso,2");

    await usuario.click(await screen.findByRole("button", { name: /Simular 2 trámites/i }));
    await waitFor(() => expect(migrarTramite).toHaveBeenCalledTimes(2));

    // Ninguna quedó como migrada, y el botón sigue ofreciendo las dos.
    expect(screen.getAllByText("Simulado, sin migrar")).toHaveLength(2);
    expect(screen.queryByText("Migrado")).not.toBeInTheDocument();

    await usuario.click(screen.getByRole("radio", { name: /Migración/i }));
    await usuario.click(await screen.findByRole("button", { name: /^Migrar 2 trámites/i }));

    await waitFor(() => expect(migrarTramite).toHaveBeenCalledTimes(4));
    expect(await screen.findAllByText("Migrado")).toHaveLength(2);
  });

  /**
   * Un id que no existe en V1 NO revienta la petición: el host responde 200 con la instancia en
   * cuarentena. Sin esto, la fila decía «Falló» a secas y el motivo solo se leía desplegando el
   * reporte y bajando hasta la instancia — tres despliegues para tres fallos en una ola de veinte.
   */
  it("la fila que falla dice por qué, sin desplegar el reporte", async () => {
    const usuario = userEvent.setup();
    migrarTramite.mockImplementation(async (p: { v1Id: number }) => {
      const base = respuestaOk(p.v1Id);
      return {
        ...base,
        conProblemas: true,
        instancias: [
          {
            instancia: "datos",
            estado: "Quarantined",
            v2Id: null,
            motivo: "No existe en la copia de V1.",
            conProblemas: true,
            conteos: {},
            avisos: [],
          },
        ],
      };
    });

    render(<CargueMasivo />);
    await cargarCsv(usuario, "tipo,id\ntraspaso,999999999");
    await usuario.click(await screen.findByRole("button", { name: /Simular 1 trámite/i }));

    expect(await screen.findByText("No existe en la copia de V1.")).toBeInTheDocument();
    expect(screen.getByText(/1 trámite falló/i)).toBeInTheDocument();
    expect(screen.getByText(/Reintentar es seguro/i)).toBeInTheDocument();
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

    await usuario.click(await screen.findByRole("button", { name: /Simular 3 trámites/i }));

    await waitFor(() => expect(migrarTramite).toHaveBeenCalledTimes(3));
    expect(await screen.findByText("el migrador dijo que no")).toBeInTheDocument();
    expect(screen.getAllByText("Migrado")).toHaveLength(2);
  });

  /**
   * Terminada una ola, la pantalla se quedaba con el lote hecho y un botón deshabilitado sin decir
   * por dónde se empieza otra. Lo natural es suponer que hay que descartar primero —el camino
   * largo, y que además hace pensar que se pierde algo—, cuando basta con cargar el archivo nuevo.
   */
  it("terminado el lote, dice cómo empezar otro", async () => {
    const usuario = userEvent.setup();
    migrarTramite.mockImplementation(async (p: { v1Id: number }) => respuestaOk(p.v1Id));

    render(<CargueMasivo />);
    await cargarCsv(usuario, "tipo,id\ntraspaso,1\ntraspaso,2");

    expect(screen.queryByText(/Lote terminado/i)).not.toBeInTheDocument();

    await usuario.click(await screen.findByRole("radio", { name: /Migración/i }));
    await usuario.click(await screen.findByRole("button", { name: /^Migrar 2 trámites/i }));

    await waitFor(() => expect(screen.getByText(/Lote terminado/i)).toBeInTheDocument());
    expect(screen.getByText(/carga un archivo nuevo/i)).toBeInTheDocument();
  });

  /** Y que sea verdad: el archivo nuevo reemplaza al anterior sin pasar por «Descartar el lote». */
  it("cargar otro archivo reemplaza el lote terminado", async () => {
    const usuario = userEvent.setup();
    migrarTramite.mockImplementation(async (p: { v1Id: number }) => respuestaOk(p.v1Id));

    render(<CargueMasivo />);
    await cargarCsv(usuario, "tipo,id\ntraspaso,1");
    await usuario.click(await screen.findByRole("radio", { name: /Migración/i }));
    await usuario.click(await screen.findByRole("button", { name: /^Migrar 1 trámite/i }));
    await waitFor(() => expect(screen.getByText(/Lote terminado/i)).toBeInTheDocument());

    const otro = new File(["tipo,id\nmatricula,7426"], "ola-2.csv", { type: "text/csv" });
    await usuario.upload(document.querySelector('input[type="file"]') as HTMLInputElement, otro);

    expect(await screen.findByText("ola-2.csv")).toBeInTheDocument();
    expect(screen.getByText("7426")).toBeInTheDocument();
    expect(screen.queryByText("ola.csv")).not.toBeInTheDocument();
    expect(screen.queryByText(/Lote terminado/i)).not.toBeInTheDocument();
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
   * Carrera real, vista en el navegador: la reconciliación del arranque tarda (consulta al
   * servidor) y puede terminar DESPUÉS de que alguien cargue otro archivo. Al aplicarse pisaba el
   * lote nuevo con el viejo y —peor— dejaba la selección del viejo sobre las filas del nuevo, con
   * un contador que decía «3 en el archivo · 1 por simular» sin nada que lo explicara.
   */
  it("una reconciliación lenta no pisa el archivo que se acaba de cargar", async () => {
    const usuario = userEvent.setup();

    let resolver: (v: unknown) => void = () => {};
    consultarEstado.mockImplementation(
      () => new Promise((r) => { resolver = r; }),
    );

    window.localStorage.setItem(
      "flit:migracion:progreso",
      JSON.stringify({
        version: 1,
        archivo: "lote-viejo.csv",
        creadoEl: "2026-08-01T00:00:00Z",
        instancias: [],
        dryRun: false,
        filas: [{ tramite: "transfer", v1Id: 999, fila: 2, estado: "pendiente" }],
      }),
    );

    render(<CargueMasivo />);

    // Llega un archivo nuevo mientras la consulta sigue en vuelo…
    await cargarCsv(usuario, "tipo,id\ntraspaso,1\ntraspaso,2\ntraspaso,3");
    expect(await screen.findByText("ola.csv")).toBeInTheDocument();

    // …y solo entonces responde el servidor.
    resolver({ tramite: "transfer", tablaV1: "transfers", items: [] });

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Simular 3 trámites/i })).toBeInTheDocument(),
    );
    expect(screen.queryByText("lote-viejo.csv")).not.toBeInTheDocument();
    expect(screen.queryByText("999")).not.toBeInTheDocument();
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
