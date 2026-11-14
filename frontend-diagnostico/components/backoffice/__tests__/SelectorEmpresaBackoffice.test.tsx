import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { SelectorEmpresaBackoffice } from "@/components/backoffice/SelectorEmpresaBackoffice";
import { simularApi } from "@/lib/tablero/__tests__/datos";
import { EMPRESAS_BACKOFFICE, EMPRESA_CLIENTE, EMPRESA_SIN_NIT } from "@/__tests__/configuracion/datos";

// Uso de ejemplo: <SelectorEmpresaBackoffice id="lotes-empresa" value={empresa} onChange={(e) => aplicar({ empresa: e.id })} />
// pide GET /backoffice/empresas y muestra sus cuatro estados (cargando, error de Identidad, vacío, listo). En «listo» es un
// combobox con búsqueda por nombre o NIT (SearchableSelect de @flit/ui); con `value` ya elegido muestra el nombre de la empresa.

afterEach(() => vi.unstubAllGlobals());

const empresasOk = () => simularApi({ "GET /backoffice/empresas": () => Response.json(EMPRESAS_BACKOFFICE) });

describe("SelectorEmpresaBackoffice — AC3: estados del listado de empresas", () => {
  it("cargando: anuncia «Cargando empresas…» y no muestra el combobox", async () => {
    let responder: (r: Response) => void = () => {};
    simularApi({ "GET /backoffice/empresas": () => new Promise<Response>((r) => (responder = r)) });
    render(<SelectorEmpresaBackoffice value="" onChange={vi.fn()} />);

    expect(screen.getByRole("status", { name: "Cargando empresas…" })).toBeInTheDocument();
    expect(screen.queryByRole("combobox")).toBeNull();
    await act(async () => responder(Response.json(EMPRESAS_BACKOFFICE)));
    expect(await screen.findByRole("combobox", { name: "Empresa" })).toBeInTheDocument();
  });

  it("503 IDENTIDAD_NO_CONFIGURADA: «El listado de empresas no está configurado», sin combobox ni Reintentar", async () => {
    simularApi({ "GET /backoffice/empresas": () => Response.json({ code: "IDENTIDAD_NO_CONFIGURADA", error: "Identidad no está configurada." }, { status: 503 }) });
    render(<SelectorEmpresaBackoffice value="" onChange={vi.fn()} />);

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("El listado de empresas no está configurado");
    expect(alerta).toHaveTextContent(/Avisa a soporte FLIT/);
    expect(within(alerta).queryByRole("button", { name: "Reintentar" })).toBeNull();
    expect(screen.queryByRole("combobox")).toBeNull();
  });

  it("503 IDENTIDAD_NO_DISPONIBLE: «No se pudo cargar el listado de empresas» con Reintentar, que vuelve a pedirlo", async () => {
    let intento = 0;
    const fetchMock = simularApi({
      "GET /backoffice/empresas": () =>
        ++intento === 1
          ? Response.json({ code: "IDENTIDAD_NO_DISPONIBLE", error: "Identidad no respondió." }, { status: 503, headers: { "Retry-After": "30" } })
          : Response.json(EMPRESAS_BACKOFFICE),
    });
    render(<SelectorEmpresaBackoffice value="" onChange={vi.fn()} />);

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No se pudo cargar el listado de empresas");
    expect(screen.queryByRole("combobox")).toBeNull();
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("combobox", { name: "Empresa" })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("un cuerpo que no es arreglo es error (con Reintentar), nunca «sin empresas»", async () => {
    simularApi({ "GET /backoffice/empresas": () => Response.json({ empresas: [] }) });
    render(<SelectorEmpresaBackoffice value="" onChange={vi.fn()} />);

    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No se pudo cargar el listado de empresas");
    expect(within(alerta).getByRole("button", { name: "Reintentar" })).toBeInTheDocument();
    expect(screen.queryByText("No hay empresas con Diagnóstico habilitado")).toBeNull();
  });

  it("vacío: «No hay empresas con Diagnóstico habilitado», sin combobox", async () => {
    simularApi({ "GET /backoffice/empresas": () => Response.json([]) });
    render(<SelectorEmpresaBackoffice value="" onChange={vi.fn()} />);

    expect(await screen.findByText("No hay empresas con Diagnóstico habilitado")).toBeInTheDocument();
    expect(screen.queryByRole("combobox")).toBeNull();
  });

  it("listo sin elegir: muestra el aviso indicado y ninguna empresa elegida", async () => {
    empresasOk();
    render(<SelectorEmpresaBackoffice value="" onChange={vi.fn()} avisoSinElegir="Todavía no has elegido una empresa." />);

    expect(await screen.findByRole("combobox", { name: "Empresa" })).toHaveValue("");
    expect(screen.getByText("Todavía no has elegido una empresa.")).toBeInTheDocument();
  });
});

describe("SelectorEmpresaBackoffice — AC1/AC6: búsqueda por nombre o NIT, teclado y anuncio de resultados", () => {
  it("lista nombre + NIT, filtra por NIT y elige con flechas y Enter; entrega la empresa elegida", async () => {
    empresasOk();
    const onChange = vi.fn();
    render(<SelectorEmpresaBackoffice value="" onChange={onChange} />);

    const selector = await screen.findByRole("combobox", { name: "Empresa" });
    expect(selector).toHaveAttribute("placeholder", "Busca por nombre o NIT");
    fireEvent.click(selector);
    expect(within(screen.getByRole("listbox")).getAllByRole("option").map((o) => o.textContent)).toEqual([
      "Logística Andina SASNIT 900000001",
      "Óptima Carga SASsin NIT",
      "Transportes Cliente SASNIT 900000002",
    ]);

    fireEvent.change(selector, { target: { value: "900000002" } });
    expect(screen.getAllByRole("option")).toHaveLength(1);
    expect(screen.getByText("1 opciones disponibles")).toBeInTheDocument();
    fireEvent.keyDown(selector, { key: "ArrowDown" });
    fireEvent.keyDown(selector, { key: "Enter" });

    expect(onChange).toHaveBeenCalledWith({ id: EMPRESA_CLIENTE, nombre: "Transportes Cliente SAS", nit: "900000002" });
  });

  it("una búsqueda sin resultados muestra «Ninguna empresa coincide»", async () => {
    empresasOk();
    render(<SelectorEmpresaBackoffice value="" onChange={vi.fn()} />);

    fireEvent.change(await screen.findByRole("combobox", { name: "Empresa" }), { target: { value: "Empresa inexistente" } });
    expect(screen.getByText("Ninguna empresa coincide")).toBeInTheDocument();
    expect(screen.queryAllByRole("option")).toHaveLength(0);
  });
});

describe("SelectorEmpresaBackoffice — AC2: empresa ya elegida (de la URL)", () => {
  it("con value de una empresa del listado, el combobox la muestra elegida", async () => {
    empresasOk();
    render(<SelectorEmpresaBackoffice value={EMPRESA_SIN_NIT} onChange={vi.fn()} avisoSinElegir="Todavía no has elegido una empresa." />);

    expect(await screen.findByRole("combobox", { name: "Empresa" })).toHaveValue("Óptima Carga SAS");
    expect(screen.queryByText("Todavía no has elegido una empresa.")).toBeNull();
  });
});

// HU #14562 · D3 (axe color-contrast): la etiqueta «Empresa» del SearchableSelect daba 4,15:1 (#162744 al 60 % sobre
// blanco). Va con --di-text-tertiary (#546b7d: 5,56:1 sobre blanco; #a9b8d4 en oscuro: 7,45:1 sobre #162744), sin opacidad.
describe("SelectorEmpresaBackoffice — D3: contraste de la etiqueta del combobox", () => {
  it("la etiqueta visible usa el token de texto terciario y no se apaga con opacidad", async () => {
    empresasOk();
    render(<SelectorEmpresaBackoffice value="" onChange={vi.fn()} />);

    await screen.findByRole("combobox", { name: "Empresa" });
    const etiqueta = screen.getByText("Empresa", { selector: "label" });
    expect(etiqueta).toHaveClass("text-[color:var(--di-text-tertiary)]");
    expect(etiqueta.className).not.toMatch(/\bopacity-/);
  });
});

// HU #14562 · O-6 (auditoría D1–D3): el `hint` de cada opción (el NIT) tenía el mismo patrón que la etiqueta de D3
// (`opacity-60` sobre tinta). Va con --di-text-tertiary, sin opacidad. axe no lo vio porque la lista estaba cerrada.
describe("SelectorEmpresaBackoffice — O-6: contraste del hint de las opciones", () => {
  it("el NIT bajo cada empresa usa el token de texto terciario y no se apaga con opacidad", async () => {
    empresasOk();
    render(<SelectorEmpresaBackoffice value="" onChange={vi.fn()} />);

    fireEvent.click(await screen.findByRole("combobox", { name: "Empresa" }));
    const hint = within(screen.getByRole("listbox")).getByText("NIT 900000001");
    expect(hint).toHaveClass("text-[color:var(--di-text-tertiary)]");
    expect(hint.className).not.toMatch(/\bopacity-/);
  });
});
