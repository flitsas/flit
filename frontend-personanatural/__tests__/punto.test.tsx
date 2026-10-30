import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { CanalPunto } from "@/components/punto/CanalPunto";
import { crearCliente } from "@/lib/api/cliente";
import { tiposDelPunto } from "@/lib/landing/puerta-iniciar";
import { json, renderConQuery } from "./utilidades";

type Manejador = (init: RequestInit) => Response | Promise<Response>;

const ENTRADA = { organismoId: "o-1", codigo: "BOG", nombre: "Movilidad de Bogotá" };

const MENSAJE_PLACA = "No encontramos un vehículo con esa placa.";
const MENSAJE_ORGANISMO = "Este trámite se hace en el organismo que tiene el vehículo.";
const MENSAJE_RUNT = "No pudimos consultar el RUNT. Inténtalo de nuevo.";

function montar(rutas: Record<string, Manejador> = {}) {
  const todas: Record<string, Manejador> = {
    "POST /punto/entrada": () => json(ENTRADA),
    ...rutas,
  };
  const llamadas: { clave: string; init: RequestInit }[] = [];
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push({ clave, init });
    return (todas[clave] ?? (() => json({ code: "no_simulado", message: clave }, 404)))(init);
  }) as unknown as typeof fetch;
  renderConQuery(<CanalPunto codigo="BOG" cliente={crearCliente({ fetchImpl })} />);
  return { llamadas };
}

const cuerpo = (llamadas: { clave: string; init: RequestInit }[], clave: string) => {
  const encontrada = [...llamadas].reverse().find((l) => l.clave === clave);
  return JSON.parse(String(encontrada!.init.body));
};

async function elegirTipo(etiqueta = "Traspaso") {
  fireEvent.click(screen.getByRole("button", { name: "Tipo de Trámite" }));
  fireEvent.click(await screen.findByRole("option", { name: etiqueta }));
}

async function llenar({ placa = "ABC123", documento = "1020456789", tipoNombre = "Traspaso" } = {}) {
  fireEvent.change(screen.getByLabelText("Placa"), { target: { value: placa } });
  fireEvent.change(screen.getByLabelText("Número de documento"), { target: { value: documento } });
  await elegirTipo(tipoNombre);
}

async function abrirConsultar() {
  fireEvent.click(await screen.findByRole("button", { name: "Consultar mi trámite" }));
  return within(await screen.findByRole("dialog"));
}

describe("canal del punto /o/codigo", () => {
  it("si la entrada falla, muestra el message y no abre Iniciar ni Consultar", async () => {
    montar({
      "POST /punto/entrada": () => json({ code: "canal_apagado", message: "Este canal está apagado." }, 422),
    });

    expect(await screen.findByRole("alert")).toHaveTextContent("Este canal está apagado.");
    expect(screen.queryByRole("heading", { name: "Iniciar mi trámite" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Consultar mi trámite" })).not.toBeInTheDocument();
    expect(screen.queryByText(/cotizar/i)).not.toBeInTheDocument();
  });

  it("al entrar muestra Iniciar y Consultar, sin Cotizar ni contraseña", async () => {
    montar();

    expect(await screen.findByRole("heading", { name: "Movilidad de Bogotá" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Iniciar mi trámite" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Consultar mi trámite" })).toBeInTheDocument();
    expect(screen.queryByText(/cotizar/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/contraseña/i)).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
    expect(screen.getByRole("img", { name: "FLIT" })).toBeInTheDocument();

    const dialogo = await abrirConsultar();
    expect(dialogo.getByLabelText("Placa")).toBeInTheDocument();
    expect(dialogo.getByLabelText("Número de Radicado o Cédula")).toBeInTheDocument();
    expect(dialogo.queryByRole("button", { name: "Consultar vehículo" })).not.toBeInTheDocument();
    expect(dialogo.queryByRole("button", { name: "Consultar persona" })).not.toBeInTheDocument();
    expect(dialogo.queryByRole("button", { name: "Consultar multas" })).not.toBeInTheDocument();
  });

  it("Iniciar sigue deshabilitado hasta placa, documento y tipo", async () => {
    montar();
    const boton = await screen.findByRole("button", { name: "Iniciar trámite" });
    expect(boton).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "ABC123" } });
    expect(boton).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Número de documento"), { target: { value: "1020456789" } });
    expect(boton).toBeDisabled();

    expect(tiposDelPunto.map((t) => t.nombre)).toEqual([
      "Traspaso",
      "Cambio de color",
      "Cambio de carrocería",
      "Conversión de combustible",
      "Duplicado de placa",
      "Duplicado de matrícula",
      "Levantar prenda",
    ]);
    await elegirTipo("Traspaso");
    expect(boton).toBeEnabled();
  });

  it.each([
    ["placa", MENSAJE_PLACA],
    ["otro organismo", MENSAJE_ORGANISMO],
    ["RUNT", MENSAJE_RUNT],
  ])("muestra el mensaje de %s del arranque en el AvisoModal del portal", async (_caso, mensaje) => {
    const { llamadas } = montar({
      "POST /punto/arranque": () => json({ puedeSeguir: false, mensaje }),
    });
    await screen.findByRole("button", { name: "Iniciar trámite" });
    await llenar();
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));

    const dialogo = await screen.findByRole("dialog", { name: "No pudimos continuar" });
    expect(within(dialogo).getByRole("alert")).toHaveTextContent(mensaje);
    expect(screen.queryByRole("heading", { name: "Puedes continuar al diagnóstico" })).not.toBeInTheDocument();
    expect(cuerpo(llamadas, "POST /punto/arranque")).toEqual({
      placa: "ABC123",
      documento: "1020456789",
      tipoCodigo: "TRASPASO_STANDARD",
    });
    expect(JSON.stringify(cuerpo(llamadas, "POST /punto/arranque"))).not.toMatch(/password|contraseña/i);
  });

  it("mientras arranca muestra el MobilityLoader del portal (overlay + frase de espera)", async () => {
    montar({ "POST /punto/arranque": () => new Promise<Response>(() => {}) });
    await screen.findByRole("button", { name: "Iniciar trámite" });
    await llenar();
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));
    const frase = await screen.findByText("Consultando el vehículo…");
    const espera = frase.closest('[role="status"]');
    expect(espera).not.toBeNull();
    expect(espera?.querySelector(".mobility-orbit")).not.toBeNull();
  });

  it("si puede seguir, deja el paso de diagnóstico y Volver permite consultar otra vez", async () => {
    const { llamadas } = montar({
      "POST /punto/arranque": () => json({ puedeSeguir: true }),
    });
    await screen.findByRole("button", { name: "Iniciar trámite" });
    await llenar();
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));

    expect(await screen.findByRole("heading", { name: "Puedes continuar al diagnóstico" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Volver" }));
    expect(screen.getByLabelText("Placa")).toHaveValue("ABC123");
    expect(screen.getByLabelText("Número de documento")).toHaveValue("1020456789");

    fireEvent.change(screen.getByLabelText("Placa"), { target: { value: "XYZ987" } });
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));
    await waitFor(() => expect(llamadas.filter((l) => l.clave === "POST /punto/arranque")).toHaveLength(2));
    expect(cuerpo(llamadas, "POST /punto/arranque")).toMatchObject({ placa: "XYZ987", documento: "1020456789" });
  });
});

