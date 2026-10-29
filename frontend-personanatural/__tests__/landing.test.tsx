import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Landing } from "@/components/landing/Landing";
import { SelectorOrganismo } from "@/components/landing/SelectorOrganismo";
import { filtrarOrganismos, tiposDisponibles } from "@/lib/landing/catalogos";
import { PASOS_TRAMITE, PREGUNTAS_FRECUENTES } from "@/lib/landing/contenido";
import { fetchFalso, json, ORGANISMOS, renderConQuery, TIPOS } from "./utilidades";

const TIPOS_PUERTA = [
  ...TIPOS,
  { code: "CAMBIO_COLOR", name: "Cambio de color", family: "OTROS", version: 1, disponible: true },
  { code: "TRASPASO_STANDARD", name: "Traspaso", family: "TRASPASO", version: 1, disponible: true },
];
const OK = { "/organismos": () => json(ORGANISMOS), "/tipos-tramite": () => json(TIPOS_PUERTA) };
const pendiente = () => new Promise<Response>(() => {});

describe("landing: organismos desde el BFF", () => {
  it("cargando: muestra el cargador con role=status mientras el BFF responde", () => {
    const { cliente } = fetchFalso({ ...OK, "/organismos": pendiente });
    renderConQuery(<Landing cliente={cliente} />);

    const columna = screen.getByRole("region", { name: "Organismos de tránsito integrados" });
    expect(within(columna).getByRole("status")).toHaveTextContent("Cargando...");
    expect(columna).toHaveAttribute("aria-busy", "true");
  });

  it("lleno: lista los organismos del BFF y los pide a /organismos y /tipos-tramite", async () => {
    const { cliente, llamadas } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);

    expect(await screen.findByText("Villa Dos")).toBeInTheDocument();
    expect(screen.getByText("Ñandú del Sur")).toBeInTheDocument();
    expect(llamadas).toContain("/organismos");
    expect(llamadas).toContain("/tipos-tramite");
  });

  it("el buscador filtra sin distinguir mayúsculas ni tildes y avisa si no hay coincidencias", async () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);
    await screen.findByText("Villa Dos");
    const buscador = screen.getByLabelText("Buscar organismo de tránsito");

    fireEvent.change(buscador, { target: { value: "NANDU" } });
    expect(screen.getByText("Ñandú del Sur")).toBeInTheDocument();
    expect(screen.queryByText("Villa Dos")).not.toBeInTheDocument();

    fireEvent.change(buscador, { target: { value: "zzz" } });
    expect(screen.getByText("No hay organismos que coincidan.")).toBeInTheDocument();
  });

  it("vacío: un catálogo sin organismos muestra el mismo aviso", async () => {
    const { cliente } = fetchFalso({ ...OK, "/organismos": () => json([]) });
    renderConQuery(<Landing cliente={cliente} />);

    expect(await screen.findByText("No hay organismos que coincidan.")).toBeInTheDocument();
  });

  it("error: muestra el mensaje del error, la landing sigue usable y «Reintentar» vuelve a pedir", async () => {
    let falla = true;
    const { cliente } = fetchFalso({ ...OK, "/organismos": () => (falla ? json({ code: "x", title: "Fallo" }, 503) : json(ORGANISMOS)) });
    renderConQuery(<Landing cliente={cliente} />);

    const mensaje = await screen.findByText(/El servicio no está disponible en este momento/);
    expect(mensaje.closest('[role="alert"]')).not.toBeNull();
    expect(screen.getByRole("link", { name: "Trámites" })).toBeInTheDocument(); // el resto de la landing sigue ahí
    falla = false;
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));

    expect(await screen.findByText("Ciudad Uno")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
  });
});

