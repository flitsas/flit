import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { CargaMasiva } from "@/components/liquidaciones/CargaMasiva";
import { errorRegla, fetchPorRuta, lote } from "./fixtures";

// Uso de ejemplo: <CargaMasiva bloqueo={null} /> en /liquidaciones/masiva. Paso 1 sube el archivo (.xlsx o .csv, 5 MB),
// paso 2 muestra el análisis (válidas, duplicadas, inválidas y el tope) y paso 3 crea el lote. Con `superaTope` rechaza
// antes de crear el lote con el literal LIMITE_SUPERADO del contrato (F3-CF2).

const push = vi.fn();
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => "/liquidaciones/masiva",
  useRouter: () => ({ replace: vi.fn(), push, refresh: vi.fn() }),
  useSearchParams: () => busqueda,
}));

const ANALISIS = {
  validas: 8,
  duplicadas: 1,
  invalidas: [{ fila: 4, valor: "AB#1", motivo: "PLACA_INVALIDA" }],
  tope: 500,
  superaTope: false,
};

const archivo = () => new File(["placa\nABC123\n"], "placas.csv", { type: "text/csv" });

async function subirYAnalizar() {
  render(<CargaMasiva bloqueo={null} />);
  fireEvent.change(screen.getByLabelText("Archivo de placas (.xlsx o .csv)"), { target: { files: [archivo()] } });
  fireEvent.click(screen.getByRole("button", { name: "Analizar archivo" }));
}

beforeEach(() => {
  push.mockReset();
  busqueda = new URLSearchParams();
});
afterEach(() => vi.unstubAllGlobals());