describe("consultar en el punto", () => {
  it("consulta un radicado existente y no abre formularios de vehículo, persona ni multas", async () => {
    const { llamadas } = montar({
      "POST /radicados/consulta": () =>
        json({
          radicado: "PN-2026-0001",
          numeroReferencia: null,
          placa: "ABC123",
          tipoTramite: "TRASPASO_STANDARD",
          organismo: "Movilidad de Bogotá",
          macroEstado: { codigo: "en_revision_ot", etiqueta: "En Revisión OT", indice: 1 },
          mensaje: "Tu trámite está en curso y aún no presenta respuesta.",
          pasos: [
            { clave: "borrador", titulo: "Borrador del trámite", estado: "done", fecha: "2026-10-20T14:15:00Z" },
            { clave: "validacion_identidad", titulo: "Validación de identidad", estado: "done", fecha: "2026-10-20T16:42:00Z" },
            { clave: "enviado_ot", titulo: "Enviado al OT", estado: "done", fecha: "2026-10-21T13:03:00Z" },
            { clave: "en_revision", titulo: "En revisión", estado: "current", fecha: "2026-10-22T19:26:00Z" },
            { clave: "resolucion", titulo: "Aprobado / Rechazado", estado: "pending", fecha: null },
          ],
          subsanacion: null,
          soloLectura: false,
        }),
    });

    const dialogo = await abrirConsultar();
    expect(screen.getByRole("dialog", { name: "Consultar mi trámite" })).toBeInTheDocument();
    expect(dialogo.queryByLabelText("Placa del vehículo")).not.toBeInTheDocument();
    expect(dialogo.queryByLabelText(/documento de la persona/i)).not.toBeInTheDocument();
    expect(dialogo.queryByLabelText(/placa de las multas/i)).not.toBeInTheDocument();

    fireEvent.change(dialogo.getByLabelText("Placa"), { target: { value: "abc123" } });
    fireEvent.change(dialogo.getByLabelText("Número de Radicado o Cédula"), { target: { value: "pn-2026-0001" } });
    fireEvent.click(dialogo.getByRole("button", { name: "Consultar" }));

    expect(await dialogo.findByRole("heading", { name: "Tu trámite está en proceso" })).toBeInTheDocument();
    expect(cuerpo(llamadas, "POST /radicados/consulta")).toEqual({
      placa: "ABC123",
      radicado: "PN-2026-0001",
    });
    const claves = llamadas.map((l) => l.clave);
    expect(claves).not.toContain("POST /punto/consultas/vehiculo");
    expect(claves).not.toContain("POST /punto/consultas/persona");
    expect(claves).not.toContain("POST /punto/consultas/multas");
    expect(claves).not.toContain("POST /sesiones");
    expect(claves).not.toContain("POST /punto/arranque");
  });
});
