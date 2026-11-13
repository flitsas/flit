import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { FECHA, ID_EXPRESS, creadoOk, cuerpoDe, llamadas, montarNuevo, type Navegacion } from "./datos";

// Uso de ejemplo: /diagnosticos/nuevo?modo=express&placa=TST123 abre el Express (HU #14085) con la placa precargada a la
// izquierda y los parámetros a la derecha; «Continuar» valida, estima si la operación lo pide y lanza POST /express, que
// lleva al tablero /diagnosticos/{id}.

const nav = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn(), params: new URLSearchParams() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/diagnosticos/nuevo",
  useSearchParams: () => nav.params,
  useRouter: () => nav,
}));

beforeEach(() => {
  nav.push.mockReset();
  nav.replace.mockReset();
  // Solo el reloj de Date (el nombre autogenerado ya no lleva el día desde HU #14087); los temporizadores siguen siendo reales.
  vi.useFakeTimers({ toFake: ["Date"], shouldAdvanceTime: true });
  vi.setSystemTime(FECHA);
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

const navegacion = nav as unknown as Navegacion;
type Opciones = Parameters<typeof montarNuevo>[1];
const montar = (opciones: Opciones = {}) => montarNuevo(navegacion, { query: "modo=express", ...opciones });

// El primer montaje del archivo importa la página en frío: con la suite en paralelo puede pasar de 1 s.
const nombre = () => screen.findByRole("textbox", { name: "Nombre del diagnóstico" }, { timeout: 5000 });
/** La placa con la configuración ya cargada (mientras llega, el campo se ve deshabilitado). */
const placa = async () => {
  await nombre();
  return screen.getByRole("textbox", { name: "Placa o VIN" });
};
const documento = () => screen.getByRole("textbox", { name: /número de documento del locatario/i });
const tipoDocumento = () => screen.getByRole("combobox", { name: /tipo de documento del locatario/i });
const iniciado = () => screen.findByRole("heading", { name: "Diagnóstico iniciado con 1 vehículo" });
/** HU #14562: «Continuar» cuando la operación muestra el estimado antes de lanzar; si no, «Iniciar diagnóstico». */
const PRINCIPAL = /^(Continuar|Iniciar diagnóstico)$/;
const continuar = async () => fireEvent.click(await screen.findByRole("button", { name: PRINCIPAL }));

describe("Express — placa y nombre autogenerado (AC2)", () => {
  it("con ?placa= precarga la placa normalizada y el nombre «Express TST123»", async () => {
    montar({ query: "modo=express&placa=tst 123" });
    expect(await placa()).toHaveValue("TST123");
    expect(await nombre()).toHaveValue("Express TST123");
  });

  it("el nombre sigue a la placa mientras no se edite y, editado, se conserva y viaja", async () => {
    const api = montar();
    fireEvent.change(await placa(), { target: { value: "tst124" } });
    expect(await nombre()).toHaveValue("Express TST124");
    fireEvent.change(await nombre(), { target: { value: "Revisión flota" } });
    fireEvent.change(await placa(), { target: { value: "TST125" } });
    expect(await nombre()).toHaveValue("Revisión flota");
    await continuar();
    await waitFor(() => expect(llamadas(api, "POST", "/express")).toHaveLength(1));
    expect(cuerpoDe(llamadas(api, "POST", "/express")[0])).toMatchObject({ nombre: "Revisión flota", identificador: "TST125" });
  });

  it("placa vacía: pide la placa o el VIN en su campo y no llama a la API", async () => {
    const api = montar();
    await nombre();
    await continuar();
    expect(await placa()).toHaveAttribute("aria-invalid", "true");
    expect(await placa()).toHaveAccessibleDescription(/escribe la placa o el vin/i);
    expect(await placa()).toHaveFocus();
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });

  it("ni placa ni VIN: muestra el error en el campo y no crea nada", async () => {
    const api = montar();
    fireEvent.change(await placa(), { target: { value: "12" } });
    await continuar();
    expect(await placa()).toHaveAccessibleDescription(/no es una placa ni un vin/i);
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });

  it("placa de más de 8 caracteres: EXCEDE_LONGITUD, sin recortar", async () => {
    montar();
    fireEvent.change(await placa(), { target: { value: "ts 1234 5678" } });
    expect(await placa()).toHaveValue("TS12345678");
    await continuar();
    expect(await placa()).toHaveAccessibleDescription(/la placa tiene 10 caracteres y el máximo es 8/i);
  });

  it("con el nombre borrado no lanza y muestra el error en el campo", async () => {
    const api = montar({ query: "modo=express&placa=TST123" });
    fireEvent.change(await nombre(), { target: { value: "" } });
    await continuar();
    expect(await nombre()).toHaveAttribute("aria-invalid", "true");
    expect(await nombre()).toHaveAccessibleDescription(/escribe el nombre del diagnóstico/i);
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });

  it("documento del locatario sin tipo: pide el tipo y no lanza", async () => {
    const api = montar({ query: "modo=express&placa=TST123" });
    await nombre();
    fireEvent.change(documento(), { target: { value: "12345678" } });
    await continuar();
    expect(await screen.findByText(/elige el tipo de documento del locatario/i)).toBeInTheDocument();
    expect(tipoDocumento()).toHaveAttribute("aria-invalid", "true");
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });

  it("el documento es opcional, lleva «Declarado por el cliente», se enmascara y viaja completo sin quedar en el navegador", async () => {
    const log = vi.spyOn(console, "log");
    const api = montar({ query: "modo=express&placa=TST123" });
    await nombre();
    expect(documento()).not.toBeRequired();
    expect(screen.getByText(/declarado por el cliente/i)).toBeInTheDocument();
    fireEvent.change(tipoDocumento(), { target: { value: "CC" } });
    fireEvent.focus(documento());
    fireEvent.change(documento(), { target: { value: "12345678" } });
    fireEvent.blur(documento());
    expect(documento()).toHaveValue("****5678");
    await continuar();
    await waitFor(() => expect(llamadas(api, "POST", "/express")).toHaveLength(1));
    expect(cuerpoDe(llamadas(api, "POST", "/express")[0])).toMatchObject({ tipo_documento_locatario: "CC", documento_locatario: "12345678" });
    expect(JSON.stringify({ ...localStorage }) + JSON.stringify({ ...sessionStorage })).not.toContain("12345678");
    expect(log.mock.calls.flat().join(" ")).not.toContain("12345678");
    log.mockRestore();
  });

  it("sin ANTES_DE_LANZAR lanza directo con la cabecera y el vehículo y navega al tablero", async () => {
    const api = montar({ query: "modo=express&placa=TST123" });
    await nombre();
    fireEvent.click(screen.getByRole("checkbox", { name: "SIMIT vehículo" }));
    fireEvent.click(screen.getByRole("radio", { name: "Con Análisis IA" }));
    fireEvent.change(await screen.findByRole("combobox", { name: /tipo de trámite/i }), { target: { value: "TRASPASO_STANDARD" } });
    await continuar();
    expect(await iniciado()).toBeInTheDocument();
    expect(llamadas(api, "POST", "/express/estimado")).toHaveLength(0);
    expect(cuerpoDe(llamadas(api, "POST", "/express")[0])).toEqual({
      nombre: "Express TST123",
      modo: "CON_ANALISIS",
      tipo_tramite_codigo: "TRASPASO_STANDARD",
      alcance: { por_vehiculo: ["RUNT", "SIMIT_VEHICULO"], por_propietario: [], por_locatario: [] },
      identificador: "TST123",
      tipo_documento_locatario: null,
      documento_locatario: null,
    });
  });

  it("con ANTES_DE_LANZAR muestra el estimado en Parámetros («al menos N») y lanza solo al confirmar", async () => {
    const api = montar({
      query: "modo=express&placa=TST123",
      estimado: "ANTES_DE_LANZAR",
      rutas: {
        "POST /express/estimado": () =>
          Response.json({
            mostrar_estimado: true,
            estimado_consultas: [
              { fuente: "SIMIT_VEHICULO", consultas: 1, minimo: false },
              { fuente: "RUNT_PROPIETARIO", consultas: 1, minimo: true },
            ],
          }),
      },
    });
    await nombre();
    await continuar();
    const estimado = await screen.findByRole("region", { name: "Estimado de consultas" });
    expect(screen.getByRole("region", { name: "Parámetros" })).toContainElement(estimado);
    expect(estimado).toHaveTextContent("SIMIT vehículo");
    expect(within(estimado).getByText("RUNT propietario").nextSibling).toHaveTextContent(/^al menos 1$/);
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: "Lanzar diagnóstico" }));
    expect(await iniciado()).toBeInTheDocument();
  });

  it("«Anterior» en el estimado vuelve a editar sin lanzar", async () => {
    const api = montar({
      query: "modo=express&placa=TST123",
      estimado: "ANTES_DE_LANZAR",
      rutas: { "POST /express/estimado": () => Response.json({ mostrar_estimado: true, estimado_consultas: [{ fuente: "RUNT", consultas: 1 }] }) },
    });
    await nombre();
    await continuar();
    fireEvent.click(await screen.findByRole("button", { name: "Anterior" }));
    expect(screen.queryByRole("region", { name: "Estimado de consultas" })).not.toBeInTheDocument();
    expect(await nombre()).toBeEnabled();
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });
});

