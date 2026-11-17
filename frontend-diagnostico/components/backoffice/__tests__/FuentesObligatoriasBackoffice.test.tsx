import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { FuentesObligatoriasBackoffice } from "@/components/backoffice/FuentesObligatoriasBackoffice";
import { llamadas, simularApi } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: <FuentesObligatoriasBackoffice /> en /backoffice/fuentes-obligatorias pide
// GET /backoffice/fuentes-obligatorias (sin ?empresa= y sin /tipos-tramite) y pinta cada definición
// guardada como una fila de la TablaEditable (HU #14562 · AC48): definición × fuentes, con RUNT vehículo fijo.

afterEach(() => vi.unstubAllGlobals());

const RUTA = "/backoffice/fuentes-obligatorias";

const CASILLAS = [
  "RUNT_PROPIETARIO",
  "RUNT_LOCATARIO",
  "SIMIT_VEHICULO",
  "SIMIT_PROPIETARIO",
  "SIMIT_LOCATARIO",
  "AVALUO",
  "IMPUESTO_DEPARTAMENTAL",
];

const SEMILLA = {
  definiciones: [
    { familia: "TRASPASO", tipo_tramite_codigo: null, fuentes: ["SIMIT_PROPIETARIO"] },
    { familia: null, tipo_tramite_codigo: "TRASPASO_STANDARD", fuentes: ["SIMIT_PROPIETARIO"] },
    { familia: null, tipo_tramite_codigo: "TRASPASO_TRANSFERENCIA_DE_DOMINIO", fuentes: ["SIMIT_PROPIETARIO"] },
    { familia: null, tipo_tramite_codigo: "TRASPASO_UNILATERAL", fuentes: [] },
  ],
};

const montar = () => render(<FuentesObligatoriasBackoffice />);

const fila = (nombre: string) => screen.getByRole("row", { name: nombre });
const casilla = (f: HTMLElement, fuente: string) => within(f).getByRole("checkbox", { name: new RegExp(`^${fuente} de `) });
const boton = (f: HTMLElement, accion: string) => within(f).getByRole("button", { name: new RegExp(`^${accion} `) });

describe("FuentesObligatoriasBackoffice — cuatro estados", () => {
  it("vacío: el GET con definiciones vacías no pinta casillas", async () => {
    const fetchMock = simularApi({ [`GET ${RUTA}`]: () => Response.json({ definiciones: [] }) });
    montar();
    expect(await screen.findByRole("heading", { name: "No hay definiciones guardadas" })).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(screen.queryByRole("button", { name: "Guardar" })).toBeNull();
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://localhost");
    expect(url.pathname).toBe(`/api/v1/diagnostico${RUTA}`);
    expect(url.search).toBe("");
  });

  it("cargando: muestra el loader mientras el GET no responde", () => {
    simularApi({ [`GET ${RUTA}`]: () => new Promise(() => {}) });
    montar();
    expect(screen.getByRole("status", { name: "Cargando las fuentes obligatorias…" })).toBeInTheDocument();
  });

  it("error: aviso rojo con Reintentar y, al recuperarse, el estado vacío", async () => {
    let n = 0;
    simularApi({
      [`GET ${RUTA}`]: () => (++n === 1 ? Response.json({ error: "x" }, { status: 500 }) : Response.json({ definiciones: [] })),
    });
    montar();
    const aviso = await screen.findByRole("alert");
    expect(aviso).toHaveTextContent(/no se pudieron cargar las fuentes obligatorias/i);
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("heading", { name: "No hay definiciones guardadas" })).toBeInTheDocument();
  });
});

