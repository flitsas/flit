// Ronda 2 · «Programar envío» de una consulta guardada, contra el mock de /programaciones (§4).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import type { SesionComparendos } from "@/lib/sesion";

const sesion = vi.hoisted(() => ({ actual: null as SesionComparendos | null }));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({ sesion: sesion.actual, cargando: false, error: null }),
}));
vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(window.location.search),
}));

import Page from "@/app/(app)/reportes/consultas/page";
import { describirFrecuencia, separarCorreos } from "@/components/consultas-personalizadas/ProgramarEnvio";
import type { GuardarProgramacion } from "@/lib/api/consultas-personalizadas";
import { ID_FABRICA_SIN_GESTION, PROGRAMADA, instalarServidor, type OpcionesMock } from "./mockServidor";

const CAPACIDADES = { derechosPeticion: true, consultaContraventor: true, cambioContraventor: true, ocr: true, reportes: true };
const LEER = "comparendos.reportes.consultas";
const PROGRAMAR = "comparendos.reportes.programar";

function comoAdminEmpresa(permisos = [LEER, PROGRAMAR]) {
  sesion.actual = {
    usuario: { id: "u", email: "a@x", esSuperAdmin: false },
    permisos,
    empresa: { id: "mia", nombre: "Mi empresa", capacidades: CAPACIDADES },
  };
}

async function abrirPanel(opciones: OpcionesMock = {}) {
  comoAdminEmpresa();
  const servidor = instalarServidor(opciones);
  render(<Page />);
  fireEvent.click(await screen.findByTestId("cmp-query-programar-mia-1"));
  return { ...servidor, panel: await screen.findByTestId("cmp-query-programar") };
}

/** Escribe en el campo de destinatarios y presiona Enter, como hace la persona. */
function escribirCorreos(texto: string) {
  const campo = screen.getByTestId("cmp-query-programar-destinatarios-input");
  fireEvent.change(campo, { target: { value: texto } });
  fireEvent.keyDown(campo, { key: "Enter" });
}
const chips = () => screen.queryAllByTestId("cmp-query-programar-chip").map((c) => c.textContent);

const posts = (peticiones: { metodo: string; ruta: string; cuerpo: unknown }[]) =>
  peticiones.filter((p) => p.metodo === "POST" && p.ruta === "/programaciones").map((p) => p.cuerpo as GuardarProgramacion);

beforeEach(() => window.history.replaceState(null, "", "/reportes/consultas"));
afterEach(() => vi.unstubAllGlobals());