describe("CargaMasiva — análisis y creación", () => {
  it("vacío: paso 1 sin archivo, con la plantilla y el aviso de que el análisis no consulta el RUNT", () => {
    vi.stubGlobal("fetch", vi.fn());
    render(<CargaMasiva bloqueo={null} />);
    expect(screen.getByRole("button", { name: "Analizar archivo" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Descargar plantilla" })).toBeInTheDocument();
    expect(screen.getByText("El análisis no consulta el RUNT ni el proveedor: solo revisa el archivo.")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Pasos de la carga" })).toBeInTheDocument();
  });

  it("analiza (multipart, con ?empresa=) y muestra válidas, duplicadas e inválidas con su motivo rotulado", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    const fetchMock = fetchPorRuta([[/^POST \/api\/v1\/impuestos\/liquidaciones\/masiva\/analizar\?empresa=e-9$/, () => Response.json(ANALISIS)]]);
    vi.stubGlobal("fetch", fetchMock);

    await subirYAnalizar();

    expect(await screen.findByText("Válidas: 8")).toBeInTheDocument();
    expect(screen.getByText("Duplicadas (se liquidan una vez): 1")).toBeInTheDocument();
    expect(screen.getByText("Inválidas: 1")).toBeInTheDocument();
    const tabla = screen.getByRole("table", { name: "Filas inválidas" });
    expect(within(tabla).getByText("AB#1")).toBeInTheDocument();
    expect(within(tabla).getByText("Placa no válida (de 4 a 10 letras o números)")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Placas válidas frente al máximo del lote" })).toHaveAttribute("aria-valuetext", "8 de 500");
    expect(fetchMock.mock.calls[0][1]?.body).toBeInstanceOf(FormData);
    expect(screen.getByRole("button", { name: "Iniciar liquidación de 8 placas" })).toBeEnabled();
  });

  it("con superaTope rechaza antes de crear el lote: literal LIMITE_SUPERADO con el tope y CTA deshabilitado con ese motivo (F3-CF2)", async () => {
    const fetchMock = fetchPorRuta([[/analizar/, () => Response.json({ ...ANALISIS, validas: 12, tope: 5, superaTope: true })]]);
    vi.stubGlobal("fetch", fetchMock);

    await subirYAnalizar();

    const aviso = await screen.findByText("El archivo supera el tope de 5 placas por lote.");
    const iniciar = screen.getByRole("button", { name: "Iniciar liquidación de 12 placas" });
    expect(iniciar).toBeDisabled();
    expect(iniciar.getAttribute("aria-describedby")).toContain(aviso.closest("[id]")?.id);
    fireEvent.click(iniciar);
    expect(fetchMock.mock.calls.some(([u]) => /\/masiva(\?|$)/.test(String(u)))).toBe(false);
  });

  it("crea el lote con el mismo archivo y navega a su detalle conservando la compañía", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    const fetchMock = fetchPorRuta([
      [/analizar/, () => Response.json(ANALISIS)],
      [/^POST \/api\/v1\/impuestos\/liquidaciones\/masiva\?empresa=e-9$/, () => Response.json(lote({ id: "L-9" }), { status: 201 })],
    ]);
    vi.stubGlobal("fetch", fetchMock);

    await subirYAnalizar();
    fireEvent.click(await screen.findByRole("button", { name: "Iniciar liquidación de 8 placas" }));

    await waitFor(() => expect(push).toHaveBeenCalledWith("/liquidaciones/L-9?empresa=e-9"));
  });

  it("422 LIMITE_SUPERADO al crear: el literal del contrato con el tope del cuerpo", async () => {
    vi.stubGlobal(
      "fetch",
      fetchPorRuta([
        [/analizar/, () => Response.json(ANALISIS)],
        [/masiva/, () => errorRegla(422, "LIMITE_SUPERADO", "El archivo supera el tope de 3 placas por lote.", { tope: 3 })],
      ]),
    );
    await subirYAnalizar();
    fireEvent.click(await screen.findByRole("button", { name: "Iniciar liquidación de 8 placas" }));
    expect(await screen.findByText("El archivo supera el tope de 3 placas por lote.")).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("«Subir otro archivo» vuelve al paso 1", async () => {
    vi.stubGlobal("fetch", fetchPorRuta([[/analizar/, () => Response.json(ANALISIS)]]));
    await subirYAnalizar();
    fireEvent.click(await screen.findByRole("button", { name: "Subir otro archivo" }));
    expect(screen.queryByText("Válidas: 8")).toBeNull();
    expect(screen.getByRole("button", { name: "Analizar archivo" })).toBeDisabled();
  });
});

describe("CargaMasiva — errores del análisis con los literales del contrato", () => {
  it.each([
    [415, "FORMATO_NO_SOPORTADO", "Solo se admiten archivos CSV y XLSX."],
    [413, "ARCHIVO_DEMASIADO_GRANDE", "El archivo supera el máximo de 5 MB."],
    [422, "COLUMNA_PLACA_AUSENTE", "Falta la columna de placa."],
  ])("%s %s", async (status, code, literal) => {
    vi.stubGlobal("fetch", fetchPorRuta([[/analizar/, () => errorRegla(status, code, literal)]]));
    await subirYAnalizar();
    expect(await screen.findByText(literal)).toBeInTheDocument();
  });

  it("otro error: «No se pudo analizar el archivo» + Reintentar sobre el mismo archivo", async () => {
    let n = 0;
    vi.stubGlobal("fetch", fetchPorRuta([[/analizar/, () => (++n === 1 ? new Response("", { status: 500 }) : Response.json(ANALISIS))]]));
    await subirYAnalizar();
    expect(await screen.findByText("No se pudo analizar el archivo")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Válidas: 8")).toBeInTheDocument();
  });

  it("cargando: «Analizando el archivo…»", async () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => {})));
    await subirYAnalizar();
    expect(await screen.findByRole("status", { name: "Analizando el archivo…" })).toBeInTheDocument();
  });

  it("con el producto bloqueado no se puede analizar ni crear; el motivo queda asociado", () => {
    vi.stubGlobal("fetch", vi.fn());
    render(<CargaMasiva bloqueo="Impuestos está suspendido para la compañía: solo lectura." />);
    fireEvent.change(screen.getByLabelText("Archivo de placas (.xlsx o .csv)"), { target: { files: [archivo()] } });
    const analizar = screen.getByRole("button", { name: "Analizar archivo" });
    expect(analizar).toBeDisabled();
    const motivo = screen.getByText("Impuestos está suspendido para la compañía: solo lectura.");
    expect(analizar.getAttribute("aria-describedby")).toContain(motivo.id);
  });
});

describe("CargaMasiva — Super Admin sin ?empresa (abierta por URL)", () => {
  it("muestra «Elige una compañía.», deja las acciones deshabilitadas con ese motivo y no analiza ni envía nada", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    render(<CargaMasiva bloqueo={null} esSuperAdmin />);

    expect(screen.getByRole("heading", { name: "Elige una compañía." })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Archivo de placas (.xlsx o .csv)"), { target: { files: [archivo()] } });
    const analizar = screen.getByRole("button", { name: "Analizar archivo" });
    const plantilla = screen.getByRole("button", { name: "Descargar plantilla" });
    expect(analizar).toBeDisabled();
    expect(plantilla).toBeDisabled();
    for (const boton of [analizar, plantilla]) {
      const ids = (boton.getAttribute("aria-describedby") ?? "").split(" ").filter(Boolean);
      expect(ids.length).toBeGreaterThan(0);
      expect(ids.map((id) => document.getElementById(id)?.textContent ?? "").join(" ")).toContain("Elige una compañía.");
    }
    fireEvent.click(analizar);
    fireEvent.click(plantilla);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("con ?empresa el Super Admin analiza normalmente sobre esa compañía", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    const fetchMock = fetchPorRuta([[/^POST \/api\/v1\/impuestos\/liquidaciones\/masiva\/analizar\?empresa=e-9$/, () => Response.json(ANALISIS)]]);
    vi.stubGlobal("fetch", fetchMock);
    render(<CargaMasiva bloqueo={null} esSuperAdmin />);

    expect(screen.queryByText("Elige una compañía.")).toBeNull();
    fireEvent.change(screen.getByLabelText("Archivo de placas (.xlsx o .csv)"), { target: { files: [archivo()] } });
    fireEvent.click(screen.getByRole("button", { name: "Analizar archivo" }));
    expect(await screen.findByText("Válidas: 8")).toBeInTheDocument();
  });
});
