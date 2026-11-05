import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { FormularioLlenadoManual } from "@/components/backoffice/FormularioLlenadoManual";
import type { Fuente } from "@/lib/configuracion";
import type { FormularioManual } from "@/lib/backoffice/api";
import { EMPRESA_A, formulario, llamadas, personaEnError, respuestaLlenado, simularApi } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: <FormularioLlenadoManual consultaId={celda.id} empresa={empresa} nombreFuente="SIMIT vehículo"
// identificador="TST001" onClose={…} onLlenada={(r) => …} /> pide GET /backoffice/consultas/{cid}/manual?empresa= y pinta el
// formulario del tipo de la fuente (SIMIT, avalúo, RUNT persona o RUES; en las de persona, una sección por persona en error).
// Al confirmar manda PUT con la versión; el 422 se muestra junto al campo y el 409 como aviso.

afterEach(() => vi.unstubAllGlobals());

const rutaGet = (cid: string) => `GET /backoffice/consultas/${cid}/manual`;
const rutaPut = (cid: string) => `PUT /backoffice/consultas/${cid}/manual`;

function montar(f: FormularioManual, put?: () => Response) {
  const fetchMock = simularApi({
    [rutaGet(f.id)]: () => Response.json(f),
    ...(put ? { [rutaPut(f.id)]: put } : {}),
  });
  const onLlenada = vi.fn();
  const onClose = vi.fn();
  const onCeldaCambiada = vi.fn();
  render(
    <FormularioLlenadoManual
      consultaId={f.id}
      empresa={EMPRESA_A}
      nombreFuente="Fuente"
      identificador="TST001"
      onClose={onClose}
      onLlenada={onLlenada}
      onCeldaCambiada={onCeldaCambiada}
    />,
  );
  return { fetchMock, onLlenada, onClose, onCeldaCambiada };
}

const dialogo = () => screen.getByRole("dialog");
const confirmar = () => fireEvent.click(within(dialogo()).getByRole("button", { name: "Confirmar llenado" }));
const escribir = (etiqueta: string | RegExp, valor: string, raiz: HTMLElement = dialogo()) =>
  fireEvent.change(within(raiz).getByLabelText(etiqueta), { target: { value: valor } });

/** El campo está marcado inválido y su aria-describedby apunta a un texto con el mensaje. */
function esperarErrorJunto(campo: HTMLElement, mensaje: RegExp) {
  expect(campo).toHaveAttribute("aria-invalid", "true");
  const id = campo.getAttribute("aria-describedby");
  expect(id).toBeTruthy();
  const descripcion = id!.split(" ").map((x) => document.getElementById(x)?.textContent ?? "").join(" ");
  expect(descripcion).toMatch(mensaje);
}

describe("FormularioLlenadoManual — cuatro estados", () => {
  it("cargando mientras llega el formulario y después el formulario de la fuente", async () => {
    montar(formulario("SIMIT_VEHICULO"));
    expect(within(dialogo()).getByRole("status", { name: /cargando/i })).toBeInTheDocument();
    expect(await within(dialogo()).findByLabelText("Semáforo")).toBeInTheDocument();
  });

  it("la lectura del formulario va con ?empresa=", async () => {
    const { fetchMock } = montar(formulario("SIMIT_VEHICULO"));
    await within(dialogo()).findByLabelText("Semáforo");
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://localhost");
    expect(url.searchParams.get("empresa")).toBe(EMPRESA_A);
  });

  it("error al leer el formulario: aviso con Reintentar", async () => {
    simularApi({ [rutaGet("celda-x")]: () => Response.json({ error: "Falla" }, { status: 500 }) });
    render(<FormularioLlenadoManual consultaId="celda-x" empresa={EMPRESA_A} nombreFuente="F" identificador="TST001" onClose={vi.fn()} onLlenada={vi.fn()} />);
    expect(await within(dialogo()).findByRole("button", { name: "Reintentar" })).toBeInTheDocument();
    expect(within(dialogo()).queryByRole("button", { name: "Confirmar llenado" })).toBeNull();
  });

  it.each([
    ["CELDA_BLOQUEADA_POR_RUNT", /RUNT/],
    ["FUENTE_NO_ADMITE_MANUAL", /no se llena a mano/i],
    ["CELDA_NO_EN_ERROR", /ya no está en error/i],
  ] as const)("admite_manual=false (%s): explica el motivo y no ofrece confirmar", async (motivo, texto) => {
    montar(formulario("SIMIT_VEHICULO", { admite_manual: false, motivo }));
    expect(await within(dialogo()).findByText(texto)).toBeInTheDocument();
    expect(within(dialogo()).queryByRole("button", { name: "Confirmar llenado" })).toBeNull();
  });
});