describe("landing: tipos de trámite desde el BFF", () => {
  it("el selector ofrece solo los tipos disponibles y permite elegir uno", async () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);
    const selector = screen.getByRole("button", { name: "Tipo de Trámite" });
    await waitFor(() => expect(selector).toBeEnabled());

    fireEvent.click(selector);
    const opciones = within(screen.getByRole("listbox", { name: "Tipo de Trámite" })).getAllByRole("option");
    expect(opciones.map((o) => o.textContent)).toEqual(["Cambio de color", "Traspaso"]);

    fireEvent.click(opciones[1]);
    expect(screen.getByRole("button", { name: "Tipo de Trámite" })).toHaveTextContent("Traspaso");
  });

  it("se opera con teclado: flechas, Enter y Escape", async () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);
    const selector = screen.getByRole("button", { name: "Tipo de Trámite" });
    await waitFor(() => expect(selector).toBeEnabled());

    fireEvent.keyDown(selector, { key: "ArrowDown" });
    const lista = screen.getByRole("listbox", { name: "Tipo de Trámite" });
    fireEvent.keyDown(lista, { key: "ArrowDown" });
    fireEvent.keyDown(lista, { key: "Enter" });
    expect(selector).toHaveTextContent("Traspaso");

    fireEvent.keyDown(selector, { key: "ArrowDown" });
    fireEvent.keyDown(screen.getByRole("listbox"), { key: "Escape" });
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(selector).toHaveFocus();
  });

  it("cargando: el selector queda deshabilitado con «Cargando...»", () => {
    const { cliente } = fetchFalso({ ...OK, "/tipos-tramite": pendiente });
    renderConQuery(<Landing cliente={cliente} />);

    const selector = screen.getByRole("button", { name: "Tipo de Trámite" });
    expect(selector).toBeDisabled();
    expect(selector).toHaveTextContent("Cargando...");
  });

  it("vacío: avisa que no hay trámites y el selector no se puede abrir", async () => {
    const { cliente } = fetchFalso({ ...OK, "/tipos-tramite": () => json([]) });
    renderConQuery(<Landing cliente={cliente} />);

    expect(await screen.findByText("Por ahora no hay trámites disponibles en el portal.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tipo de Trámite" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Iniciar trámite" })).toBeDisabled();
  });

  it("error: muestra el mensaje y permite reintentar sin tocar los organismos", async () => {
    let falla = true;
    const { cliente } = fetchFalso({ ...OK, "/tipos-tramite": () => (falla ? json({ code: "x" }, 503) : json(TIPOS_PUERTA)) });
    renderConQuery(<Landing cliente={cliente} />);

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("El servicio no está disponible en este momento");
    falla = false;
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Tipo de Trámite" })).toBeEnabled());
    expect(screen.getByText("Villa Dos")).toBeInTheDocument();
  });

  it("las filas de consultar y cotizar están activas; «Iniciar trámite» espera a tener los tres datos", () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);

    expect(screen.getByRole("button", { name: "Consultar mi trámite" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Cotizar mi trámite" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Iniciar trámite" })).toBeDisabled();
  });

  it("cédula solo dígitos y placa en mayúsculas, sin guardarlos en el navegador", () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);
    const cedula = screen.getByLabelText("Número de Cédula");
    const placa = screen.getByLabelText("Placa del vehículo");

    fireEvent.change(cedula, { target: { value: "10a2-0" } });
    fireEvent.change(placa, { target: { value: "ab-c12345" } });

    expect(cedula).toHaveValue("1020");
    expect(placa).toHaveValue("ABC123");
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});

describe("landing: estructura, accesibilidad y contenido", () => {
  it("tiene los puntos de referencia y los títulos del diseño", () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);

    expect(screen.getByRole("banner")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Secciones" })).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
    expect(screen.getByRole("complementary", { name: "Servicios y organismos" })).toBeInTheDocument();
    expect(screen.getByRole("contentinfo")).toBeInTheDocument();
    for (const titulo of ["Iniciar mi trámite", "4 simples pasos para tu trámite", "¿Cómo radicar mi trámite?", "Somos FLIT", "Servicios Adicionales", "Otros Servicios Vehiculares", "Preguntas Frecuentes"]) {
      expect(screen.getByRole("heading", { name: titulo })).toBeInTheDocument();
    }
    const pasos = within(screen.getByRole("region", { name: "Cuatro simples pasos para tu trámite" })).getAllByRole("listitem");
    expect(pasos).toHaveLength(PASOS_TRAMITE.length);
  });

  it("los banners disponibles llevan texto alternativo y los puntos cambian de banner", () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);

    const puntos = screen.getAllByRole("button", { name: /Ir al banner/ });
    expect(puntos).toHaveLength(2);
    expect(puntos[0]).toHaveAttribute("aria-current", "true");
    fireEvent.click(puntos[1]);
    expect(puntos[1]).toHaveAttribute("aria-current", "true");
    expect(screen.getAllByRole("img", { hidden: true }).some((i) => i.getAttribute("alt")?.startsWith("Tus trámites de tránsito"))).toBe(true);
  });

  it("el video pendiente deja su marco con aviso accesible", () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);

    expect(screen.getByRole("img", { name: "Video explicativo disponible próximamente" })).toBeInTheDocument();
  });

  it("FAQ: el acordeón abre una pregunta a la vez y no explica un pago del trámite", () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);

    expect(JSON.stringify(PREGUNTAS_FRECUENTES)).not.toMatch(/PSE|transferencia|Bancolombia|comprobante del pago/);
    expect(PREGUNTAS_FRECUENTES).toHaveLength(4);
    const identidad = screen.getByRole("button", { name: "¿Cómo se realiza la validación de identidad biométrica?" });
    const entrega = screen.getByRole("button", { name: "¿Cómo me entregan la tarjeta de propiedad?" });
    expect(identidad).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(identidad);
    expect(identidad).toHaveAttribute("aria-expanded", "true");
    fireEvent.click(entrega);
    expect(identidad).toHaveAttribute("aria-expanded", "false");
    expect(entrega).toHaveAttribute("aria-expanded", "true");
  });

  it("los botones de servicios abren el aviso «Próximamente» en un diálogo", async () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);

    fireEvent.click(screen.getByRole("button", { name: "Compra aquí" }));

    const dialogo = await screen.findByRole("dialog", { name: "Compra aquí" });
    expect(dialogo).toHaveTextContent("Próximamente");
  });

  it("el pie enlaza la política pública de FLIT 2", async () => {
    const { cliente } = fetchFalso(OK);
    renderConQuery(<Landing cliente={cliente} />);

    const politica = screen.getByRole("link", { name: /Política de tratamiento de datos personales/ });
    expect(politica).toHaveAttribute("href", "https://flitsas.com.co/privacy-policy/");
    expect(politica).toHaveAttribute("target", "_blank");
    expect(politica).toHaveAttribute("rel", "noopener noreferrer");
  });
});

