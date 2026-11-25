import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { DetalleItem } from "@/components/liquidaciones/DetalleItem";
import { detalle, errorRegla, fetchPorRuta, periodo } from "./fixtures";

// Uso de ejemplo: <DetalleItem liquidacionId="L" itemId="I" puedeRelanzar puedeDescargar esSuperAdmin={false} /> en
// /liquidaciones/L/items/I. Muestra la placa con su propietario (sin dirección, correo ni teléfono), los totales y las
// vigencias (un monto null dice «Sin dato», un 0 dice «$ 0»: F2b-CF7), las observaciones del proveedor (nunca
// `validations[].sent`), el error clasificado, los recibos con «Descargar recibo» por el proxy BFF y «Relanzar».

const push = vi.fn();
let busqueda = new URLSearchParams();
vi.mock("next/navigation", () => ({
  usePathname: () => "/liquidaciones/L/items/I",
  useRouter: () => ({ replace: vi.fn(), push, refresh: vi.fn() }),
  useSearchParams: () => busqueda,
  notFound: () => {
    throw new Error("NEXT_NOT_FOUND");
  },
}));

const RUTA_ITEM = /^GET \/api\/v1\/impuestos\/liquidaciones\/L\/items\/I(\?|$)/;

function montar(cuerpo: unknown, props: Partial<Parameters<typeof DetalleItem>[0]> = {}, extra: Parameters<typeof fetchPorRuta>[0] = []) {
  const fetchMock = fetchPorRuta([[RUTA_ITEM, () => Response.json(cuerpo)], ...extra]);
  vi.stubGlobal("fetch", fetchMock);
  render(<DetalleItem liquidacionId="L" itemId="I" puedeRelanzar puedeDescargar esSuperAdmin={false} {...props} />);
  return fetchMock;
}

beforeEach(() => {
  push.mockReset();
  busqueda = new URLSearchParams();
});
afterEach(() => vi.unstubAllGlobals());

describe("DetalleItem — montos (F2b-CF7: null ≠ 0)", () => {
  it("total null dice «Sin dato»; en las vigencias el 0 es «$ 0» y el null «Sin dato»", async () => {
    montar(detalle({ totalAPagar: null, periodos: [periodo({ impuesto: 0, sancion: null })] }));

    const totales = await screen.findByRole("region", { name: "Totales" });
    expect(within(totales).getByText("Total a pagar").nextElementSibling).toHaveTextContent("Sin dato");
    const tabla = screen.getByRole("table", { name: "Vigencias" });
    const fila = within(tabla).getAllByRole("row")[1];
    expect(within(fila).getByText("$ 0")).toBeInTheDocument();
    expect(within(fila).getAllByText("Sin dato").length).toBeGreaterThanOrEqual(2);
    expect(within(tabla).queryByText("$ 0,00")).toBeNull();
  });

  it("un período no liquidable lleva el literal del contrato", async () => {
    montar(detalle({ periodos: [periodo({ noLiquidable: true, totalAPagar: null })] }));
    expect(await screen.findByText("El portal no permite liquidar esta vigencia")).toBeInTheDocument();
  });

  it("sin vigencias: «Sin vigencias pendientes»", async () => {
    montar(detalle({ periodos: [] }));
    expect(await screen.findByText("Sin vigencias pendientes")).toBeInTheDocument();
  });
});