describe("FormularioLlenadoManual — un formulario por fuente", () => {
  it("SIMIT vehículo: semáforo y comparendos; al confirmar manda el PUT con la versión y avisa la celda llenada", async () => {
    const respuesta = respuestaLlenado("SIMIT_VEHICULO");
    const { fetchMock, onLlenada } = montar(formulario("SIMIT_VEHICULO"), () => Response.json(respuesta));
    await within(dialogo()).findByLabelText("Semáforo");

    fireEvent.change(within(dialogo()).getByLabelText("Semáforo"), { target: { value: "AMARILLO" } });
    fireEvent.click(within(dialogo()).getByRole("button", { name: "Agregar comparendo" }));
    escribir("Número del comparendo 1", "TST-C-0001");
    escribir("Valor del comparendo 1 (pesos)", "468500");
    confirmar();

    await waitFor(() => expect(onLlenada).toHaveBeenCalledWith(respuesta));
    const [put] = llamadas(fetchMock, "PUT", "/backoffice/consultas/celda-1-simit_vehiculo/manual");
    expect(new URL(String(put[0]), "http://localhost").searchParams.get("empresa")).toBe(EMPRESA_A);
    expect(JSON.parse(String((put[1] as RequestInit).body))).toEqual({
      version: 10,
      resultado: { semaforo: "AMARILLO", cantidad_comparendos: 1, comparendos: [{ numero: "TST-C-0001", valor: "468500" }] },
    });
  });

  it("avalúo: sin valor comercial, valor sugerido, fuente principal y fuentes (no los campos de SIMIT)", async () => {
    montar(formulario("AVALUO"));
    const d = await screen.findByRole("dialog");
    expect(await within(d).findByLabelText("Sin valor comercial")).toBeInTheDocument();
    expect(within(d).getByLabelText("Valor sugerido (pesos)")).toBeInTheDocument();
    expect(within(d).getByLabelText("Fuente principal")).toBeInTheDocument();
    expect(within(d).getByLabelText("Fuente 1")).toBeInTheDocument();
    expect(within(d).queryByLabelText("Semáforo")).toBeNull();
  });

  it("fuente de persona: una sección por persona en error con su documento enmascarado y el esquema de su consulta", async () => {
    const p1 = personaEnError(1, { consulta: "RUNT_PERSONA", documento_enmascarado: "****0101" });
    const p2 = personaEnError(3, { consulta: "RUES", tipo_documento: "N", documento_enmascarado: "****9903" });
    montar(formulario("RUNT_PROPIETARIO", { personas: [p1, p2], tipo_error: "PERSONAS_CON_ERROR" }));

    const secciones = await screen.findAllByRole("group", { name: /^Propietario/ });
    expect(secciones).toHaveLength(2);
    expect(within(secciones[0]).getByText(/\*\*\*\*0101/)).toBeInTheDocument();
    expect(within(secciones[0]).getByLabelText("Inscrito en el RUNT")).toBeInTheDocument();
    expect(within(secciones[1]).getByText(/\*\*\*\*9903/)).toBeInTheDocument();
    expect(within(secciones[1]).getByLabelText("Matrícula encontrada en el RUES")).toBeInTheDocument();
    expect(within(secciones[1]).queryByLabelText("Inscrito en el RUNT")).toBeNull();
  });

  it("fuente de persona: el PUT lleva todas las personas en error con su persona_id", async () => {
    const p1 = personaEnError(1);
    const p2 = personaEnError(2);
    const f = formulario("SIMIT_PROPIETARIO", { personas: [p1, p2], tipo_error: "PERSONAS_CON_ERROR" });
    const { fetchMock, onLlenada } = montar(f, () => Response.json(respuestaLlenado("SIMIT_PROPIETARIO")));
    const secciones = await screen.findAllByRole("group", { name: /^Propietario/ });
    fireEvent.change(within(secciones[0]).getByLabelText("Semáforo"), { target: { value: "VERDE" } });
    confirmar();

    await waitFor(() => expect(onLlenada).toHaveBeenCalled());
    const [put] = llamadas(fetchMock, "PUT", `/backoffice/consultas/${f.id}/manual`);
    const cuerpo = JSON.parse(String((put[1] as RequestInit).body));
    expect(cuerpo.personas.map((p: { persona_id: string }) => p.persona_id)).toEqual([p1.persona_id, p2.persona_id]);
    expect(cuerpo.personas[0].resultado).toEqual({ semaforo: "VERDE", cantidad_comparendos: 0, comparendos: [] });
  });
});