describe("cmp-query · programar envío", () => {
  it("cmp-query: se ofrece en las consultas propias, no en las del sistema", async () => {
    comoAdminEmpresa();
    instalarServidor();
    render(<Page />);
    expect(await screen.findByTestId("cmp-query-programar-mia-1")).toBeInTheDocument();
    expect(screen.queryByTestId(`cmp-query-programar-${ID_FABRICA_SIN_GESTION}`)).not.toBeInTheDocument();
    expect(screen.getByTestId("cmp-query-grupo-sistema")).toHaveTextContent("guárdala primero como tuya");
  });

  it("cmp-query: sin el permiso de programar no aparece", async () => {
    comoAdminEmpresa([LEER]);
    instalarServidor();
    render(<Page />);
    await screen.findByTestId("cmp-query-guardada-mia-1");
    expect(screen.queryByTestId("cmp-query-programar-mia-1")).not.toBeInTheDocument();
  });

  it("cmp-query: la lista muestra próximo y último envío y el resultado tal como llega", async () => {
    const { panel } = await abrirPanel({ programaciones: [PROGRAMADA] });
    const tarjeta = await within(panel).findByTestId("cmp-query-programar-envio-prog-1");
    expect(tarjeta).toHaveTextContent("Cada lunes a las 07:00");
    expect(tarjeta).toHaveTextContent("flota@empresa.com, gerencia@empresa.com, jefe@empresa.com");
    expect(tarjeta).toHaveTextContent("Próximo envío");
    expect(tarjeta).toHaveTextContent("Último envío");
    expect(within(tarjeta).getByTestId("cmp-query-programar-resultado-prog-1")).toHaveTextContent("Enviado a 3 destinatarios");
  });

  it("cmp-query: sin envíos lo dice, y un correo inválido no deja guardar y se nombra", async () => {
    const { panel, peticiones } = await abrirPanel();
    expect(await within(panel).findByTestId("cmp-query-programar-vacio")).toHaveTextContent("todavía no se envía por correo");
    fireEvent.click(within(panel).getByTestId("cmp-query-programar-nuevo"));
    escribirCorreos("Flota@Empresa.com no-es-correo");
    // El válido queda como ficha; el inválido se nombra y se queda en el campo para corregirlo.
    expect(chips()).toEqual(["flota@empresa.com"]);
    expect(screen.getByTestId("cmp-query-programar-correos-invalidos")).toHaveTextContent("no-es-correo");
    expect(screen.getByTestId("cmp-query-programar-destinatarios-input")).toHaveValue("no-es-correo");
    // Con algo inválido sin agregar, Guardar no se deja usar: se leería como incluido y no lo está.
    expect(screen.getByTestId("cmp-query-programar-guardar")).toBeDisabled();
    // Al corregir el campo, Guardar vuelve.
    fireEvent.change(screen.getByTestId("cmp-query-programar-destinatarios-input"), { target: { value: "" } });
    expect(screen.getByTestId("cmp-query-programar-guardar")).toBeEnabled();
    expect(posts(peticiones)).toHaveLength(0);
  });

  it("cmp-query: guarda un envío semanal con el día, la hora y los correos normalizados", async () => {
    const { peticiones } = await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    fireEvent.change(screen.getByTestId("cmp-query-programar-dia-semana"), { target: { value: "5" } });
    fireEvent.change(screen.getByTestId("cmp-query-programar-hora"), { target: { value: "06:00" } });
    escribirCorreos("Flota@Empresa.com flota@empresa.com; jefe@empresa.com");
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toEqual({
      consultaId: "mia-1", periodicidad: "semanal", diaSemana: 5, diaMes: null, hora: "06:00",
      destinatarios: ["flota@empresa.com", "jefe@empresa.com"], activa: true,
    });
    expect(await screen.findByText(/Listo: cada viernes a las 06:00, a 2 destinatarios/)).toBeInTheDocument();
    expect(screen.getByTestId("cmp-query-programar-lista")).toHaveTextContent("Cada viernes a las 06:00");
  });

  it("cmp-query: el mensual manda el día del mes y no el de la semana", async () => {
    const { peticiones } = await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    fireEvent.change(screen.getByTestId("cmp-query-programar-periodicidad"), { target: { value: "mensual" } });
    fireEvent.change(screen.getByTestId("cmp-query-programar-dia-mes"), { target: { value: "28" } });
    escribirCorreos("a@b.co");
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toMatchObject({ periodicidad: "mensual", diaMes: 28, diaSemana: null });
  });

  it("cmp-query: un error del servidor se lee junto a Guardar y el formulario se conserva", async () => {
    await abrirPanel({ errorProgramar: { status: 422, code: "CONSULTA_INVALIDA", error: "La hora debe estar entre las 05:00 y las 22:00." } });
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    escribirCorreos("a@b.co");
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    expect(await screen.findByTestId("cmp-query-programar-error")).toHaveTextContent("La hora debe estar entre las 05:00 y las 22:00.");
    expect(chips()).toEqual(["a@b.co"]);
  });

  it("cmp-query: pausar guarda al instante con activa en no", async () => {
    const { panel, peticiones } = await abrirPanel({ programaciones: [PROGRAMADA] });
    const tarjeta = await within(panel).findByTestId("cmp-query-programar-envio-prog-1");
    fireEvent.click(within(tarjeta).getByTestId("cmp-query-programar-activo-prog-1"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toMatchObject({ id: "prog-1", activa: false, hora: "07:00", diaSemana: 1 });
    expect(await within(tarjeta).findByText("En pausa", { selector: "dd" })).toBeInTheDocument();
  });

  it("cmp-query: borrar se confirma en la tarjeta antes de llamar al servidor", async () => {
    const { panel, peticiones } = await abrirPanel({ programaciones: [PROGRAMADA] });
    await within(panel).findByTestId("cmp-query-programar-envio-prog-1");
    fireEvent.click(screen.getByTestId("cmp-query-programar-borrar-prog-1"));
    expect(peticiones.some((p) => p.metodo === "DELETE")).toBe(false);
    fireEvent.click(screen.getByTestId("cmp-query-programar-borrar-si-prog-1"));
    await waitFor(() => expect(peticiones.some((p) => p.metodo === "DELETE" && p.ruta === "/programaciones/prog-1")).toBe(true));
    expect(await screen.findByTestId("cmp-query-programar-vacio")).toBeInTheDocument();
  });
});

describe("cmp-query · programar envío (SuperAdmin y edición)", () => {
  it("cmp-query: el SuperAdmin en «todas» no ve envíos; con una empresa los ve en solo lectura y con ?empresa=", async () => {
    sesion.actual = { usuario: { id: "s", email: "s@x", esSuperAdmin: true }, permisos: [], empresa: null };
    instalarServidor();
    const { unmount } = render(<Page />);
    await screen.findByTestId("cmp-query-guardada-mia-1");
    expect(screen.queryByTestId("cmp-query-programar-mia-1")).not.toBeInTheDocument();
    unmount();

    window.history.replaceState(null, "", "/reportes/consultas?empresa=e1");
    const { peticiones } = instalarServidor({ programaciones: [PROGRAMADA] });
    render(<Page />);
    fireEvent.click(await screen.findByTestId("cmp-query-programar-mia-1"));
    const panel = await screen.findByTestId("cmp-query-programar");
    const tarjeta = await within(panel).findByTestId("cmp-query-programar-envio-prog-1");

    // Lo que sirve para soporte, a la vista.
    expect(tarjeta).toHaveTextContent("Cada lunes a las 07:00");
    expect(tarjeta).toHaveTextContent("flota@empresa.com");
    expect(within(tarjeta).getByTestId("cmp-query-programar-resultado-prog-1")).toHaveTextContent("Enviado a 3 destinatarios");
    expect(within(tarjeta).getByTestId("cmp-query-programar-estado-prog-1")).toHaveTextContent("Activo");
    expect(within(panel).getByTestId("cmp-query-programar-solo-lectura")).toHaveTextContent("El SuperAdmin consulta los envíos; los crea y edita la empresa.");

    // Nada que el servidor le vaya a rechazar.
    expect(within(panel).queryByTestId("cmp-query-programar-nuevo")).not.toBeInTheDocument();
    expect(within(tarjeta).queryByRole("checkbox")).not.toBeInTheDocument();
    expect(within(tarjeta).queryByTestId("cmp-query-programar-editar-prog-1")).not.toBeInTheDocument();
    expect(within(tarjeta).queryByTestId("cmp-query-programar-borrar-prog-1")).not.toBeInTheDocument();

    const deProgramacion = peticiones.filter((p) => p.ruta.startsWith("/programaciones"));
    expect(deProgramacion.map((p) => p.metodo)).toEqual(["GET"]);
    expect(deProgramacion[0].query.get("empresa")).toBe("e1");
  });

  it("cmp-query: el usuario de la empresa no ve la nota de solo lectura", async () => {
    const { panel } = await abrirPanel({ programaciones: [PROGRAMADA] });
    await within(panel).findByTestId("cmp-query-programar-envio-prog-1");
    expect(within(panel).queryByTestId("cmp-query-programar-solo-lectura")).not.toBeInTheDocument();
    expect(within(panel).getByTestId("cmp-query-programar-nuevo")).toBeInTheDocument();
  });

  it("cmp-query: editar precarga el envío y lo reemplaza sin duplicarlo", async () => {
    const { panel, peticiones } = await abrirPanel({ programaciones: [PROGRAMADA] });
    fireEvent.click(await within(panel).findByTestId("cmp-query-programar-editar-prog-1"));
    expect(screen.getByTestId("cmp-query-programar-dia-semana")).toHaveValue("1");
    expect(screen.getByTestId("cmp-query-programar-hora")).toHaveValue("07:00");
    expect(chips()).toEqual(["flota@empresa.com", "gerencia@empresa.com", "jefe@empresa.com"]);
    fireEvent.change(screen.getByTestId("cmp-query-programar-hora"), { target: { value: "08:00" } });
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toMatchObject({ id: "prog-1", hora: "08:00" });
    const lista = await screen.findByTestId("cmp-query-programar-lista");
    expect(within(lista).getAllByRole("listitem")).toHaveLength(1);
    expect(lista).toHaveTextContent("Cada lunes a las 08:00");
  });

  it("cmp-query: sin envíos previos lo dice y no inventa un resultado", async () => {
    const { panel } = await abrirPanel({
      programaciones: [{ ...PROGRAMADA, ultimaEjecucion: null, ultimoResultado: null }, { ...PROGRAMADA, id: "otra", consultaId: "otra-consulta" }],
    });
    const tarjeta = await within(panel).findByTestId("cmp-query-programar-envio-prog-1");
    expect(tarjeta).toHaveTextContent("Todavía no ha salido");
    expect(within(tarjeta).queryByText("Resultado")).not.toBeInTheDocument();
    // Solo los envíos de esta consulta.
    expect(within(panel).queryByTestId("cmp-query-programar-envio-otra")).not.toBeInTheDocument();
  });
});

describe("cmp-query · programar envío (teclado)", () => {
  it("cmp-query: Escape en la confirmación de borrado la cancela sin cerrar el panel", async () => {
    const { panel, peticiones } = await abrirPanel({ programaciones: [PROGRAMADA] });
    await within(panel).findByTestId("cmp-query-programar-envio-prog-1");
    fireEvent.click(screen.getByTestId("cmp-query-programar-borrar-prog-1"));
    fireEvent.keyDown(screen.getByTestId("cmp-query-programar-confirmar-prog-1"), { key: "Escape" });
    expect(screen.queryByTestId("cmp-query-programar-confirmar-prog-1")).not.toBeInTheDocument();
    expect(screen.getByTestId("cmp-query-programar")).toBeInTheDocument();
    expect(peticiones.some((p) => p.metodo === "DELETE")).toBe(false);
  });

  it("cmp-query: al abrir el formulario el foco va a su primer control", async () => {
    await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    expect(screen.getByTestId("cmp-query-programar-periodicidad")).toHaveFocus();
  });
});

describe("cmp-query · día de la semana ISO", () => {
  it("cmp-query: el domingo se envía como 7 y el selector ofrece 1–7 de lunes a domingo", async () => {
    const { peticiones } = await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    const select = screen.getByTestId("cmp-query-programar-dia-semana") as HTMLSelectElement;
    expect([...select.options].map((o) => [o.value, o.text])).toEqual([
      ["1", "Lunes"], ["2", "Martes"], ["3", "Miércoles"], ["4", "Jueves"], ["5", "Viernes"], ["6", "Sábado"], ["7", "Domingo"],
    ]);
    fireEvent.change(select, { target: { value: "7" } });
    escribirCorreos("a@b.co");
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toMatchObject({ periodicidad: "semanal", diaSemana: 7, diaMes: null });
  });
});

describe("cmp-query · programar envío como en Trámites", () => {
  it("cmp-query: es un modal centrado con título, subtítulo de la consulta y formato Excel fijo", async () => {
    await abrirPanel();
    const dialogo = screen.getByRole("dialog", { name: "Programar envío" });
    expect(dialogo).toHaveAttribute("aria-modal", "true");
    expect(dialogo).toHaveTextContent("«Mis placas de flota» llega por correo");
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    expect(screen.getByTestId("cmp-query-programar-formato")).toHaveTextContent("Excel");
    expect(screen.getByText("El resultado va como Excel adjunto.")).toBeInTheDocument();
    expect(screen.getByText("Presiona Enter o coma para agregar. Máximo 10 destinatarios.")).toBeInTheDocument();
  });

  it("cmp-query: pegar una columna de correos (con saltos de línea) los agrega todos", async () => {
    await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    fireEvent.paste(screen.getByTestId("cmp-query-programar-destinatarios-input"), {
      clipboardData: { getData: () => "Ana@Empresa.com\nluis@empresa.com\nana@empresa.com" },
    });
    expect(chips()).toEqual(["ana@empresa.com", "luis@empresa.com"]);
  });

  it("cmp-query: los destinatarios se agregan con coma y no pasan de 10", async () => {
    await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    const campo = screen.getByTestId("cmp-query-programar-destinatarios-input");
    fireEvent.change(campo, { target: { value: "uno@empresa.com," } });
    expect(chips()).toEqual(["uno@empresa.com"]);
    escribirCorreos(Array.from({ length: 15 }, (_, i) => `p${i}@empresa.com`).join(" "));
    expect(chips()).toHaveLength(10);
    expect(screen.getByTestId("cmp-query-programar-correos-invalidos")).toHaveTextContent("No puedes indicar más de 10 destinatarios.");
    fireEvent.click(screen.getByRole("button", { name: "Quitar uno@empresa.com" }));
    expect(chips()).toHaveLength(9);
  });
});

describe("cmp-query · programar envío (detalles del formulario)", () => {
  it("cmp-query: desmarcar Activo manda activa en no", async () => {
    const { peticiones } = await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    escribirCorreos("a@b.co");
    fireEvent.click(screen.getByTestId("cmp-query-programar-activo"));
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toMatchObject({ activa: false });
  });

  it("cmp-query: salir del campo agrega el correo escrito, y Backspace en vacío quita la última ficha", async () => {
    await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    const campo = screen.getByTestId("cmp-query-programar-destinatarios-input");
    fireEvent.change(campo, { target: { value: "uno@empresa.com" } });
    fireEvent.blur(campo);
    expect(chips()).toEqual(["uno@empresa.com"]);
    expect(screen.getByTestId("cmp-query-programar-guardar")).toBeEnabled();
    fireEvent.keyDown(campo, { key: "Backspace" });
    expect(chips()).toEqual([]);
  });

  it("cmp-query: Cancelar cierra el formulario y devuelve el foco a «Nuevo envío»", async () => {
    await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByTestId("cmp-query-programar-formulario")).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId("cmp-query-programar-nuevo")).toHaveFocus());
  });
});