describe("DetalleItem — propietario, observaciones y PII", () => {
  it("propietario y documento completos (DA-7=b), sin pintar validations[].sent", async () => {
    montar(
      detalle({
        resultado: {
          warnings: [{ code: "period_not_liquidable", message: "Vigencia 2019 no liquidable" }],
          validations: [{ field: "owner_name", message: "El nombre no coincide", sent: "Dato Enviado Secreto", portal: "otro" }],
          file_errors: [{ fileId: "f-1", message: "No se pudo generar el PDF" }],
        },
      }),
    );

    expect(await screen.findByText("Propietario Demo")).toBeInTheDocument();
    expect(screen.getByText("CC 0000000")).toBeInTheDocument();
    expect(screen.getByText("Cundinamarca")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Observaciones del proveedor" }));
    expect(screen.getByText("Vigencia 2019 no liquidable")).toBeInTheDocument();
    expect(screen.getByText(/El nombre no coincide/)).toBeInTheDocument();
    expect(screen.getByText("No se pudo generar el PDF")).toBeInTheDocument();
    expect(screen.queryByText(/Dato Enviado Secreto/)).toBeNull();
  });

  it("sin observaciones, la tarjeta no se pinta", async () => {
    montar(detalle({ resultado: null }));
    await screen.findByText("Propietario Demo");
    expect(screen.queryByRole("button", { name: "Observaciones del proveedor" })).toBeNull();
  });
});

describe("DetalleItem — estado y relanzar", () => {
  it("error reintentable: aviso con la clase del contrato y «Relanzar» solo con permiso", async () => {
    montar(detalle({ estado: "ERROR", errorClase: "REINTENTABLE", errorCodigo: "PROVIDER_TIMEOUT", resultadoStatus: "error", totalAPagar: null }));
    expect(await screen.findByText("Reintentable como nueva consulta")).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "Estado: Con error" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Relanzar" })).toBeInTheDocument();
    // El código solo lo ve el Super Admin.
    expect(screen.queryByText(/PROVIDER_TIMEOUT/)).toBeNull();
  });

  it("sin impuestos.liquidaciones.crear no hay «Relanzar» en el DOM; el SA ve el código", async () => {
    // El Super Admin lee con ?empresa (regla del backend 1.1.0).
    busqueda = new URLSearchParams("empresa=e-9");
    montar(detalle({ estado: "INCIERTO_REQUIERE_DECISION", errorCodigo: "X_CODE" }), { puedeRelanzar: false, esSuperAdmin: true });
    await screen.findByRole("status", { name: "Estado: Incierto — requiere decisión" });
    expect(screen.queryByRole("button", { name: "Relanzar" })).toBeNull();
    expect(screen.getByText("Código: X_CODE")).toBeInTheDocument();
  });

  it("datos incompletos: nombra el campo faltante con la etiqueta del contrato", async () => {
    montar(detalle({ estado: "DATOS_INCOMPLETOS", campoFaltante: "OwnerEmail", resultadoStatus: null, totalAPagar: null, periodos: [] }));
    expect(await screen.findByText("Faltan datos del RUNT para liquidar: Correo del propietario.")).toBeInTheDocument();
  });

  it("paz y salvo exitoso: chip «Paz y salvo»; marcas parcial y posible duplicado con texto", async () => {
    montar(detalle({ resultadoStatus: "paz_y_salvo", parcial: true, posibleDuplicado: true }));
    expect(await screen.findByRole("status", { name: "Estado: Paz y salvo" })).toBeInTheDocument();
    expect(screen.getAllByText("Parcial").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Posible duplicado").length).toBeGreaterThan(0);
  });
});

describe("DetalleItem — recibos por el proxy BFF", () => {
  const RECIBOS = [
    { id: "R-1", tipo: "VIGENCIA", periodos: ["2025"], estado: "GUARDADO", motivo: null, razon: null },
    { id: "R-2", tipo: "CONSOLIDADO", periodos: [], estado: "SIN_ARCHIVO", motivo: "SIN_FILE_ID", razon: "el portal no generó recibo" },
  ];

  it("«Descargar recibo» pide el PDF al BFF con ?empresa= y lo entrega como descarga", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    const crear = vi.fn(() => "blob:x");
    vi.stubGlobal("URL", Object.assign(globalThis.URL, { createObjectURL: crear, revokeObjectURL: vi.fn() }));
    const fetchMock = montar(detalle({ recibos: RECIBOS }), {}, [
      [/^GET \/api\/v1\/impuestos\/liquidaciones\/L\/items\/I\/recibos\/R-1\?empresa=e-9$/, () => new Response(new Blob(["%PDF"]), { status: 200, headers: { "Content-Type": "application/pdf" } })],
    ]);

    const boton = await screen.findByRole("button", { name: "Descargar recibo: Recibo de vigencia 2025" });
    fireEvent.click(boton);

    await waitFor(() => expect(crear).toHaveBeenCalled());
    expect(fetchMock.mock.calls.some(([u]) => String(u).includes("/recibos/R-1?empresa=e-9"))).toBe(true);
    expect(screen.getByText("Sin recibo: el portal no generó recibo")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Recibo consolidado/ })).toBeNull();
  });

  it("409 RECIBO_NO_DISPONIBLE: el literal del contrato bajo el botón con role=alert", async () => {
    montar(detalle({ recibos: RECIBOS }), {}, [[/recibos\/R-1/, () => errorRegla(409, "RECIBO_NO_DISPONIBLE", "El recibo no está disponible.")]]);
    fireEvent.click(await screen.findByRole("button", { name: "Descargar recibo: Recibo de vigencia 2025" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("El recibo no está disponible.");
  });

  it("sin impuestos.recibos.descargar solo se ve el estado del recibo", async () => {
    montar(detalle({ recibos: RECIBOS }), { puedeDescargar: false });
    expect(await screen.findByText("Disponible")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Descargar recibo/ })).toBeNull();
  });

  it("sin recibos: «Esta liquidación no tiene recibos.»", async () => {
    montar(detalle({ recibos: [] }));
    expect(await screen.findByText("Esta liquidación no tiene recibos.")).toBeInTheDocument();
  });
});