describe("FuentesObligatoriasBackoffice — piso, unilateral y RUNT", () => {
  it("lleno: una TablaEditable con una fila por definición y una columna por fuente, sin heredar lo que no vino", async () => {
    const fetchMock = simularApi({ [`GET ${RUTA}`]: () => Response.json(SEMILLA) });
    montar();

    const tabla = await screen.findByRole("table", { name: "Fuentes obligatorias por definición" });
    expect(within(tabla).getByRole("columnheader", { name: "Definición" })).toBeInTheDocument();
    expect(within(tabla).getByRole("columnheader", { name: "RUNT vehículo" })).toBeInTheDocument();
    expect(fila("Familia: Traspaso")).toBeInTheDocument();
    expect(fila("Tipo de trámite: Traspaso estándar")).toBeInTheDocument();
    expect(fila("Tipo de trámite: Traspaso por transferencia de dominio")).toBeInTheDocument();
    const unilateral = fila("Tipo de trámite: Traspaso unilateral");

    expect(screen.getByText(/sigue siendo obligatorio en todo diagnóstico y no se configura aquí/i)).toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: /^RUNT vehículo/ })).toBeNull();
    expect(fetchMock.mock.calls.map(([u]) => String(u)).some((u) => u.includes("tipos-tramite") || u.includes("empresa="))).toBe(false);

    for (const nombre of ["Familia: Traspaso", "Tipo de trámite: Traspaso estándar", "Tipo de trámite: Traspaso por transferencia de dominio"]) {
      const f = fila(nombre);
      expect(within(f).getAllByRole("checkbox").map((c) => c.getAttribute("value"))).toEqual(CASILLAS);
      const piso = casilla(f, "SIMIT propietario");
      expect(piso).toBeChecked();
      expect(piso).toHaveAttribute("aria-disabled", "true");
      expect(piso).not.toBeDisabled();
      expect(piso).toHaveAccessibleDescription(/no se puede quitar/i);
      piso.focus();
      expect(piso).toHaveFocus();
      fireEvent.click(piso);
      expect(piso).toBeChecked();
      const libre = casilla(f, "Avalúo comercial");
      expect(libre).not.toBeChecked();
      expect(libre).not.toHaveAttribute("aria-disabled");
    }

    expect(within(unilateral).getByText(/lista vacía/i)).toBeInTheDocument();
    expect(within(unilateral).queryByText(/hereda/i)).toBeNull();
    expect(within(unilateral).getAllByRole("checkbox").map((c) => c.getAttribute("value"))).toEqual(CASILLAS);
    for (const nombre of ["SIMIT vehículo", "SIMIT propietario", "SIMIT locatario"]) {
      const c = casilla(unilateral, nombre);
      expect(c).not.toBeChecked();
      expect(c).toHaveAttribute("aria-disabled", "true");
      expect(c).not.toBeDisabled();
      expect(c).toHaveAccessibleDescription(/no se puede marcar/i);
      fireEvent.click(c);
      expect(c).not.toBeChecked();
    }
    const avaluo = casilla(unilateral, "Avalúo comercial");
    expect(avaluo).not.toHaveAttribute("aria-disabled");
    fireEvent.click(avaluo);
    expect(avaluo).toBeChecked();
  });
});

describe("FuentesObligatoriasBackoffice — guardar", () => {
  it("el PUT lleva el arreglo completo de esa definición y un error no borra las demás", async () => {
    const fetchMock = simularApi({
      [`GET ${RUTA}`]: () => Response.json(SEMILLA),
      [`PUT ${RUTA}`]: () => Response.json({ code: "PISO_NO_DISPENSABLE", error: "No se puede quitar el piso." }, { status: 422 }),
    });
    montar();
    await screen.findByRole("table", { name: "Fuentes obligatorias por definición" });
    const standard = fila("Tipo de trámite: Traspaso estándar");
    fireEvent.click(casilla(standard, "Avalúo comercial"));
    fireEvent.click(boton(standard, "Guardar"));

    await waitFor(() => expect(llamadas(fetchMock, "PUT", RUTA)).toHaveLength(1));
    const [url, init] = llamadas(fetchMock, "PUT", RUTA)[0];
    expect(String(url)).toBe(`/api/v1/diagnostico${RUTA}`);
    expect((init as RequestInit).method).toBe("PUT");
    expect(JSON.parse(String((init as RequestInit).body))).toEqual({
      familia: null,
      tipo_tramite_codigo: "TRASPASO_STANDARD",
      fuentes: ["SIMIT_PROPIETARIO", "AVALUO"],
    });

    const aviso = await screen.findByRole("alert");
    expect(aviso).toHaveTextContent(/Traspaso estándar/);
    expect(aviso).toHaveTextContent(/no se puede quitar el piso/i);
    expect(within(standard).getByText("Error de guardado")).toBeInTheDocument();
    expect(casilla(standard, "Avalúo comercial")).toBeChecked();
    const unilateral = fila("Tipo de trámite: Traspaso unilateral");
    expect(within(unilateral).getByText("Sin cambios")).toBeInTheDocument();
    expect(casilla(fila("Familia: Traspaso"), "SIMIT propietario")).toBeChecked();
  });
});

describe("FuentesObligatoriasBackoffice — Punto 5: nombres, no códigos", () => {
  it("titula cada definición con la etiqueta de la API y no pinta el código del tipo ni de la familia", async () => {
    simularApi({
      [`GET ${RUTA}`]: () =>
        Response.json({
          definiciones: [
            { familia: "TRASPASO", familia_etiqueta: "Traspasos", tipo_tramite_codigo: null, fuentes: ["SIMIT_PROPIETARIO"] },
            {
              familia: null,
              tipo_tramite_codigo: "TRASPASO_STANDARD",
              tipo_tramite_etiqueta: "Traspaso de propiedad",
              fuentes: ["SIMIT_PROPIETARIO"],
            },
          ],
        }),
    });
    const { container } = montar();
    expect(await screen.findByRole("row", { name: "Familia: Traspasos" })).toBeInTheDocument();
    expect(fila("Tipo de trámite: Traspaso de propiedad")).toBeInTheDocument();
    expect(container).not.toHaveTextContent("TRASPASO_STANDARD");
    expect(container).not.toHaveTextContent(/TRASPASO\b/);
  });

  it("un tipo desconocido sin etiqueta dice «Novedad sin clasificar», nunca el código", async () => {
    const aviso = vi.spyOn(console, "warn").mockImplementation(() => {});
    simularApi({
      [`GET ${RUTA}`]: () => Response.json({ definiciones: [{ familia: null, tipo_tramite_codigo: "TIPO_FUTURO_X", fuentes: [] }] }),
    });
    const { container } = montar();
    expect(await screen.findByRole("row", { name: "Tipo de trámite: Novedad sin clasificar" })).toBeInTheDocument();
    expect(container).not.toHaveTextContent("TIPO_FUTURO_X");
    expect(aviso).toHaveBeenCalled();
    aviso.mockRestore();
  });
});

