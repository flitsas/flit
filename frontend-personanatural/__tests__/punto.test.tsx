import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ProveedorMarca } from "@/components/marca/MarcaPunto";
import { CanalPunto } from "@/components/punto/CanalPunto";
import type { MarcaAplicable } from "@/lib/marca/marca";
import { crearCliente } from "@/lib/api/cliente";
import { json, renderConQuery } from "./utilidades";

type Manejador = (init: RequestInit) => Response | Promise<Response>;

const ENTRADA = { organismoId: "o-1", codigo: "BOG", nombre: "Movilidad de Bogotá" };

// Tipos con tarifa vigente en el organismo de la sesión del punto (GET /punto/tipos-tramite): la lista la define la plataforma, no el portal.
const TIPOS_PUNTO = [
  { code: "TRASPASO_STANDARD", name: "Traspaso", family: "TRASPASO", version: 1, disponible: true },
  { code: "CAMBIO_COLOR", name: "Cambio de color", family: "OTROS", version: 1, disponible: true },
  { code: "TRASLADO_CUENTA", name: "Traslado de cuenta", family: "OTROS", version: 1, disponible: true },
  { code: "CANCELACION_MATRICULA", name: "Cancelación de matrícula", family: "OTROS", version: 2, disponible: true },
  { code: "SIN_TARIFA", name: "Trámite sin tarifa", family: "OTROS", version: 1, disponible: false, motivoNoDisponible: "sin tarifa vigente" },
];

const MENSAJE_PLACA = "No encontramos un vehículo con esa placa.";
const MENSAJE_ORGANISMO = "Este trámite se hace en el organismo que tiene el vehículo.";
const MENSAJE_RUNT = "No pudimos consultar el RUNT. Inténtalo de nuevo.";
const MENSAJE_REVISANDO = "Revisando el vehículo…";