describe("cmp-query · programar envío (reapertura y teclado)", () => {
  it("cmp-query: un correo inválido sin agregar no deja Guardar apagado al cerrar y volver a abrir", async () => {
    const { peticiones } = await abrirPanel({ programaciones: [PROGRAMADA] });
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    escribirCorreos("ana@");
    expect(screen.getByTestId("cmp-query-programar-guardar")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    await waitFor(() => expect(screen.queryByTestId("cmp-query-programar")).not.toBeInTheDocument());

    fireEvent.click(screen.getByTestId("cmp-query-programar-mia-1"));
    fireEvent.click(await screen.findByTestId("cmp-query-programar-editar-prog-1"));
    fireEvent.change(screen.getByTestId("cmp-query-programar-hora"), { target: { value: "09:00" } });
    expect(screen.getByTestId("cmp-query-programar-guardar")).toBeEnabled();
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
  });

  it("cmp-query: Escape con el formulario abierto cierra el modal, como en Trámites", async () => {
    await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    fireEvent.keyDown(screen.getByTestId("cmp-query-programar-periodicidad"), { key: "Escape" });
    await waitFor(() => expect(screen.queryByTestId("cmp-query-programar")).not.toBeInTheDocument());
  });

  it("cmp-query: escribir un correo a medias no apaga Guardar; solo un intento fallido de agregarlo", async () => {
    await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    escribirCorreos("a@b.co");
    fireEvent.change(screen.getByTestId("cmp-query-programar-destinatarios-input"), { target: { value: "jefe@emp" } });
    expect(screen.getByTestId("cmp-query-programar-guardar")).toBeEnabled();
  });

  it("cmp-query: si borrar falla, el foco vuelve a «Borrar envío» y el error se lee en la tarjeta", async () => {
    const { panel, fetchMock } = await abrirPanel({ programaciones: [PROGRAMADA] });
    await within(panel).findByTestId("cmp-query-programar-envio-prog-1");
    const original = fetchMock.getMockImplementation()!;
    fetchMock.mockImplementation(async (entrada, init) =>
      init?.method === "DELETE"
        ? new Response(JSON.stringify({ code: "ERROR", error: "No se pudo borrar ahora." }), { status: 500, headers: { "Content-Type": "application/json" } })
        : original(entrada, init),
    );
    fireEvent.click(screen.getByTestId("cmp-query-programar-borrar-prog-1"));
    fireEvent.click(screen.getByTestId("cmp-query-programar-borrar-si-prog-1"));
    expect(await within(panel).findByText("No se pudo borrar ahora.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId("cmp-query-programar-borrar-si-prog-1")).toHaveFocus());
  });
});

describe("cmp-query · hora en punto como Trámites", () => {
  const horas = () => [...(screen.getByTestId("cmp-query-programar-hora") as HTMLSelectElement).options].map((o) => o.value);

  it("cmp-query: un envío nuevo ofrece solo horas en punto, de 00:00 a 23:00", async () => {
    await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    expect(screen.getByTestId("cmp-query-programar-hora").tagName).toBe("SELECT");
    expect(horas()).toHaveLength(24);
    expect(horas()[0]).toBe("00:00");
    expect(horas()[23]).toBe("23:00");
    expect(screen.getByTestId("cmp-query-programar-hora")).toHaveValue("07:00");
  });

  it("cmp-query: al editar uno viejo de las 11:20, esa hora sigue ofrecida y se conserva al guardar", async () => {
    const { panel, peticiones } = await abrirPanel({ programaciones: [{ ...PROGRAMADA, hora: "11:20:00" }] });
    fireEvent.click(await within(panel).findByTestId("cmp-query-programar-editar-prog-1"));
    expect(screen.getByTestId("cmp-query-programar-hora")).toHaveValue("11:20");
    expect(horas()).toHaveLength(25);
    expect(horas().indexOf("11:20")).toBe(horas().indexOf("11:00") + 1);
    fireEvent.change(screen.getByTestId("cmp-query-programar-dia-semana"), { target: { value: "3" } });
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toMatchObject({ hora: "11:20", diaSemana: 3 });
  });
});

describe("cmp-query · utilidades de programación", () => {
  it("cmp-query: separa, normaliza y deduplica correos", () => {
    expect(separarCorreos(" A@b.co, a@b.co;\nmal ,x@y.com ")).toEqual({ validos: ["a@b.co", "x@y.com"], invalidos: ["mal"] });
  });
  it("cmp-query: describe la frecuencia en palabras", () => {
    // ISO: 7 = domingo y 1 = lunes. El 0 de .NET no es un día válido aquí.
    expect(describirFrecuencia({ periodicidad: "semanal", diaSemana: 7, diaMes: null, hora: "18:00:00" })).toBe("Cada domingo a las 18:00");
    expect(describirFrecuencia({ periodicidad: "semanal", diaSemana: 1, diaMes: null, hora: "07:00" })).toBe("Cada lunes a las 07:00");
    expect(describirFrecuencia({ periodicidad: "mensual", diaSemana: null, diaMes: 5, hora: "07:00" })).toBe("El día 5 de cada mes a las 07:00");
  });
});