// HU #14562 · F-10 · AC48 (UI Kit 2026): TablaEditable, RUNT vehículo fijo con candado y estado por fila.
describe("FuentesObligatoriasBackoffice — estado por fila (AC48)", () => {
  const UNILATERAL = "Tipo de trámite: Traspaso unilateral";

  it("RUNT vehículo aparece fijo con candado en cada fila y no es una casilla desmarcable", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json(SEMILLA) });
    montar();
    const f = await screen.findByRole("row", { name: UNILATERAL });
    expect(within(f).getByText("Siempre obligatoria")).toBeInTheDocument();
    expect(within(f).getByTestId("candado-runt")).toBeInTheDocument();
    expect(within(f).queryByRole("checkbox", { name: /^RUNT vehículo/ })).toBeNull();
  });

  it("pasa de «Sin cambios» a «Editado» con Descartar y Guardar, y Descartar restaura", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json(SEMILLA) });
    montar();
    const f = await screen.findByRole("row", { name: UNILATERAL });
    expect(within(f).getByText("Sin cambios")).toBeInTheDocument();
    expect(within(f).queryByRole("button", { name: /^Descartar/ })).toBeNull();
    fireEvent.click(casilla(f, "Avalúo comercial"));
    expect(within(f).getByText("Editado")).toBeInTheDocument();
    expect(boton(f, "Guardar")).toBeEnabled();
    fireEvent.click(boton(f, "Descartar"));
    expect(within(f).getByText("Sin cambios")).toBeInTheDocument();
    expect(casilla(f, "Avalúo comercial")).not.toBeChecked();
  });

  it("si el PUT falla muestra «Error de guardado» con Reintentar, que reenvía la fila", async () => {
    let puts = 0;
    simularApi({
      [`GET ${RUTA}`]: () => Response.json(SEMILLA),
      [`PUT ${RUTA}`]: () => {
        puts += 1;
        return puts === 1
          ? Response.json({ title: "Error" }, { status: 500 })
          : Response.json({ familia: null, tipo_tramite_codigo: "TRASPASO_UNILATERAL", fuentes: ["AVALUO"] });
      },
    });
    montar();
    const f = await screen.findByRole("row", { name: UNILATERAL });
    fireEvent.click(casilla(f, "Avalúo comercial"));
    fireEvent.click(boton(f, "Guardar"));
    expect(await within(f).findByText("Error de guardado")).toBeInTheDocument();
    fireEvent.click(boton(f, "Reintentar"));
    await waitFor(() => expect(within(f).getByText("Sin cambios")).toBeInTheDocument());
    expect(screen.queryByRole("alert")).toBeNull();
    expect(puts).toBe(2);
  });

  it("tras guardar, Descartar vuelve a lo último guardado y no a lo leído al abrir", async () => {
    simularApi({
      [`GET ${RUTA}`]: () => Response.json(SEMILLA),
      [`PUT ${RUTA}`]: () => Response.json({ familia: null, tipo_tramite_codigo: "TRASPASO_UNILATERAL", fuentes: ["AVALUO"] }),
    });
    montar();
    const f = await screen.findByRole("row", { name: UNILATERAL });
    fireEvent.click(casilla(f, "Avalúo comercial"));
    fireEvent.click(boton(f, "Guardar"));
    await waitFor(() => expect(within(f).getByText("Sin cambios")).toBeInTheDocument());
    fireEvent.click(casilla(f, "Impuesto departamental"));
    fireEvent.click(boton(f, "Descartar"));
    expect(casilla(f, "Avalúo comercial")).toBeChecked();
    expect(casilla(f, "Impuesto departamental")).not.toBeChecked();
  });
});

// HU #14562 · M6 (AC56): F-10 es la TablaEditable del kit, así que por debajo de 900 px cada definición es una tarjeta y
// cada celda dice su columna (definición, RUNT fijo y cada fuente configurable).
describe("FuentesObligatoriasBackoffice — HU #14562 M6: tarjetas por debajo de 900 px", () => {
  it("cada celda de la fila lleva el nombre de su columna en data-label", async () => {
    simularApi({ [`GET ${RUTA}`]: () => Response.json(SEMILLA) });
    montar();
    const f = await screen.findByRole("row", { name: "Tipo de trámite: Traspaso unilateral" });
    const etiquetas = Array.from(f.children).map((c) => c.getAttribute("data-label"));
    expect(etiquetas[0]).toBe("Definición");
    expect(etiquetas[1]).toBe("RUNT vehículo");
    expect(etiquetas).toContain("Avalúo comercial");
    expect(etiquetas.slice(-2)).toEqual(["Estado", "Acciones"]);
    expect(etiquetas.every((e) => !!e)).toBe(true);
  });
});
