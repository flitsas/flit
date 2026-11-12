import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { FuentesObligatoriasBackoffice } from "@/components/backoffice/FuentesObligatoriasBackoffice";
import { llamadas, simularApi } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: <FuentesObligatoriasBackoffice /> en /backoffice/fuentes-obligatorias pide
// GET /backoffice/fuentes-obligatorias (sin ?empresa= y sin /tipos-tramite) y pinta cada definición
// guardada con las casillas del enum Fuente menos RUNT.

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
  it("lleno: cada definición guardada trae sus casillas, sin RUNT y sin heredar lo que no vino", async () => {
    const fetchMock = simularApi({ [`GET ${RUTA}`]: () => Response.json(SEMILLA) });
    montar();

    expect(await screen.findByRole("region", { name: "Familia: Traspaso" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Tipo de trámite: Traspaso estándar" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Tipo de trámite: Traspaso por transferencia de dominio" })).toBeInTheDocument();
    const unilateral = screen.getByRole("region", { name: "Tipo de trámite: Traspaso unilateral" });

    expect(screen.getByText(/sigue siendo obligatorio en todo diagnóstico y no se configura aquí/i)).toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: "RUNT vehículo" })).toBeNull();
    expect(fetchMock.mock.calls.map(([u]) => String(u)).some((u) => u.includes("tipos-tramite") || u.includes("empresa="))).toBe(false);

    for (const nombre of ["Familia: Traspaso", "Tipo de trámite: Traspaso estándar", "Tipo de trámite: Traspaso por transferencia de dominio"]) {
      const region = screen.getByRole("region", { name: nombre });
      expect(within(region).getAllByRole("checkbox").map((c) => c.getAttribute("value"))).toEqual(CASILLAS);
      const piso = within(region).getByRole("checkbox", { name: "SIMIT propietario" });
      expect(piso).toBeChecked();
      expect(piso).toHaveAttribute("aria-disabled", "true");
      expect(piso).not.toBeDisabled();
      expect(piso).toHaveAccessibleDescription(/no se puede quitar/i);
      piso.focus();
      expect(piso).toHaveFocus();
      fireEvent.click(piso);
      expect(piso).toBeChecked();
      const libre = within(region).getByRole("checkbox", { name: "Avalúo comercial" });
      expect(libre).not.toBeChecked();
      expect(libre).not.toHaveAttribute("aria-disabled");
    }

    expect(within(unilateral).getByText(/definición guardada con la lista vacía/i)).toBeInTheDocument();
    expect(within(unilateral).queryByText(/hereda/i)).toBeNull();
    expect(within(unilateral).getAllByRole("checkbox").map((c) => c.getAttribute("value"))).toEqual(CASILLAS);
    for (const nombre of ["SIMIT vehículo", "SIMIT propietario", "SIMIT locatario"]) {
      const casilla = within(unilateral).getByRole("checkbox", { name: nombre });
      expect(casilla).not.toBeChecked();
      expect(casilla).toHaveAttribute("aria-disabled", "true");
      expect(casilla).not.toBeDisabled();
      expect(casilla).toHaveAccessibleDescription(/no se puede marcar/i);
      fireEvent.click(casilla);
      expect(casilla).not.toBeChecked();
    }
    const avaluo = within(unilateral).getByRole("checkbox", { name: "Avalúo comercial" });
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
    const standard = await screen.findByRole("region", { name: "Tipo de trámite: Traspaso estándar" });
    fireEvent.click(within(standard).getByRole("checkbox", { name: "Avalúo comercial" }));
    fireEvent.click(within(standard).getByRole("button", { name: "Guardar" }));

    await waitFor(() => expect(llamadas(fetchMock, "PUT", RUTA)).toHaveLength(1));
    const [url, init] = llamadas(fetchMock, "PUT", RUTA)[0];
    expect(String(url)).toBe(`/api/v1/diagnostico${RUTA}`);
    expect((init as RequestInit).method).toBe("PUT");
    expect(JSON.parse(String((init as RequestInit).body))).toEqual({
      familia: null,
      tipo_tramite_codigo: "TRASPASO_STANDARD",
      fuentes: ["SIMIT_PROPIETARIO", "AVALUO"],
    });

    expect(await within(standard).findByRole("alert")).toHaveTextContent(/no se puede quitar el piso/i);
    const unilateral = screen.getByRole("region", { name: "Tipo de trámite: Traspaso unilateral" });
    expect(within(unilateral).getByText(/definición guardada con la lista vacía/i)).toBeInTheDocument();
    expect(within(unilateral).queryByRole("alert")).toBeNull();
    expect(within(screen.getByRole("region", { name: "Familia: Traspaso" })).getByRole("checkbox", { name: "SIMIT propietario" })).toBeChecked();
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
    expect(await screen.findByRole("region", { name: "Familia: Traspasos" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Tipo de trámite: Traspaso de propiedad" })).toBeInTheDocument();
    expect(container).not.toHaveTextContent("TRASPASO_STANDARD");
    expect(container).not.toHaveTextContent(/TRASPASO\b/);
  });

  it("un tipo desconocido sin etiqueta dice «Novedad sin clasificar», nunca el código", async () => {
    const aviso = vi.spyOn(console, "warn").mockImplementation(() => {});
    simularApi({
      [`GET ${RUTA}`]: () => Response.json({ definiciones: [{ familia: null, tipo_tramite_codigo: "TIPO_FUTURO_X", fuentes: [] }] }),
    });
    const { container } = montar();
    expect(await screen.findByRole("region", { name: "Tipo de trámite: Novedad sin clasificar" })).toBeInTheDocument();
    expect(container).not.toHaveTextContent("TIPO_FUTURO_X");
    expect(aviso).toHaveBeenCalled();
    aviso.mockRestore();
  });
});
