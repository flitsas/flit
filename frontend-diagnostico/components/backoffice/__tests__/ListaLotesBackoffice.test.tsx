import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { ListaLotesBackoffice } from "@/components/backoffice/ListaLotesBackoffice";
import { EMPRESA_A, listaLotes, lote, simularApi } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: <ListaLotesBackoffice empresa="" estado={null} conErrores={false} pagina={1} /> en /backoffice/diagnosticos:
// sin compañía elegida no consulta; al elegirla en el selector (identificador uuid) pide GET /backoffice/diagnosticos?empresa=
// con los filtros de estado y «con errores» y lista los lotes con un enlace a su grilla (?empresa= incluido).

const h = vi.hoisted(() => ({ replace: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/backoffice/diagnosticos", useRouter: () => ({ replace: h.replace, push: vi.fn(), refresh: vi.fn() }) }));

beforeEach(() => h.replace.mockReset());
afterEach(() => vi.unstubAllGlobals());

const montar = (props: Partial<Parameters<typeof ListaLotesBackoffice>[0]> = {}) =>
  render(<ListaLotesBackoffice empresa="" estado={null} conErrores={false} pagina={1} {...props} />);

const elegir = (empresa: string) => {
  fireEvent.change(screen.getByLabelText("Identificador de la compañía"), { target: { value: empresa } });
  fireEvent.click(screen.getByRole("button", { name: "Ver lotes" }));
};

const consultas = (fetchMock: { mock: { calls: unknown[][] } }) =>
  fetchMock.mock.calls.map(([u]) => Object.fromEntries(new URL(String(u), "http://localhost").searchParams));

describe("ListaLotesBackoffice — AC1: compañía elegida y filtro «con errores»", () => {
  it("sin compañía elegida no consulta y lo dice", () => {
    const fetchMock = simularApi({});
    montar();
    expect(screen.getByText(/elige una compañía/i)).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("un identificador inválido muestra el error junto al campo y no consulta", () => {
    const fetchMock = simularApi({});
    montar();
    elegir("compania-a");
    const campo = screen.getByLabelText("Identificador de la compañía");
    expect(campo).toHaveAttribute("aria-invalid", "true");
    expect(document.getElementById(campo.getAttribute("aria-describedby")!)).toHaveTextContent(/uuid/i);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("elige A y filtra «con errores»: pide con empresa=A y con_errores=true y ve solo los lotes devueltos", async () => {
    const fetchMock = simularApi({
      "GET /backoffice/diagnosticos": (url) =>
        Response.json(url.searchParams.get("con_errores") === "true" ? listaLotes([lote(1, { nombre: "Lote con fallas", celdas_en_error: 3 })]) : listaLotes([lote(1, { nombre: "Lote con fallas", celdas_en_error: 3 }), lote(2, { nombre: "Lote limpio", celdas_en_error: 0, estado: "COMPLETADO" })])),
    });
    montar();
    elegir(EMPRESA_A);
    const tabla = await screen.findByRole("table", { name: /lotes de la compañía/i });
    expect(within(tabla).getByText("Lote limpio")).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText("Solo lotes con errores"));
    await waitFor(() => expect(within(screen.getByRole("table", { name: /lotes de la compañía/i })).queryByText("Lote limpio")).toBeNull());

    const ultima = consultas(fetchMock).at(-1)!;
    expect(ultima).toMatchObject({ empresa: EMPRESA_A, con_errores: "true" });
    const fila = within(screen.getByRole("table", { name: /lotes de la compañía/i })).getByRole("row", { name: /Lote con fallas/ });
    expect(within(fila).getByText(/3 con error/)).toBeInTheDocument();
    const enlace = within(fila).getByRole("link", { name: "Lote con fallas" });
    expect(enlace).toHaveAttribute("href", `/backoffice/diagnosticos/${lote(1).id}?empresa=${EMPRESA_A}`);
    expect(h.replace).toHaveBeenLastCalledWith(expect.stringContaining("con_errores=true"), expect.anything());
  });

  it("con la compañía en la URL consulta de entrada con sus filtros", async () => {
    const fetchMock = simularApi({ "GET /backoffice/diagnosticos": () => Response.json(listaLotes([lote(1)])) });
    montar({ empresa: EMPRESA_A, estado: "INCOMPLETO", conErrores: true });
    await screen.findByRole("table", { name: /lotes de la compañía/i });
    expect(consultas(fetchMock)[0]).toEqual({ empresa: EMPRESA_A, estado: "INCOMPLETO", con_errores: "true", pagina: "1" });
  });

  it("vacío: la compañía no tiene lotes con esos filtros", async () => {
    simularApi({ "GET /backoffice/diagnosticos": () => Response.json(listaLotes([])) });
    montar({ empresa: EMPRESA_A });
    expect(await screen.findByText(/no tiene lotes con estos filtros/i)).toBeInTheDocument();
  });

  it("falla: aviso con Reintentar", async () => {
    let n = 0;
    simularApi({ "GET /backoffice/diagnosticos": () => (++n === 1 ? Response.json({ error: "x" }, { status: 500 }) : Response.json(listaLotes([lote(1)]))) });
    montar({ empresa: EMPRESA_A });
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("table", { name: /lotes de la compañía/i })).toBeInTheDocument();
  });
});