describe("Express — errores (AC4)", () => {
  it("el 422 del identificador se pinta en el campo Placa o VIN, conserva lo digitado y no navega", async () => {
    montar({
      query: "modo=express&placa=TST123",
      rutas: {
        "POST /express": () =>
          Response.json({ code: "IDENTIFICADOR_DEMASIADO_LARGO", error: "El identificador es demasiado largo.", campo: "identificador" }, { status: 422 }),
      },
    });
    await nombre();
    await continuar();
    await waitFor(async () => expect(await placa()).toHaveAccessibleDescription("El identificador es demasiado largo."));
    expect(await placa()).toHaveAttribute("aria-invalid", "true");
    expect(await placa()).toHaveValue("TST123");
    expect(nav.push).not.toHaveBeenCalled();
  });

  it("el 422 del documento se pinta en su campo", async () => {
    montar({
      query: "modo=express&placa=TST123",
      rutas: { "POST /express": () => Response.json({ code: "DOCUMENTO_INVALIDO", error: "El documento no es válido.", campo: "documento_locatario" }, { status: 422 }) },
    });
    await nombre();
    fireEvent.change(tipoDocumento(), { target: { value: "CC" } });
    fireEvent.change(documento(), { target: { value: "12" } });
    await continuar();
    expect(await screen.findByText("El documento no es válido.")).toBeInTheDocument();
    expect(documento()).toHaveAttribute("aria-invalid", "true");
    expect(nav.push).not.toHaveBeenCalled();
  });

  it("403 sin código: «Sin permiso para crear diagnósticos», sin navegar y conservando el modo y lo digitado", async () => {
    montar({ query: "modo=express&placa=TST123", rutas: { "POST /express": () => new Response(null, { status: 403 }) } });
    await nombre();
    await continuar();
    expect(await screen.findByText("Sin permiso para crear diagnósticos")).toBeInTheDocument();
    expect(screen.getByText(/no tienes permiso/i)).toBeInTheDocument();
    expect(nav.replace).not.toHaveBeenCalled();
    expect(nav.push).not.toHaveBeenCalled();
    expect(await placa()).toHaveValue("TST123");
    expect(within(screen.getByRole("radiogroup", { name: "Tipo de diagnóstico" })).getByRole("radio", { name: "Express" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });

  it("403 de sesión lleva a /403 con su código", async () => {
    montar({ query: "modo=express&placa=TST123", rutas: { "POST /express": () => Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "No habilitado" }, { status: 403 }) } });
    await nombre();
    await continuar();
    await waitFor(() => expect(nav.replace).toHaveBeenCalledWith("/403?code=PRODUCTO_NO_HABILITADO"));
  });

  it("500 o red: «No se pudo lanzar el diagnóstico» con Reintentar, que vuelve a lanzar", async () => {
    let veces = 0;
    const api = montar({ query: "modo=express&placa=TST123", rutas: { "POST /express": () => (++veces === 1 ? Response.json({}, { status: 500 }) : creadoOk()) } });
    await nombre();
    await continuar();
    expect(await screen.findByText("No se pudo lanzar el diagnóstico")).toBeInTheDocument();
    expect(screen.getByText(/diagnóstico no respondió/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await iniciado()).toBeInTheDocument();
    expect(llamadas(api, "POST", "/express")).toHaveLength(2);
  });
});

describe("Express — resultado «Diagnóstico iniciado» con redirección de 5 s (AC18, AC19)", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date", "setInterval", "clearInterval"], shouldAdvanceTime: true });
    vi.setSystemTime(FECHA);
  });

  it("AC18: tras crear muestra el resultado con Ver tablero, Volver y Quedarme aquí y a los 5 s navega al tablero", async () => {
    montar({ query: "modo=express&placa=TST123" });
    await nombre();
    await continuar();
    expect(await iniciado()).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ver tablero" })).toHaveAttribute("href", `/diagnosticos/${ID_EXPRESS}`);
    expect(screen.getByRole("link", { name: "Volver" })).toHaveAttribute("href", "/");
    expect(screen.getByRole("button", { name: "Quedarme aquí" })).toBeInTheDocument();
    expect(nav.push).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(5000);
    expect(nav.push).toHaveBeenCalledWith(`/diagnosticos/${ID_EXPRESS}`);
  });

  it("AC19: «Quedarme aquí» cancela la redirección y lo anuncia en una región polite sin mover el foco", async () => {
    montar({ query: "modo=express&placa=TST123" });
    await nombre();
    await continuar();
    await iniciado();
    fireEvent.click(screen.getByRole("button", { name: "Quedarme aquí" }));
    await vi.advanceTimersByTimeAsync(6000);
    expect(nav.push).not.toHaveBeenCalled();
    expect(screen.getByText("Detuvimos la redirección. Sigues en esta página.")).toHaveAttribute("aria-live", "polite");
  });
});

