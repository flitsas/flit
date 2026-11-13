import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import { PaginaDetalleConsulta } from "@/components/tablero/PaginaDetalleConsulta";
import type { DetalleCelda as Detalle } from "@/lib/tablero/detalle";
import { ID, sesion } from "@/lib/tablero/__tests__/datos";
import { comparendo, detalle, persona, simitVehiculo } from "@/lib/tablero/__tests__/datosDetalle";

// Uso de ejemplo: <PaginaDetalleConsulta loteId vehiculoId fuente cargar /> pinta la miga de 4 niveles
// «Diagnósticos › lote › placa › fuente», la cabecera «fuente · placa» con el chip de estado, la respuesta de la fuente
// como lista de definición (sub-tablas plegables) y las personas con documento completo (D1 = a).
// Datos sintéticos.

vi.mock("@/components/DiagnosticoShell", () => ({
  useSesion: () => ({ sesion: sesion({ verDiagnosticos: true }), cargando: false, error: null, recargar: () => {} }),
}));
vi.mock("next/navigation", () => ({ usePathname: () => "/diagnosticos/x", useRouter: () => ({ replace: vi.fn(), push: vi.fn(), refresh: vi.fn() }) }));
afterEach(() => vi.unstubAllGlobals());

async function pintar(d: Detalle, extra: Partial<Parameters<typeof PaginaDetalleConsulta>[0]> = {}) {
  render(<PaginaDetalleConsulta loteId={ID} vehiculoId="vehiculo-1" fuente={d.fuente} cargar={async () => d} {...extra} />);
  await waitFor(() => expect(screen.queryByRole("status", { name: /cargando/i })).not.toBeInTheDocument());
}

describe("HU14562 AC39 — detalle con datos completos (D1)", () => {
  it("miga de 4 niveles «Diagnósticos › lote › placa › fuente» y cabecera «fuente · placa» con el estado", async () => {
    await pintar(simitVehiculo([comparendo(1)]));
    const migas = screen.getByRole("navigation", { name: "Migas de pan" });
    const items = within(migas).getAllByRole("listitem");
    expect(items.map((i) => i.textContent?.replace("›", "").trim())).toEqual(["Diagnósticos", "Lote de prueba", "TST001", "SIMIT vehículo"]);
    expect(within(items[3]).getByText("SIMIT vehículo")).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("heading", { level: 1, name: "SIMIT vehículo · TST001" })).toBeInTheDocument();
    const cabecera = screen.getByRole("region", { name: "SIMIT vehículo · TST001" });
    expect(within(cabecera).getByText("Ejecutado")).toBeInTheDocument();
  });

  it("en el backoffice la miga conserva las rutas del backoffice en sus 4 niveles", async () => {
    await pintar(simitVehiculo(), { backoffice: true, rutaListado: "/backoffice/diagnosticos?empresa=e1", rutaDiagnostico: `/backoffice/diagnosticos/${ID}?empresa=e1` });
    const migas = screen.getByRole("navigation", { name: "Migas de pan" });
    expect(within(migas).getByRole("link", { name: "Diagnósticos" })).toHaveAttribute("href", "/backoffice/diagnosticos?empresa=e1");
    expect(within(migas).getByRole("link", { name: "Lote de prueba" })).toHaveAttribute("href", `/backoffice/diagnosticos/${ID}?empresa=e1`);
    expect(within(migas).getAllByRole("listitem")).toHaveLength(4);
  });

  it("la respuesta de la fuente es una lista de definición con sub-tablas plegables", async () => {
    await pintar(simitVehiculo([comparendo(1), comparendo(2)]));
    const respuesta = screen.getByRole("region", { name: "Respuesta de la fuente" });
    const lista = within(respuesta).getByRole("group", { name: "Respuesta de la fuente" });
    const terminos = within(lista).getAllByRole("term").map((t) => t.textContent);
    expect(terminos).toContain("Cantidad de comparendos");
    const dd = within(lista).getAllByRole("term").find((t) => t.textContent === "Cantidad de comparendos")!.nextElementSibling;
    expect(dd?.textContent).toBe("2");
    const comparendos = within(respuesta).getByRole("table", { name: "Comparendos" });
    expect(comparendos.closest("details")).not.toBeNull();
  });

  it("personas con rol, documento completo y estado, sin enmascarar", async () => {
    await pintar(
      detalle("RUNT_PROPIETARIO", {
        personas: [persona(1, { documento: "900000001" })],
      }),
    );
    const auditoria = screen.getByRole("region", { name: "Auditoría de consulta" });
    expect(within(auditoria).getByText("C 900000001")).toBeInTheDocument();
    expect(within(auditoria).queryByText(/\*\*\*\*1001/)).not.toBeInTheDocument();
    const filas = within(within(auditoria).getByRole("table", { name: "Personas consultadas" })).getAllByRole("row");
    expect(filas[1]).toHaveTextContent("Propietario");
    expect(filas[1]).toHaveTextContent("Ejecutado");
  });
});
