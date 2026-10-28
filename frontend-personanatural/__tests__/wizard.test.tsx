import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { useEffect, useState } from "react";
import { describe, expect, it } from "vitest";
import { useWizard, type PropsSeccion, type RegistroRenderers } from "@/components/tramite/ContextoWizard";
import { REGISTRO_RENDERERS } from "@/components/tramite/renderers/registro";
import { fijarContexto, obtenerContexto } from "@/lib/tramite/contexto";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { CONTEXTO, cuerpo, DIAGNOSTICO_OK, guardado, montar, nodo, wizardAbierto } from "./montar-wizard";
import { routerPush } from "./router";
import { json } from "./utilidades";


describe("estructura del wizard (F1)", () => {
  it("muestra cabecera, migas, título, barra lateral y los cinco pasos del portal con el primero activo", async () => {
    await wizardAbierto();

    expect(screen.getByAltText("FLIT 2.0")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Notificaciones" })).toBeInTheDocument();
    const migas = screen.getByRole("navigation", { name: "Ruta de navegación" });
    expect(migas).toHaveTextContent("Dashboard > Trámites > Trámite A > Diagnóstico");
    const lateral = screen.getByRole("complementary", { name: "Resumen y navegación del trámite" });
    expect(within(lateral).getByText("Mi trámite")).toBeInTheDocument();
    expect(within(lateral).getByTestId("radicado")).toHaveTextContent("PN-2026-0042");
    expect(within(lateral).getByText("ABC123")).toBeInTheDocument();
    expect(within(lateral).getByLabelText("Organismo de Tránsito")).toHaveValue("Ciudad Uno");
    expect(within(lateral).getByLabelText("Tipo de Trámite")).toHaveValue("Trámite A");
    expect(within(lateral).getByLabelText("Valor del trámite")).toBeInTheDocument();

    const pasos = within(screen.getByRole("list", { name: "Pasos del trámite" })).getAllByRole("button");
    expect(pasos.map((b) => b.textContent?.replace(/\s+/g, " ").trim())).toEqual(["1Paso 1 · Diagnóstico", "2Paso 2 · Actores", "3Paso 3 · Requisitos", "4Paso 4 · Pago", "5Paso 5 · Envío"]);
    expect(nodo(1)).toHaveAttribute("aria-current", "step");
    for (const n of [2, 3, 4, 5]) expect(nodo(n)).toBeDisabled();
  });

  it("lee el borrador, luego la definición con el organismo del borrador, y no pide nada más sin acción del ciudadano", async () => {
    const { llamadas } = await wizardAbierto();

    expect(llamadas.map((l) => l.clave)).toEqual(["GET /tipos-tramite", "GET /solicitudes/s-1", "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1"]);
  });

  it("monta los cinco pasos como un carrusel: los ocultos no se alcanzan con Tab ni los lee el lector de pantalla", async () => {
    await wizardAbierto();

    const slides = document.querySelectorAll(".wizard-slide");
    expect(slides).toHaveLength(5);
    expect(slides[0]).not.toHaveAttribute("inert");
    expect(slides[0]).toHaveAttribute("aria-hidden", "false");
    for (const s of Array.from(slides).slice(1)) {
      expect(s).toHaveAttribute("inert");
      expect(s).toHaveAttribute("aria-hidden", "true");
    }
    expect(document.querySelector(".wizard-slider")).toHaveClass("overflow-x-hidden");
  });

  it("un tipo que no está en el catálogo dice que no está disponible, sin abrir sesión ni borrador", async () => {
    fijarContexto({ ...CONTEXTO, tipoCodigo: "TIPO_X" });
    const { llamadas } = montar({}, { tipo: "TIPO_X" });

    expect(await screen.findByRole("heading", { level: 1, name: "Este trámite aún no está disponible en el portal" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Volver al Dashboard" })).toHaveAttribute("href", "/");
    expect(llamadas.map((l) => l.clave)).toEqual(["GET /tipos-tramite"]);
  });

  it("un tipo del catálogo con un section_type que el portal no pinta se muestra como no disponible entero", async () => {
    fijarContexto(CONTEXTO);
    montar({ "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1": () => json(definicionDe([["vehicle_query"], ["signature_fur"]])) });

    expect(await screen.findByRole("heading", { level: 1, name: "Este trámite aún no está disponible en el portal" })).toBeInTheDocument();
    expect(screen.queryByRole("complementary")).not.toBeInTheDocument();
  });

  it("mientras carga muestra el MobilityLoader y si falla ofrece reintentar", async () => {
    fijarContexto(CONTEXTO);
    let intentos = 0;
    montar({ "GET /solicitudes/s-1": () => (++intentos === 1 ? json({ code: "boom" }, 500) : json(solicitudDe())) });

    expect(screen.getByRole("status")).toHaveTextContent("Cargando...");
    expect(await screen.findByText("No pudimos cargar el trámite. Intenta de nuevo.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Trámite de Trámite A" })).toBeInTheDocument();
  });
});

describe("sin sesión: reanudar", () => {
  it("sin contexto (se recargó la página) pide radicado y cédula, y con la sesión reanudada abre el trámite", async () => {
    const { llamadas } = montar({
      "POST /sesiones/reanudar": () => json({ solicitudId: "s-1", codigoPublico: "PN-2026-0042", estado: "borrador", tipoTramiteCodigo: "TIPO_A", expiraEn: "2026-10-28T22:00:00Z" }),
    });

    expect(await screen.findByRole("heading", { level: 1, name: "Reanuda tu trámite" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Número de radicado"), { target: { value: "pn-2026-0042" } });
    fireEvent.change(screen.getByLabelText("Cédula del solicitante"), { target: { value: "1020456789" } });
    fireEvent.click(screen.getByLabelText("Verificación de seguridad"));
    fireEvent.click(screen.getByRole("button", { name: "Reanudar" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Trámite de Trámite A" })).toBeInTheDocument();
    expect(cuerpo(llamadas, "POST /sesiones/reanudar")).toEqual({ radicado: "PN-2026-0042", cedula: "1020456789" });
    expect(llamadas.find((l) => l.clave === "POST /sesiones/reanudar")?.init.headers).toMatchObject({ "x-captcha-token": "dev-captcha-token" });
    expect(obtenerContexto()).toMatchObject({ solicitudId: "s-1", radicado: "PN-2026-0042", cedula: "1020456789" });
  });

  it("al venir de «Consultar mi trámite» (subsanar) entra con el radicado y la cédula ya escritos", async () => {
    fijarContexto({ intencion: "subsanar", tipoCodigo: "TIPO_A", radicado: "PN-2026-0007", cedula: "1020456789" });
    montar();

    expect(await screen.findByLabelText("Número de radicado")).toHaveValue("PN-2026-0007");
    expect(screen.getByLabelText("Cédula del solicitante")).toHaveValue("1020456789");
  });

  it("si la sesión venció al leer el borrador dice «Tu sesión venció» y deja reanudar", async () => {
    fijarContexto(CONTEXTO);
    montar({ "GET /solicitudes/s-1": () => json({ code: "session_expired", title: "Sesión vencida" }, 401) });

    expect(await screen.findByRole("heading", { level: 1, name: "Tu sesión venció" })).toBeInTheDocument();
    expect(screen.getByLabelText("Número de radicado")).toHaveValue("PN-2026-0042");
  });

  it("el radicado manda: si abre un trámite de otro tipo, lleva a la ruta de ese tipo", async () => {
    montar({
      "POST /sesiones/reanudar": () => json({ solicitudId: "s-9", codigoPublico: "PN-2026-0009", estado: "borrador", tipoTramiteCodigo: "TIPO_C", expiraEn: "2026-10-28T22:00:00Z" }),
    });
    await screen.findByLabelText("Número de radicado");
    fireEvent.change(screen.getByLabelText("Número de radicado"), { target: { value: "PN-2026-0009" } });
    fireEvent.change(screen.getByLabelText("Cédula del solicitante"), { target: { value: "1020456789" } });
    fireEvent.click(screen.getByLabelText("Verificación de seguridad"));
    fireEvent.click(screen.getByRole("button", { name: "Reanudar" }));

    await waitFor(() => expect(routerPush).toHaveBeenCalledWith("/tramite/TIPO_C"));
  });
});

describe("avance bloqueado, guardado y conflictos", () => {
  it("los pasos que el borrador no alcanzó no se pueden abrir desde el navegador de pasos", async () => {
    await wizardAbierto({ "GET /solicitudes/s-1": () => json(guardado(DIAGNOSTICO_OK)) });

    expect(nodo(3)).toBeDisabled();
    expect(nodo(5)).toBeDisabled();
  });

  it("un paso cuya sección aún no está construida avisa con un estado accesible y no deja continuar", async () => {
    await wizardAbierto({ "GET /solicitudes/s-1": () => json(guardado(DIAGNOSTICO_OK)) });

    const slide = document.querySelectorAll(".wizard-slide")[1] as HTMLElement;
    expect(within(slide).getByRole("status")).toHaveTextContent("Esta sección aún no está disponible en el portal");
    expect(screen.getByRole("button", { name: "Continuar" })).toBeDisabled();
  });

  /** Renderer de prueba: una sección con un campo obligatorio y un guardado que usa la versión. */
  function CampoPrueba({ intentoAvance, onEstado }: PropsSeccion) {
    const { guardarConVersion, registrarGuardado, cliente, solicitud, reportarError } = useWizard();
    const [valor, setValor] = useState("");
    useEffect(() => onEstado({ completo: valor !== "" }), [valor, onEstado]);
    useEffect(() => {
      registrarGuardado("p1:vehicle_query", () =>
        guardarConVersion((version) => cliente.solicitudes.guardarCampos(solicitud.solicitudId, { version, valores: { color: valor } })).catch((e) => {
          reportarError(e);
          throw e;
        }),
      );
      return () => registrarGuardado("p1:vehicle_query", null);
    }, [valor, guardarConVersion, registrarGuardado, cliente, solicitud.solicitudId, reportarError]);
    return (
      <label>
        Color
        <input aria-invalid={intentoAvance > 0 && valor === "" ? "true" : undefined} value={valor} onChange={(e) => setValor(e.target.value)} />
      </label>
    );
  }
  const REGISTRO_PRUEBA: RegistroRenderers = { ...REGISTRO_RENDERERS, vehicle_query: CampoPrueba };

  it("al intentar avanzar con obligatorios sin completar no avanza, marca el campo y manda el foco al primer inválido", async () => {
    await wizardAbierto({}, { registro: REGISTRO_PRUEBA });

    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    expect(screen.getByLabelText("Color")).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(screen.getByLabelText("Color")).toHaveFocus());
    expect(nodo(1)).toHaveAttribute("aria-current", "step");
  });

  it("guarda el paso con la versión del borrador antes de avanzar y avanza con la respuesta", async () => {
    const guardados: unknown[] = [];
    await wizardAbierto(
      {
        "PUT /solicitudes/s-1/campos": (init) => {
          guardados.push(JSON.parse(String(init.body)));
          return json(solicitudDe({ version: 4 }));
        },
      },
      { registro: REGISTRO_PRUEBA },
    );

    fireEvent.change(screen.getByLabelText("Color"), { target: { value: "AZUL" } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    await waitFor(() => expect(nodo(2)).toHaveAttribute("aria-current", "step"));
    expect(guardados).toEqual([{ version: 3, valores: { color: "AZUL" } }]);
  });

  it("ante un 409 por versión avisa, no avanza y «Recargar» vuelve a leer el borrador", async () => {
    let lecturas = 0;
    await wizardAbierto(
      {
        "GET /solicitudes/s-1": () => (++lecturas, json(solicitudDe({ version: lecturas === 1 ? 3 : 5 }))),
        "PUT /solicitudes/s-1/campos": () => json({ code: "conflicto_version", title: "Cambió mientras lo editaba" }, 409),
      },
      { registro: REGISTRO_PRUEBA },
    );

    fireEvent.change(screen.getByLabelText("Color"), { target: { value: "AZUL" } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    expect(await screen.findByText(/Este trámite cambió mientras lo editabas/)).toBeInTheDocument();
    expect(nodo(1)).toHaveAttribute("aria-current", "step");
    fireEvent.click(screen.getByRole("button", { name: "Recargar" }));
    await waitFor(() => expect(screen.queryByText(/Este trámite cambió mientras lo editabas/)).not.toBeInTheDocument());
    expect(lecturas).toBe(2);
  });

  it("si la sesión vence al guardar, avisa «Tu sesión venció» y deja reanudar", async () => {
    await wizardAbierto({ "PUT /solicitudes/s-1/campos": () => json({ code: "session_expired", title: "Sesión vencida" }, 401) }, { registro: REGISTRO_PRUEBA });

    fireEvent.change(screen.getByLabelText("Color"), { target: { value: "AZUL" } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    const dialogo = await screen.findByRole("dialog", { name: "Tu sesión venció" });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Reanudar mi trámite" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Reanuda tu trámite" })).toBeInTheDocument();
    expect(screen.getByLabelText("Número de radicado")).toHaveValue("PN-2026-0042");
    expect(obtenerContexto()?.solicitudId).toBeUndefined();
  });

  it("«Atrás» en el paso 1 pide confirmar y solo entonces vuelve al inicio, conservando el borrador", async () => {
    const { llamadas } = await wizardAbierto();

    fireEvent.click(screen.getByRole("button", { name: "Atrás" }));
    const dialogo = await screen.findByRole("dialog", { name: "Salir del trámite" });
    expect(routerPush).not.toHaveBeenCalled();
    fireEvent.click(within(dialogo).getByRole("button", { name: "Seguir aquí" }));
    expect(routerPush).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Atrás" }));
    fireEvent.click(within(await screen.findByRole("dialog", { name: "Salir del trámite" })).getByRole("button", { name: "Salir" }));

    expect(routerPush).toHaveBeenCalledWith("/");
    expect(llamadas.some((l) => l.init.method === "DELETE")).toBe(false);
  });

  it("un gesto de deslizar tampoco avanza con el paso incompleto", async () => {
    await wizardAbierto();
    const slider = document.querySelector(".wizard-slider") as HTMLElement;

    fireEvent.touchStart(slider, { touches: [{ clientX: 300 }] });
    fireEvent.touchEnd(slider, { changedTouches: [{ clientX: 100 }] });

    expect(nodo(1)).toHaveAttribute("aria-current", "step");
  });
});

describe("valor del trámite", () => {
  it("se calcula a pedido con la cotización vigente del organismo para el tipo", async () => {
    const { llamadas } = await wizardAbierto({
      "POST /cotizaciones": () => json({ lineas: [], totalCop: 285400, versionTarifa: "v1", adicionalesNoAplicados: [] }),
    });
    const lateral = screen.getByRole("complementary", { name: "Resumen y navegación del trámite" });

    fireEvent.click(within(lateral).getByRole("button", { name: "Calcular valor" }));
    fireEvent.click(within(lateral).getByLabelText("Verificación de seguridad"));

    await waitFor(() => expect(within(lateral).getByLabelText("Valor del trámite")).toHaveValue("$ 285.400 COP"));
    expect(cuerpo(llamadas, "POST /cotizaciones")).toEqual({ organismoId: "o-1", tipoCodigo: "TIPO_A" });
  });

  it("sin tarifa vigente avisa y no deja continuar", async () => {
    await wizardAbierto({
      "POST /cotizaciones": () => json({ code: "tarifa_no_vigente", title: "Sin tarifa" }, 422),
      "GET /solicitudes/s-1": () => json(guardado(DIAGNOSTICO_OK)),
    });
    const lateral = screen.getByRole("complementary", { name: "Resumen y navegación del trámite" });

    fireEvent.click(within(lateral).getByRole("button", { name: "Calcular valor" }));
    fireEvent.click(within(lateral).getByLabelText("Verificación de seguridad"));

    expect(await within(lateral).findByText("Este organismo no tiene tarifa para este trámite")).toBeInTheDocument();
    fireEvent.click(nodo(1));
    expect(await screen.findByRole("button", { name: "Continuar" })).toBeDisabled();
  });
});

describe("Habeas Data en el wizard", () => {
  it("la cédula y la placa no aparecen en la URL ni se guardan en el almacenamiento del navegador", async () => {
    await wizardAbierto();

    expect(window.location.href).not.toMatch(/1020456789|ABC123/);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});