describe("SelectorOrganismo (para el modal de cotizar, HU E4)", () => {
  it("abre, filtra sin tildes y elige un organismo con teclado", () => {
    const elegido: string[] = [];
    renderConQuery(<SelectorOrganismo organismos={ORGANISMOS} valor={null} onChange={(o) => elegido.push(o.id)} />);

    fireEvent.click(screen.getByRole("button", { name: "Selecciona un organismo" }));
    const buscador = screen.getByRole("combobox", { name: "Buscar organismo de tránsito" });
    fireEvent.change(buscador, { target: { value: "nandu" } });
    expect(screen.getAllByRole("option")).toHaveLength(1);

    fireEvent.keyDown(buscador, { key: "Enter" });
    expect(elegido).toEqual(["o-3"]);
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("elegir con clic y mostrar el elegido; sin coincidencias avisa y Escape devuelve el foco", () => {
    const elegido: string[] = [];
    renderConQuery(<SelectorOrganismo organismos={ORGANISMOS} valor={ORGANISMOS[1]} onChange={(o) => elegido.push(o.id)} />);
    const disparador = screen.getByRole("button", { name: "Villa Dos" });

    fireEvent.click(disparador);
    expect(screen.getByRole("option", { name: "Villa Dos" })).toHaveAttribute("aria-selected", "true");
    fireEvent.click(screen.getByRole("button", { name: "Ciudad Uno" }));
    expect(elegido).toEqual(["o-1"]);

    fireEvent.click(disparador);
    const buscador = screen.getByRole("combobox");
    fireEvent.change(buscador, { target: { value: "qqq" } });
    expect(screen.getByText("No hay organismos que coincidan.")).toBeInTheDocument();
    fireEvent.keyDown(buscador, { key: "Escape" });

    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(disparador).toHaveFocus();
  });
});

describe("módulos puros", () => {
  it("filtrarOrganismos ignora mayúsculas y tildes", () => {
    expect(filtrarOrganismos(ORGANISMOS, " SUR ").map((o) => o.id)).toEqual(["o-3"]);
    expect(filtrarOrganismos(ORGANISMOS, "")).toHaveLength(3);
  });

  it("tiposDisponibles descarta los no disponibles", () => {
    expect(tiposDisponibles(TIPOS).map((t) => t.code)).toEqual(["TIPO_A", "TIPO_C"]);
  });
});

describe("guardas de la landing", () => {
  const RAIZ = path.resolve(__dirname, "..");
  const fuentes = ["components/landing", "lib/landing"].flatMap((d) =>
    readdirSync(path.join(RAIZ, d)).map((f) => ({ f: `${d}/${f}`, s: readFileSync(path.join(RAIZ, d, f), "utf8").replace(/^\s*\/\/.*$/gm, "") })),
  );

  it("no importa simuladores ni el repo de referencia ni usa almacenamiento o consola", () => {
    expect(fuentes.length).toBeGreaterThan(10);
    expect(fuentes.filter(({ s }) => /transfer-step-trail|-demo|localStorage|sessionStorage|console\./.test(s)).map(({ f }) => f)).toEqual([]);
  });

  it("no trae organismos ni tarifas escritos a mano", () => {
    const sospechosos = ["Medellín", "Envigado", "Bello", "Manizales", "Palmira", "Sabaneta", "Barbosa", "$ ", " COP"];
    expect(fuentes.filter(({ s }) => sospechosos.some((n) => s.includes(n))).map(({ f }) => f)).toEqual([]);
  });
});
