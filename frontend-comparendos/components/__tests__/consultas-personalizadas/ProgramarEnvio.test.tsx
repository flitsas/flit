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
    fireEvent.change(screen.getByTestId("cmp-query-programar-destinatarios"), { target: { value: "Flota@Empresa.com, no-es-correo" } });
    expect(screen.getByTestId("cmp-query-programar-correos-invalidos")).toHaveTextContent("no-es-correo");
    expect(screen.getByTestId("cmp-query-programar-guardar")).toBeDisabled();
    expect(posts(peticiones)).toHaveLength(0);
  });

  it("cmp-query: guarda un envío semanal con el día, la hora y los correos normalizados", async () => {
    const { peticiones } = await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    fireEvent.change(screen.getByTestId("cmp-query-programar-dia-semana"), { target: { value: "5" } });
    fireEvent.change(screen.getByTestId("cmp-query-programar-hora"), { target: { value: "06:30" } });
    fireEvent.change(screen.getByTestId("cmp-query-programar-destinatarios"), { target: { value: "Flota@Empresa.com\nflota@empresa.com; jefe@empresa.com" } });
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toEqual({
      consultaId: "mia-1", periodicidad: "semanal", diaSemana: 5, diaMes: null, hora: "06:30",
      destinatarios: ["flota@empresa.com", "jefe@empresa.com"], activa: true,
    });
    expect(await screen.findByText(/Listo: cada viernes a las 06:30, a 2 destinatarios/)).toBeInTheDocument();
    expect(screen.getByTestId("cmp-query-programar-lista")).toHaveTextContent("Cada viernes a las 06:30");
  });

  it("cmp-query: el mensual manda el día del mes y no el de la semana", async () => {
    const { peticiones } = await abrirPanel();
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    fireEvent.click(screen.getByRole("radio", { name: "Cada mes" }));
    fireEvent.change(screen.getByTestId("cmp-query-programar-dia-mes"), { target: { value: "28" } });
    fireEvent.change(screen.getByTestId("cmp-query-programar-destinatarios"), { target: { value: "a@b.co" } });
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toMatchObject({ periodicidad: "mensual", diaMes: 28, diaSemana: null });
  });

  it("cmp-query: un error del servidor se lee junto a Guardar y el formulario se conserva", async () => {
    await abrirPanel({ errorProgramar: { status: 422, code: "CONSULTA_INVALIDA", error: "La hora debe estar entre las 05:00 y las 22:00." } });
    fireEvent.click(await screen.findByTestId("cmp-query-programar-nuevo"));
    fireEvent.change(screen.getByTestId("cmp-query-programar-destinatarios"), { target: { value: "a@b.co" } });
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    expect(await screen.findByTestId("cmp-query-programar-error")).toHaveTextContent("La hora debe estar entre las 05:00 y las 22:00.");
    expect(screen.getByTestId("cmp-query-programar-destinatarios")).toHaveValue("a@b.co");
  });

  it("cmp-query: pausar guarda al instante con activa en no", async () => {
    const { panel, peticiones } = await abrirPanel({ programaciones: [PROGRAMADA] });
    const tarjeta = await within(panel).findByTestId("cmp-query-programar-envio-prog-1");
    fireEvent.click(within(tarjeta).getByRole("switch"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toMatchObject({ id: "prog-1", activa: false, hora: "07:00", diaSemana: 1 });
    expect(await within(tarjeta).findByText("En pausa")).toBeInTheDocument();
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
  it("cmp-query: el SuperAdmin en «todas» no programa; con una empresa elegida, todo va con ?empresa=", async () => {
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
    const tarjeta = await screen.findByTestId("cmp-query-programar-envio-prog-1");
    fireEvent.click(within(tarjeta).getByRole("switch"));
    // Con la pausa en vuelo, la tarjeta no acepta otra operación: se espera a que termine.
    expect(screen.getByTestId("cmp-query-programar-borrar-prog-1")).toBeDisabled();
    expect(await within(tarjeta).findByText("En pausa")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId("cmp-query-programar-borrar-prog-1")).toBeEnabled());
    fireEvent.click(screen.getByTestId("cmp-query-programar-borrar-prog-1"));
    fireEvent.click(screen.getByTestId("cmp-query-programar-borrar-si-prog-1"));
    await waitFor(() => expect(peticiones.some((p) => p.metodo === "DELETE")).toBe(true));
    const deProgramacion = peticiones.filter((p) => p.ruta.startsWith("/programaciones"));
    expect(deProgramacion.map((p) => p.metodo).sort()).toEqual(["DELETE", "GET", "POST"]);
    expect(deProgramacion.every((p) => p.query.get("empresa") === "e1")).toBe(true);
  });

  it("cmp-query: editar precarga el envío y lo reemplaza sin duplicarlo", async () => {
    const { panel, peticiones } = await abrirPanel({ programaciones: [PROGRAMADA] });
    fireEvent.click(await within(panel).findByTestId("cmp-query-programar-editar-prog-1"));
    expect(screen.getByTestId("cmp-query-programar-dia-semana")).toHaveValue("1");
    expect(screen.getByTestId("cmp-query-programar-hora")).toHaveValue("07:00");
    expect(screen.getByTestId("cmp-query-programar-destinatarios")).toHaveValue("flota@empresa.com\ngerencia@empresa.com\njefe@empresa.com");
    fireEvent.change(screen.getByTestId("cmp-query-programar-hora"), { target: { value: "08:15" } });
    fireEvent.click(screen.getByTestId("cmp-query-programar-guardar"));
    await waitFor(() => expect(posts(peticiones)).toHaveLength(1));
    expect(posts(peticiones)[0]).toMatchObject({ id: "prog-1", hora: "08:15" });
    const lista = await screen.findByTestId("cmp-query-programar-lista");
    expect(within(lista).getAllByRole("listitem")).toHaveLength(1);
    expect(lista).toHaveTextContent("Cada lunes a las 08:15");
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
    expect(screen.getByRole("radio", { name: "Cada semana" })).toHaveFocus();
  });
});

describe("cmp-query · utilidades de programación", () => {
  it("cmp-query: separa, normaliza y deduplica correos", () => {
    expect(separarCorreos(" A@b.co, a@b.co;\nmal ,x@y.com ")).toEqual({ validos: ["a@b.co", "x@y.com"], invalidos: ["mal"] });
  });
  it("cmp-query: describe la frecuencia en palabras", () => {
    expect(describirFrecuencia({ periodicidad: "semanal", diaSemana: 0, diaMes: null, hora: "18:00:00" })).toBe("Cada domingo a las 18:00");
    expect(describirFrecuencia({ periodicidad: "mensual", diaSemana: null, diaMes: 5, hora: "07:00" })).toBe("El día 5 de cada mes a las 07:00");
  });
});
