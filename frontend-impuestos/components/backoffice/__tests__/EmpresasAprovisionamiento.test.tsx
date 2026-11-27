import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { EmpresasAprovisionamiento } from "@/components/backoffice/EmpresasAprovisionamiento";
import type { EstadoSesion } from "@/components/ImpuestosShell";

// Uso de ejemplo: <EmpresasAprovisionamiento /> en /backoffice/empresas (solo Super Admin). Lee GET /backoffice/empresas
// (PaginaEmpresasBackoffice, contrato impuestos.v1.yaml 1.1.0) y pinta Compañía · NIT · Usuarios · Estado · Motivo ·
// Acción. `usuarios` (delta 6) es opcional: sin él la celda queda vacía. `suspendida` se ve con texto, nunca solo color.

const replace = vi.fn();
vi.mock("next/navigation", () => ({
  usePathname: () => "/backoffice/empresas",
  useRouter: () => ({ replace, push: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
}));

let estadoSesion: EstadoSesion;
vi.mock("@/components/ImpuestosShell", () => ({ useSesion: () => estadoSesion }));

const LISTO = { estado: "LISTO", motivo: null, accion: null, firmaConfirmada: true };
const EMPRESAS = {
  items: [
    { id: "e-1", nombre: "Compañía Uno", nit: "900000001", aprovisionamiento: LISTO, suspendida: false, usuarios: 12 },
    { id: "e-2", nombre: "Compañía Dos", nit: null, aprovisionamiento: LISTO, suspendida: true },
    { id: "e-3", nombre: "Compañía Tres", nit: "900000003", aprovisionamiento: { ...LISTO, estado: "SUSPENDIDO" }, suspendida: true, usuarios: 0 },
  ],
  total: 3,
  page: 1,
  pageSize: 50,
};

beforeEach(() => {
  replace.mockReset();
  estadoSesion = {
    sesion: {
      usuario: { id: "u-1", email: "nombre@empresa.com", nombre: "Persona Demo" },
      empresa: { id: "e-flit", nombre: "FLIT" },
      roles: [],
      permisos: [],
      esSuperAdmin: true,
      aprovisionamiento: null,
    },
    cargando: false,
    error: null,
    recargar: vi.fn(),
  };
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => Response.json(EMPRESAS)),
  );
});
afterEach(() => vi.unstubAllGlobals());

const fila = (nombre: string) => screen.getByText(nombre).closest("tr") as HTMLElement;
const celda = (tr: HTMLElement, etiqueta: string) => Array.from(tr.querySelectorAll("td")).find((td) => td.getAttribute("data-label") === etiqueta);

describe("Backoffice — lista con el conteo de usuarios y la suspensión (contrato 1.1.0)", () => {
  it("la columna «Usuarios» (x-flit-etiquetas de EmpresaBackoffice) va tras el NIT con el conteo, incluido el 0", async () => {
    render(<EmpresasAprovisionamiento />);
    await screen.findByText("Compañía Uno");

    expect(screen.getAllByRole("columnheader").map((th) => th.textContent)).toEqual(["Compañía", "NIT", "Usuarios", "Estado", "Motivo", "Acción"]);
    expect(celda(fila("Compañía Uno"), "Usuarios")?.textContent).toBe("12");
    expect(celda(fila("Compañía Tres"), "Usuarios")?.textContent).toBe("0");
  });

  it("sin `usuarios` la celda queda vacía (sin «undefined» ni un 0 inventado)", async () => {
    render(<EmpresasAprovisionamiento />);
    await screen.findByText("Compañía Dos");

    const c = celda(fila("Compañía Dos"), "Usuarios");
    expect(c).toBeDefined();
    expect(c?.textContent?.trim()).toBe("");
    expect(fila("Compañía Dos")).not.toHaveTextContent("undefined");
  });

  it("`suspendida` se marca con el texto «Suspendido» junto al estado; sin duplicarlo si el estado ya es SUSPENDIDO", async () => {
    render(<EmpresasAprovisionamiento />);
    await screen.findByText("Compañía Uno");

    expect(within(fila("Compañía Uno")).queryByText("Suspendido")).toBeNull();
    const dos = within(celda(fila("Compañía Dos"), "Estado")!);
    expect(dos.getByText("Listo")).toBeInTheDocument();
    expect(dos.getByText("Suspendido")).toBeInTheDocument();
    expect(within(celda(fila("Compañía Tres"), "Estado")!).getAllByText("Suspendido")).toHaveLength(1);
  });
});

describe("Backoffice — motivos del contrato 1.2.0 en la lista (alta idempotente, plan §5.8)", () => {
  const conMotivos = (...motivos: Array<[string, string, string | null]>) =>
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        Response.json({
          items: motivos.map(([id, estado, motivo]) => ({
            id,
            nombre: `Compañía ${id}`,
            nit: "900000001",
            aprovisionamiento: { estado, motivo, accion: estado === "ERROR" || estado === "LISTO_SIN_WEBHOOK" ? "REINTENTAR" : null, firmaConfirmada: true },
            suspendida: false,
          })),
          total: motivos.length,
          page: 1,
          pageSize: 50,
        }),
      ),
    );

  it("EMPRESA_INACTIVA_EN_PROVEEDOR muestra su literal de x-flit-etiquetas, nunca el código ni «Sin clasificar»", async () => {
    conMotivos(["i-1", "ERROR", "EMPRESA_INACTIVA_EN_PROVEEDOR"]);
    render(<EmpresasAprovisionamiento />);
    await screen.findByText("Compañía i-1");

    const motivo = celda(fila("Compañía i-1"), "Motivo")!;
    expect(motivo).toHaveTextContent("El proveedor tiene la compañía desactivada: pide su reactivación y luego reintenta.");
    expect(motivo).not.toHaveTextContent("EMPRESA_INACTIVA_EN_PROVEEDOR");
    expect(motivo).not.toHaveTextContent("Sin clasificar");
  });

  it("CLAVE_ROTADA y los retirados (EMPRESA_YA_REGISTRADA, ALTA_SIN_CONFIRMAR) conservan su literal", async () => {
    conMotivos(["r-1", "LISTO", "CLAVE_ROTADA"], ["r-2", "REQUIERE_CREDENCIALES", "EMPRESA_YA_REGISTRADA"], ["r-3", "INCIERTO", "ALTA_SIN_CONFIRMAR"]);
    render(<EmpresasAprovisionamiento />);
    await screen.findByText("Compañía r-1");

    expect(celda(fila("Compañía r-1"), "Motivo")).toHaveTextContent("El proveedor reemplazó la API key de la compañía; la nueva quedó guardada.");
    expect(celda(fila("Compañía r-2"), "Motivo")).toHaveTextContent(
      "La empresa ya existe en el proveedor: rota las credenciales en la consola del API y cárgalas en Impuestos.",
    );
    expect(celda(fila("Compañía r-3"), "Motivo")).toHaveTextContent("El proveedor no confirmó el alta: puede haberla recibido. Reintenta para verificar.");
  });
});
