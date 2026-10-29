import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DrFlit } from "@/components/chat/DrFlit";
import { Landing } from "@/components/landing/Landing";
import { ASSETS_DR_FLIT } from "@/lib/assets";
import { SALUDO, SIN_INFORMACION } from "@/lib/chat/respuestas";
import { fetchFalso, json, ORGANISMOS, renderConQuery, TIPOS } from "./utilidades";

const CONTEXTO = { tipos: ["Trámite A"], organismos: ["Ciudad Uno"] };

beforeEach(() => vi.useFakeTimers());
afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

const lanzador = () => screen.getByRole("button", { name: /Abrir Dr\. FLIT/ });
const abrir = () => fireEvent.click(lanzador());
const esperarRespuesta = () => act(() => { vi.advanceTimersByTime(700); });
const escribir = (texto: string) => {
  fireEvent.change(screen.getByLabelText("Mensaje para DR. FLIT"), { target: { value: texto } });
  fireEvent.click(screen.getByRole("button", { name: "Enviar mensaje" }));
};

describe("Dr. FLIT: abrir, cerrar y foco", () => {
  it("cerrado: solo se ve el lanzador, con el estado del diálogo en aria-expanded", () => {
    render(<DrFlit />);

    expect(lanzador()).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("abre el widget con el saludo del diseño y lleva el foco al botón de minimizar", () => {
    render(<DrFlit />);
    abrir();

    const dialogo = screen.getByRole("dialog", { name: "Asistente virtual DR. FLIT" });
    expect(within(dialogo).getByRole("log", { name: "Conversación con DR. FLIT" })).toHaveTextContent(SALUDO);
    expect(screen.getByRole("button", { name: "Minimizar Dr. FLIT" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: "Minimizar chat" })).toHaveFocus();
  });

  it("Escape minimiza y devuelve el foco al lanzador", () => {
    render(<DrFlit />);
    abrir();

    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(lanzador()).toHaveFocus();
  });

  it("el botón de minimizar y el lanzador también cierran", () => {
    render(<DrFlit />);
    abrir();
    fireEvent.click(screen.getByRole("button", { name: "Minimizar chat" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    abrir();
    fireEvent.click(screen.getByRole("button", { name: "Minimizar Dr. FLIT" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("advierte que es una demostración sin IA y que no se escriban datos personales", () => {
    render(<DrFlit />);
    abrir();

    expect(screen.getByRole("dialog")).toHaveAccessibleDescription(/no usa inteligencia artificial.*No escribas datos personales/);
  });

  it("sin imágenes entregadas muestra el marcador del avatar y no una imagen inventada", () => {
    expect(ASSETS_DR_FLIT).toEqual({ lanzador: null, avatar: null, completo: null });
    const { container } = render(<DrFlit />);
    abrir();

    expect(container.querySelectorAll("img:not([role])")).toHaveLength(0);
    expect(container.querySelectorAll("[data-marcador='dr-flit']").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByRole("img", { name: "Robot asistente DR. FLIT" })).toBeInTheDocument();
  });
});

describe("Dr. FLIT: conversación simulada", () => {
  it("una pregunta frecuente muestra «escribiendo», luego la respuesta y sugerencias relacionadas", () => {
    render(<DrFlit contexto={CONTEXTO} />);
    abrir();

    fireEvent.click(screen.getByRole("button", { name: "¿Cuánto tarda mi trámite?" }));
    const registro = screen.getByRole("log");
    expect(registro).toHaveTextContent("¿Cuánto tarda mi trámite?");
    expect(screen.getByRole("status")).toHaveTextContent("DR. FLIT está escribiendo…");
    expect(screen.getByRole("button", { name: "Enviar mensaje" })).toBeDisabled();

    esperarRespuesta();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(registro).toHaveTextContent("no puedo asegurarte un plazo ni una aprobación");
    const sugerencias = screen.getByRole("group", { name: "Sugerencias relacionadas" });
    expect(within(sugerencias).getByRole("button", { name: "¿Cómo consulto mi trámite?" })).toBeInTheDocument();
  });

  it("texto libre: responde por tema usando las listas recibidas", () => {
    render(<DrFlit contexto={CONTEXTO} />);
    abrir();

    escribir("¿en que organismos puedo radicar?");
    esperarRespuesta();

    expect(screen.getByRole("log")).toHaveTextContent("Organismos de tránsito habilitados: Ciudad Uno.");
    expect(screen.getByLabelText("Mensaje para DR. FLIT")).toHaveValue("");
  });

  it("sin tema responde que no tiene la información, sin inventar un canal", () => {
    render(<DrFlit />);
    abrir();

    escribir("hablemos de futbol");
    esperarRespuesta();

    expect(screen.getByRole("log")).toHaveTextContent(SIN_INFORMACION);
  });

  it("Enter envía y Mayús+Enter no; un mensaje vacío no se envía", () => {
    render(<DrFlit />);
    abrir();
    const campo = screen.getByLabelText("Mensaje para DR. FLIT");
    expect(screen.getByRole("button", { name: "Enviar mensaje" })).toBeDisabled();

    fireEvent.change(campo, { target: { value: "documentos" } });
    fireEvent.keyDown(campo, { key: "Enter", shiftKey: true });
    expect(screen.queryByRole("status")).not.toBeInTheDocument();

    fireEvent.keyDown(campo, { key: "Enter" });
    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("el historial se conserva en memoria mientras se minimiza y se vuelve a abrir", () => {
    render(<DrFlit contexto={CONTEXTO} />);
    abrir();
    escribir("documentos");
    esperarRespuesta();

    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    abrir();

    const registro = screen.getByRole("log");
    expect(registro).toHaveTextContent(SALUDO);
    expect(registro).toHaveTextContent("documentos");
    expect(registro).toHaveTextContent("Los documentos dependen del tipo de trámite");
  });

  it("lo escrito no sale del componente: ni almacenamiento, ni consola, ni URL, ni red", () => {
    const consola = [vi.spyOn(console, "log"), vi.spyOn(console, "error"), vi.spyOn(console, "warn"), vi.spyOn(console, "info")];
    const fetchEspia = vi.fn();
    vi.stubGlobal("fetch", fetchEspia);
    const setItem = vi.spyOn(Storage.prototype, "setItem");
    const url = window.location.href;
    render(<DrFlit contexto={CONTEXTO} />);
    abrir();

    escribir("mi cedula es 123456789");
    esperarRespuesta();

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(setItem).not.toHaveBeenCalled();
    for (const c of consola) expect(c).not.toHaveBeenCalled();
    expect(fetchEspia).not.toHaveBeenCalled();
    expect(window.location.href).toBe(url);
    vi.unstubAllGlobals();
  });

  it("limita el largo del mensaje", () => {
    render(<DrFlit />);
    abrir();

    expect(screen.getByLabelText("Mensaje para DR. FLIT")).toHaveAttribute("maxLength", "300");
  });
});

describe("landing: Dr. FLIT con los catálogos del BFF y enlaces legales", () => {
  const OK = { "/organismos": () => json(ORGANISMOS), "/tipos-tramite": () => json(TIPOS) };

  it("responde con los trámites disponibles y los organismos del back, no con una lista fija", async () => {
    vi.useRealTimers();
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);
    await screen.findByText("Villa Dos");
    vi.useFakeTimers();

    abrir();
    fireEvent.click(screen.getByRole("button", { name: "¿Qué trámites puedo realizar?" }));
    esperarRespuesta();
    expect(screen.getByRole("log")).toHaveTextContent("Trámites habilitados: Trámite A, Trámite C.");
    expect(screen.getByRole("log")).not.toHaveTextContent("Trámite B");

    fireEvent.click(within(screen.getByRole("group", { name: "Preguntas frecuentes" })).getByRole("button", { name: "¿En qué organismos puedo radicar?" }));
    esperarRespuesta();
    expect(screen.getByRole("log")).toHaveTextContent("Ciudad Uno, Villa Dos, Ñandú del Sur.");
  });

  it("el enlace legal del pie abre la política pública de FLIT 2", async () => {
    vi.useRealTimers();
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);

    const politica = screen.getByRole("link", { name: /Política de tratamiento de datos personales/ });
    expect(politica).toHaveAttribute("href", "https://flitsas.com.co/privacy-policy/");
    expect(politica).toHaveAttribute("rel", "noopener noreferrer");
  });
});

describe("enlaces legales: un único módulo", () => {
  const RAIZ = path.resolve(__dirname, "..");
  const fuentes = (dir: string): string[] =>
    readdirSync(dir).flatMap((n) => {
      const ruta = path.join(dir, n);
      if (n === "node_modules" || n === ".next" || n === "__tests__") return [];
      return statSync(ruta).isDirectory() ? fuentes(ruta) : /\.tsx?$/.test(n) ? [ruta] : [];
    });
  const codigo = ["app", "lib", "components"].flatMap((d) => fuentes(path.join(RAIZ, d))).map((f) => ({ f: path.relative(RAIZ, f).replaceAll("\\", "/"), s: readFileSync(f, "utf8") }));

  it("el título del documento y su mensaje de no disponible solo se escriben en lib/legal.ts", () => {
    const sueltos = codigo.filter(({ f, s }) => f !== "lib/legal.ts" && /["'`][^"'`\n]*(Políticas de Privacidad|Términos de Uso|documento oficial aún no)/.test(s)).map(({ f }) => f);

    expect(sueltos).toEqual([]);
  });

  it("ningún enlace legal apunta a una URL escrita a mano", () => {
    const enlaces = codigo.filter(({ s }) => /href=["'`][^"'`]*(privacidad|politica|terminos|habeas)/i.test(s)).map(({ f }) => f);

    expect(enlaces).toEqual([]);
  });
});