function montar(rutas: Record<string, Manejador> = {}, marca: MarcaAplicable | null = null) {
  const todas: Record<string, Manejador> = {
    "POST /punto/entrada": () => json(ENTRADA),
    "GET /punto/tipos-tramite": () => json(TIPOS_PUNTO),
    "GET /tipos-tramite": () => json(TIPOS_PUNTO),
    "GET /tipos-tramite?organismoId=o-1": () => json(TIPOS_PUNTO),
    ...rutas,
  };
  const llamadas: { clave: string; init: RequestInit }[] = [];
  const fetchImpl = (async (url: string, init: RequestInit = {}) => {
    const clave = `${init.method ?? "GET"} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push({ clave, init });
    return (todas[clave] ?? (() => json({ code: "no_simulado", message: clave }, 404)))(init);
  }) as unknown as typeof fetch;
  renderConQuery(
    <ProveedorMarca marca={marca}>
      <CanalPunto codigo="BOG" cliente={crearCliente({ fetchImpl })} />
    </ProveedorMarca>,
  );
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
  fireEvent.change(screen.getByLabelText("Placa del vehículo"), { target: { value: placa } });
  fireEvent.change(screen.getByLabelText("Número de Cédula"), { target: { value: documento } });
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
    expect(screen.queryByRole("button", { name: "Cotizar mi trámite" })).not.toBeInTheDocument();
  });

  it("la entrada replica la web: cédula antes que placa, pasos, Somos, valor de los trámites, preguntas y navegación completa", async () => {
    montar();
    await screen.findByRole("heading", { name: "Iniciar mi trámite" });

    const cedula = screen.getByLabelText("Número de Cédula");
    const placa = screen.getByLabelText("Placa del vehículo");
    expect(cedula.compareDocumentPosition(placa) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.getByText(/en Movilidad de Bogotá/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "4 simples pasos para tu trámite" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Preguntas Frecuentes" })).toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "Secciones" });
    expect(within(nav).getAllByRole("link").map((l) => l.textContent)).toEqual(["Trámites", "Nosotros", "Servicios", "FAQ"]);
    // «Somos» y el pie llevan el nombre del organismo, no el de FLIT; no hay lista de organismos.
    expect(screen.getByRole("region", { name: "Somos Movilidad de Bogotá" })).toBeInTheDocument();
    expect(screen.getByRole("contentinfo")).toHaveTextContent("© 2026 Movilidad de Bogotá");
    expect(screen.queryByRole("region", { name: /Organismos de tránsito integrados/ })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Servicios Adicionales" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Otros Servicios Vehiculares" })).toBeInTheDocument();
  });

  it("con carrusel del OT reemplaza «Servicios Adicionales»; sin él, quedan los servicios de siempre", async () => {
    const carrusel = [{ imagenUrl: "/api/v1/persona-natural/marca/imagenes/a1", alt: "Curso de infractores", enlace: null }];
    montar({}, { nombre: "Movilidad de Bogotá", colores: { primario: "#001682", secundario: "#CC4E0A", sobrePrimario: "#FFFFFF" }, logoUrl: null, bannerUrl: null, version: 1, carrusel });
    expect(await screen.findByRole("region", { name: "Imágenes destacadas del organismo" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Servicios Adicionales" })).not.toBeInTheDocument();
  });

  it("«Consulta el valor de tu trámite» lista los trámites del OT, filtra y «Ver valor» abre Cotizar con ese trámite", async () => {
    montar();
    const columna = within(await screen.findByRole("region", { name: "Consulta el valor de tu trámite" }));
    expect(await columna.findByRole("button", { name: "Ver el valor de Traspaso" })).toBeInTheDocument();

    fireEvent.change(columna.getByLabelText("Buscar trámite"), { target: { value: "zzz" } });
    expect(columna.getByText("No hay trámites que coincidan.")).toBeInTheDocument();
    fireEvent.change(columna.getByLabelText("Buscar trámite"), { target: { value: "trasp" } });

    fireEvent.click(columna.getByRole("button", { name: "Ver el valor de Traspaso" }));
    const dialogo = within(await screen.findByRole("dialog"));
    expect(dialogo.getByTestId("organismo-fijo")).toHaveTextContent("Movilidad de Bogotá");
    expect(await dialogo.findByRole("button", { name: "Tipo de trámite" })).toHaveTextContent("Traspaso");
  });

  it("Cotizar abre con el organismo del punto fijo (sin selector); la entrada trae banners y el asistente", async () => {
    montar();
    expect(await screen.findByRole("region", { name: "Comunicados" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Dr. FLIT|asistente/i })).toBeInTheDocument();

    fireEvent.click(await screen.findByRole("button", { name: "Cotizar mi trámite" }));
    const dialogo = within(await screen.findByRole("dialog"));
    expect(dialogo.getByTestId("organismo-fijo")).toHaveTextContent("Movilidad de Bogotá");
    expect(dialogo.queryByRole("button", { name: /Organismo de tránsito/ })).not.toBeInTheDocument();
    expect(dialogo.getByRole("button", { name: "Tipo de trámite" })).not.toHaveTextContent("Primero elige un organismo");
  });

  it("al entrar muestra Iniciar, Consultar y Cotizar, sin contraseña", async () => {
    montar();

    expect(await screen.findByRole("heading", { name: "Movilidad de Bogotá" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Iniciar mi trámite" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Consultar mi trámite" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cotizar mi trámite" })).toBeInTheDocument();
    expect(screen.queryByLabelText(/contraseña/i)).not.toBeInTheDocument();
    expect(document.querySelector('input[type="password"]')).toBeNull();
    expect(screen.getByRole("img", { name: "FLIT 2.0" })).toBeInTheDocument();

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

    fireEvent.change(screen.getByLabelText("Placa del vehículo"), { target: { value: "ABC123" } });
    expect(boton).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Número de Cédula"), { target: { value: "1020456789" } });
    expect(boton).toBeDisabled();

    await elegirTipo("Traspaso");
    expect(boton).toBeEnabled();
  });

  it("el selector lista los tipos configurados para el organismo del punto, sin lista fija", async () => {
    const { llamadas } = montar();
    await screen.findByRole("button", { name: "Iniciar trámite" });
    const selector = screen.getByRole("button", { name: "Tipo de Trámite" });
    await waitFor(() => expect(selector).toBeEnabled());
    fireEvent.click(selector);

    const lista = await screen.findByRole("listbox", { name: "Tipo de Trámite" });
    // Solo los disponibles (con tarifa vigente); incluye tipos que antes no estaban en la lista fija.
    expect(within(lista).getAllByRole("option").map((o) => o.textContent)).toEqual(["Traspaso", "Cambio de color", "Traslado de cuenta", "Cancelación de matrícula"]);
    expect(llamadas.map((l) => l.clave)).toContain("GET /punto/tipos-tramite");
  });

  it("un tipo del catálogo que no estaba en la lista fija se puede iniciar y llega al arranque con su código", async () => {
    const { llamadas } = montar({ "POST /punto/arranque": () => json({ puedeSeguir: false, mensaje: "x" }) });
    await screen.findByRole("button", { name: "Iniciar trámite" });
    await llenar({ tipoNombre: "Traslado de cuenta" });
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));
    await waitFor(() => expect(llamadas.some((l) => l.clave === "POST /punto/arranque")).toBe(true));
    expect(cuerpo(llamadas, "POST /punto/arranque").tipoCodigo).toBe("TRASLADO_CUENTA");
  });

  it("mientras carga los tipos lo dice; si fallan avisa con role=alert y reintenta; sin tipos lo explica", async () => {
    let estado: "carga" | "falla" | "vacio" | "ok" = "carga";
    let liberar: (r: Response) => void = () => undefined;
    montar({
      "GET /punto/tipos-tramite": () => {
        if (estado === "carga") return new Promise<Response>((r) => (liberar = r));
        if (estado === "falla") return json({ code: "x" }, 503);
        return json(estado === "vacio" ? [] : TIPOS_PUNTO);
      },
    });
    await screen.findByRole("button", { name: "Iniciar trámite" });
    expect(screen.getByRole("button", { name: "Tipo de Trámite" })).toHaveTextContent("Cargando...");
    expect(screen.getByRole("button", { name: "Tipo de Trámite" })).toBeDisabled();
    estado = "falla";
    liberar(json({ code: "x" }, 503));
    expect(await screen.findByRole("alert")).toBeInTheDocument();

    estado = "vacio";
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Por ahora no hay trámites disponibles en este punto.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tipo de Trámite" })).toBeDisabled();
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
    expect(screen.queryByRole("heading", { name: "Puedes continuar." })).not.toBeInTheDocument();
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

  it("si puede seguir, abre el cascarón del wizard y Atrás permite consultar otra vez", async () => {
    const { llamadas } = montar({
      "POST /punto/arranque": () => json({ puedeSeguir: true }),
      "POST /punto/diagnostico": () =>
        json({ puedeContinuar: true, requisitos: [{ codigo: "soat", descripcion: "SOAT vigente", estado: "cumple" }] }),
      "POST /punto/consultas/vehiculo": () =>
        json({ existe: true, placa: "ABC123", marca: "MAZDA", linea: "3", modeloAno: "2020", estado: "ACTIVO" }),
    });
    await screen.findByRole("button", { name: "Iniciar trámite" });
    await llenar();
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));

    expect(await screen.findByRole("heading", { name: "Puedes continuar." })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Ruta de navegación" })).toHaveTextContent("Diagnóstico");
    expect(screen.getByRole("list", { name: "Pasos del trámite" })).toHaveTextContent("Paso 2 · Actores");
    expect(screen.getByRole("heading", { name: "Diagnóstico de requisitos previos" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Datos técnicos del vehículo" })).toBeInTheDocument();
    expect(screen.getByText("Atención:")).toBeInTheDocument();
    expect(cuerpo(llamadas, "POST /punto/diagnostico")).toEqual({
      placa: "ABC123",
      documento: "1020456789",
      tipoCodigo: "TRASPASO_STANDARD",
      tipoDocumento: "CC",
    });
    expect(screen.getByRole("button", { name: "Continuar" })).toBeEnabled();

    fireEvent.click(screen.getByRole("button", { name: "Atrás" }));
    expect(screen.getByLabelText("Placa del vehículo")).toHaveValue("ABC123");
    expect(screen.getByLabelText("Número de Cédula")).toHaveValue("1020456789");

    fireEvent.change(screen.getByLabelText("Placa del vehículo"), { target: { value: "XYZ987" } });
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));
    await waitFor(() => expect(llamadas.filter((l) => l.clave === "POST /punto/arranque")).toHaveLength(2));
    expect(cuerpo(llamadas, "POST /punto/arranque")).toMatchObject({ placa: "XYZ987", documento: "1020456789" });
  });

  it("con bloqueo muestra No puedes continuar y deshabilita Continuar (como el portal)", async () => {
    montar({
      "POST /punto/arranque": () => json({ puedeSeguir: true }),
      "POST /punto/diagnostico": () =>
        json({ puedeContinuar: false, requisitos: [{ codigo: "soat", descripcion: "SOAT vencido", estado: "falta" }] }),
      "POST /punto/consultas/vehiculo": () => json({ existe: true, placa: "ABC123" }),
    });
    await screen.findByRole("button", { name: "Iniciar trámite" });
    await llenar();
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));

    expect(await screen.findByRole("heading", { name: "No puedes continuar" })).toBeInTheDocument();
    expect(screen.getByLabelText("Motivos del diagnóstico")).toHaveTextContent("SOAT vencido");
    expect(screen.getByRole("button", { name: "Continuar" })).toBeDisabled();
  });

  it("mientras diagnostica muestra Revisando el vehículo…", async () => {
    const colgado = () => new Promise<Response>(() => {});
    montar({
      "POST /punto/arranque": () => json({ puedeSeguir: true }),
      "POST /punto/diagnostico": colgado,
      "POST /punto/consultas/vehiculo": colgado,
    });
    await screen.findByRole("button", { name: "Iniciar trámite" });
    await llenar();
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));
    const frases = await screen.findAllByText(MENSAJE_REVISANDO);
    expect(frases.some((n) => n.closest('[role="status"]')?.querySelector(".mobility-orbit"))).toBe(true);
  });

  it("si el diagnóstico falla, AvisoModal y Reintentar vuelve a consultar", async () => {
    let intentos = 0;
    const { llamadas } = montar({
      "POST /punto/arranque": () => json({ puedeSeguir: true }),
      "POST /punto/diagnostico": () => {
        intentos += 1;
        if (intentos === 1) return json({ code: "proveedor_no_disponible", message: "No pudimos armar el diagnóstico. Inténtalo de nuevo." }, 503);
        return json({ puedeContinuar: true, requisitos: [] });
      },
    });
    await screen.findByRole("button", { name: "Iniciar trámite" });
    await llenar();
    fireEvent.click(screen.getByRole("button", { name: "Iniciar trámite" }));

    const dialogo = await screen.findByRole("dialog", { name: "No pudimos continuar" });
    // mensajeDeFallo mapea proveedor_no_disponible al texto de RUNT del portal.
    expect(within(dialogo).getByRole("alert")).toHaveTextContent("No pudimos consultar el RUNT");
    fireEvent.click(within(dialogo).getAllByRole("button", { name: "Cerrar" }).at(-1)!);
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("heading", { name: "Puedes continuar." })).toBeInTheDocument();
    expect(llamadas.filter((l) => l.clave === "POST /punto/diagnostico")).toHaveLength(2);
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