describe("FormularioLlenadoManual — errores: el 422 junto al campo y el 409 como aviso", () => {
  async function llenarAvaluoValido() {
    const d = await screen.findByRole("dialog");
    await within(d).findByLabelText("Valor sugerido (pesos)");
    escribir("Valor sugerido (pesos)", "48500000");
    fireEvent.change(within(d).getByLabelText("Fuente principal"), { target: { value: "fasecolda" } });
    fireEvent.change(within(d).getByLabelText("Fuente 1"), { target: { value: "fasecolda" } });
    fireEvent.change(within(d).getByLabelText("Estado de la fuente 1"), { target: { value: "OK" } });
    escribir("Valor de la fuente 1 (pesos)", "48500000");
  }

  it("422 RESULTADO_INVALIDO con campo: el mensaje queda junto a ese campo y no se avisa la celda llenada", async () => {
    const { onLlenada } = montar(formulario("AVALUO"), () =>
      Response.json({ code: "RESULTADO_INVALIDO", error: "El valor supera el máximo permitido.", campo: "resultado.fuentes[0].valor" }, { status: 422 }),
    );
    await llenarAvaluoValido();
    confirmar();

    const campo = within(dialogo()).getByLabelText("Valor de la fuente 1 (pesos)");
    await waitFor(() => esperarErrorJunto(campo, /supera el máximo/));
    expect(onLlenada).not.toHaveBeenCalled();
  });

  it("422 con un campo que no está en el formulario: aviso general con el mensaje", async () => {
    montar(formulario("AVALUO"), () => Response.json({ code: "RESULTADO_INVALIDO", error: "El campo «proveedor» no existe.", campo: "resultado.proveedor" }, { status: 422 }));
    await llenarAvaluoValido();
    confirmar();
    expect(await within(dialogo()).findByText(/proveedor/)).toBeInTheDocument();
  });

  it("409 CELDA_MODIFICADA: aviso, pide recargar la grilla y no avisa la celda llenada", async () => {
    const { onLlenada, onCeldaCambiada } = montar(formulario("SIMIT_VEHICULO"), () => Response.json({ code: "CELDA_MODIFICADA", error: "x" }, { status: 409 }));
    await within(dialogo()).findByLabelText("Semáforo");
    confirmar();

    const aviso = await within(dialogo()).findByRole("alert");
    expect(aviso).toHaveTextContent(/cambió/i);
    expect(onCeldaCambiada).toHaveBeenCalled();
    expect(onLlenada).not.toHaveBeenCalled();
  });

  it("AC5: valor fuera del esquema en el avalúo: error junto al campo y no se manda nada", async () => {
    const { fetchMock, onLlenada } = montar(formulario("AVALUO"), () => Response.json(respuestaLlenado("AVALUO")));
    await llenarAvaluoValido();
    escribir("Valor sugerido (pesos)", "cuarenta millones");
    confirmar();

    const campo = within(dialogo()).getByLabelText("Valor sugerido (pesos)");
    await waitFor(() => esperarErrorJunto(campo, /número entero/i));
    expect(llamadas(fetchMock, "PUT", "/backoffice/consultas/celda-1-avaluo/manual")).toHaveLength(0);
    expect(onLlenada).not.toHaveBeenCalled();
  });

  it("Cancelar cierra sin mandar nada", async () => {
    const { fetchMock, onClose } = montar(formulario("SIMIT_VEHICULO" as Fuente));
    await within(dialogo()).findByLabelText("Semáforo");
    fireEvent.click(within(dialogo()).getByRole("button", { name: "Cancelar" }));
    expect(onClose).toHaveBeenCalled();
    expect(fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method === "PUT")).toHaveLength(0);
  });
});