describe("DetalleItem — cargando y error", () => {
  it("cargando: «Cargando la placa…»", () => {
    vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(() => {})));
    render(<DetalleItem liquidacionId="L" itemId="I" puedeRelanzar puedeDescargar esSuperAdmin={false} />);
    expect(screen.getByRole("status", { name: "Cargando la placa…" })).toBeInTheDocument();
  });

  it("error: «No se pudo cargar la placa» + Reintentar", async () => {
    let n = 0;
    vi.stubGlobal("fetch", fetchPorRuta([[RUTA_ITEM, () => (++n === 1 ? new Response("", { status: 500 }) : Response.json(detalle()))]]));
    render(<DetalleItem liquidacionId="L" itemId="I" puedeRelanzar puedeDescargar esSuperAdmin={false} />);
    expect(await screen.findByText("No se pudo cargar la placa")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("Propietario Demo")).toBeInTheDocument();
  });
});

describe("DetalleItem — contrato 1.1.0 (delta 5: secretaría)", () => {
  const secretariaDe = () => within(screen.getByRole("region", { name: "Vehículo y propietario" })).getByText("Secretaría").nextElementSibling;

  it("con nombre y ciudad muestra ambos", async () => {
    montar(detalle({ secretaria: { nombre: "Secretaría Demo", ciudad: "Ciudad Demo" } }));
    await screen.findByText("Propietario Demo");
    expect(secretariaDe()).toHaveTextContent("Secretaría Demo, Ciudad Demo");
  });

  it("con nombre null (lo que hoy devuelve el backend) muestra solo la ciudad", async () => {
    montar(detalle({ secretaria: { nombre: null, ciudad: "Ciudad Demo" } }));
    await screen.findByText("Propietario Demo");
    expect(secretariaDe()).toHaveTextContent(/^Ciudad Demo$/);
  });

  it("sin secretaría (null) dice «Sin dato»", async () => {
    montar(detalle({ secretaria: null }));
    await screen.findByText("Propietario Demo");
    expect(secretariaDe()).toHaveTextContent("Sin dato");
  });
});

describe("DetalleItem — Super Admin y ?empresa", () => {
  it("el Super Admin sin compañía no pide nada y ve «Elige una compañía.»", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    render(<DetalleItem liquidacionId="L" itemId="I" puedeRelanzar puedeDescargar esSuperAdmin />);
    expect(screen.getByText("Elige una compañía.")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("el Super Admin con compañía la envía al pedir la placa", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    const fetchMock = montar(detalle(), { esSuperAdmin: true }, [[/^GET \/api\/v1\/impuestos\/liquidaciones\/L\?/, () => Response.json({})]]);
    await screen.findByText("Propietario Demo");
    const pedida = fetchMock.mock.calls.map(([u]) => new URL(String(u), "http://app")).find((u) => u.pathname.endsWith("/items/I"));
    expect(pedida?.searchParams.get("empresa")).toBe("e-9");
  });

  it("un 404 EMPRESA_NO_ENCONTRADA va en línea con su literal, no a la 404", async () => {
    busqueda = new URLSearchParams("empresa=e-9");
    vi.stubGlobal("fetch", fetchPorRuta([[/liquidaciones\/L/, () => errorRegla(404, "EMPRESA_NO_ENCONTRADA", "detalle interno")]]));
    render(<DetalleItem liquidacionId="L" itemId="I" puedeRelanzar puedeDescargar esSuperAdmin />);
    expect(await screen.findByText("No se pudo cargar la placa")).toBeInTheDocument();
    expect(screen.getByText("La compañía no existe o no tiene Impuestos.")).toBeInTheDocument();
  });
});