describe("Express — pie, cargando y validaciones de Nuevo (AC16, AC17)", () => {
  it("AC16: el pie ofrece Cancelar hacia el inicio y el botón principal entra en estado cargando mientras se crea", async () => {
    let soltar: (r: Response) => void = () => {};
    montar({ query: "modo=express&placa=TST123", rutas: { "POST /express": () => new Promise<Response>((r) => (soltar = r)) } });
    await nombre();
    expect(within(screen.getByRole("region", { name: "Parámetros" })).getByRole("link", { name: "Cancelar" })).toHaveAttribute("href", "/");
    await continuar();
    await waitFor(() => expect(screen.getByRole("button", { name: PRINCIPAL })).toHaveAttribute("aria-busy", "true"));
    soltar(creadoOk());
    expect(await iniciado()).toBeInTheDocument();
  });

  it("AC16: con el resultado a la vista el selector Express/Masivo se oculta", async () => {
    montar({ query: "modo=express&placa=TST123" });
    await nombre();
    expect(screen.getByRole("radiogroup", { name: "Tipo de diagnóstico" })).toBeInTheDocument();
    await continuar();
    await iniciado();
    expect(screen.queryByRole("radiogroup", { name: "Tipo de diagnóstico" })).toBeNull();
  });

  it("AC17: Con Análisis IA sin tipo de trámite no envía y el foco va al tipo de trámite con su error", async () => {
    const api = montar({ query: "modo=express&placa=TST123" });
    await nombre();
    fireEvent.click(screen.getByRole("radio", { name: "Con Análisis IA" }));
    const tipo = await screen.findByRole("combobox", { name: /tipo de trámite/i });
    await continuar();
    await waitFor(() => expect(tipo).toHaveFocus());
    expect(tipo).toHaveAttribute("aria-invalid", "true");
    expect(tipo).toHaveAccessibleDescription(/.+/);
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });

  it("AC17: un nombre de más de 150 caracteres se indica en el contador al escribir y bloquea el envío", async () => {
    const api = montar({ query: "modo=express&placa=TST123" });
    fireEvent.change(await nombre(), { target: { value: "N".repeat(151) } });
    expect(screen.getByText(/151\/150/)).toBeInTheDocument();
    await continuar();
    expect(await screen.findByText(/admite hasta 150 caracteres/i)).toBeInTheDocument();
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });

  it("AC17: si el API responde error, el botón sale de cargando y se conserva lo digitado", async () => {
    montar({ query: "modo=express&placa=TST123", rutas: { "POST /express": () => Response.json({}, { status: 500 }) } });
    fireEvent.change(await nombre(), { target: { value: "Flota de prueba" } });
    await continuar();
    expect(await screen.findByText("No se pudo lanzar el diagnóstico")).toBeInTheDocument();
    const boton = screen.getByRole("button", { name: PRINCIPAL });
    expect(boton).not.toHaveAttribute("aria-busy");
    expect(boton).toBeEnabled();
    expect(screen.getByRole("textbox", { name: "Nombre del diagnóstico" })).toHaveValue("Flota de prueba");
    expect(screen.getByRole("textbox", { name: "Placa o VIN" })).toHaveValue("TST123");
  });
});

// HU #14562 (auditoría §4.3, AC16): el botón principal del Express dice lo que hace. Sin estimado lanza directo: «Iniciar
// diagnóstico»; con estimado ANTES_DE_LANZAR lleva al paso del estimado: «Continuar».
describe("Express — HU #14562 rótulo del botón principal", () => {
  it("sin estimado (NO_MOSTRAR) el botón dice «Iniciar diagnóstico»", async () => {
    montar({ query: "modo=express&placa=TST123", estimado: "NO_MOSTRAR" });
    await nombre();
    expect(screen.getByRole("button", { name: "Iniciar diagnóstico" })).toHaveAttribute("type", "submit");
    expect(screen.queryByRole("button", { name: "Continuar" })).toBeNull();
  });

  it("con estimado ANTES_DE_LANZAR el botón sigue diciendo «Continuar»", async () => {
    montar({ query: "modo=express&placa=TST123", estimado: "ANTES_DE_LANZAR" });
    await nombre();
    expect(screen.getByRole("button", { name: "Continuar" })).toHaveAttribute("type", "submit");
    expect(screen.queryByRole("button", { name: "Iniciar diagnóstico" })).toBeNull();
  });
});
