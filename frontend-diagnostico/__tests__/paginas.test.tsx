import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import Inicio from "@/app/(app)/page";
import Forbidden from "@/app/403/page";
import NotFound from "@/app/not-found";

// Uso de ejemplo: render(await Forbidden({ searchParams: Promise.resolve({ code: "PRODUCTO_NO_HABILITADO" }) }))

vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
  useRouter: () => ({ replace: vi.fn(), push: vi.fn(), refresh: vi.fn() }),
}));

const RAIZ = resolve(__dirname, "..");

function fuentes(dir: string): string[] {
  return readdirSync(dir).flatMap((nombre) => {
    const ruta = join(dir, nombre);
    if (statSync(ruta).isDirectory()) return fuentes(ruta);
    return /\.(tsx?|jsx?)$/.test(nombre) ? [ruta] : [];
  });
}

const sesion = {
  usuario: { id: "00000000-0000-7000-8000-000000000001", email: "ana@empresa-prueba.co", esSuperAdmin: false },
  empresa: { id: "00000000-0000-7000-8000-0000000000e1", nombre: "Transportes Prueba SAS", tipo: "COMPANY" },
  roles: [],
  permisos: ["diagnostico.diagnosticos.ver"],
  capacidades: {
    crearDiagnosticos: false, verDiagnosticos: true, descargarReportes: false, derivar: false, verConfiguracion: false,
    editarConfiguracion: false, reprocesarConsultas: false, backoffice: false, editarContrato: false, llenarConsultas: false,
    corregirDictamen: false, verAuditoria: false, verVolumetria: false,
  },
};

/** GET /sesion responde la sesión y GET /diagnosticos (listado del inicio, HU #13852) una lista vacía. */
const apiDePrueba = () =>
  vi.fn(async (entrada: string) =>
    String(entrada).includes("/diagnosticos") ? Response.json({ pagina: 1, tamano: 25, total: 0, diagnosticos: [] }) : Response.json(sesion),
  );

beforeEach(() => vi.stubEnv("FLIT_HUB_URL", "https://dev.flitsas.online"));
afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

describe("sin formulario propio", () => {
  it("ninguna página ni componente de la app declara campos de usuario o contraseña", () => {
    const archivos = [...fuentes(join(RAIZ, "app")), ...fuentes(join(RAIZ, "components"))];
    expect(archivos.length).toBeGreaterThan(5);
    for (const archivo of archivos) {
      const codigo = readFileSync(archivo, "utf8");
      expect(codigo, archivo).not.toMatch(/type=["']password["']/);
      // Los campos de la pantalla 10 (HU #13744) y del formulario de parametrización (HU #13752) son de configuración y de
      // la cabecera del diagnóstico, y los de la carga masiva (HU #13754) son el archivo y las filas de vehículos, no de
      // credenciales; los de la captura express (HU #13755) son la placa o el VIN y el documento declarado del locatario, y
      // los del backoffice (HU #13802) son el identificador de la compañía y el resultado del llenado manual, y los del
      // listado del inicio (HU #13852) son los filtros de fecha, tipo, estado y nombre. El resto de la app sigue sin ningún
      // <input>.
      const conCampos = [
        join("components", "configuracion"),
        join("components", "parametrizacion"),
        join("components", "carga"),
        join("components", "express"),
        join("components", "backoffice"),
        join("components", "listado"),
      ];
      if (!conCampos.some((carpeta) => archivo.includes(carpeta))) expect(codigo, archivo).not.toMatch(/<input\b/);
      expect(codigo, archivo).not.toMatch(/autoComplete=["'](username|current-password)["']/);
    }
  });

  it("el home no tiene campos de credenciales (solo los filtros del listado) y la 403 y la not-found ninguno", async () => {
    vi.stubGlobal("fetch", apiDePrueba());
    const { container, unmount } = render(
      <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
        <Inicio />
      </DiagnosticoShell>,
    );
    await screen.findByRole("heading", { level: 1, name: "Diagnóstico vehicular" });
    // Desde HU #13852 el inicio lleva los filtros del listado; ninguno es de usuario o contraseña.
    expect(container.querySelectorAll('input[type="password"], input[autocomplete="username"], textarea')).toHaveLength(0);
    unmount();

    for (const pagina of [await Forbidden({ searchParams: Promise.resolve({}) }), <NotFound key="nf" />]) {
      const r = render(pagina);
      expect(r.container.querySelectorAll("input, textarea, select")).toHaveLength(0);
      r.unmount();
    }
  });
});

describe("suscripción suspendida en la interfaz", () => {
  it("la 403 con PRODUCTO_NO_HABILITADO explica que Diagnóstico no está habilitado y ofrece volver al hub", async () => {
    render(await Forbidden({ searchParams: Promise.resolve({ code: "PRODUCTO_NO_HABILITADO" }) }));

    expect(screen.getByRole("heading", { level: 1, name: "No tienes acceso" })).toBeInTheDocument();
    expect(screen.getByText(/Diagnóstico no está habilitado para tu empresa/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ir a mis productos" })).toHaveAttribute("href", "https://dev.flitsas.online/?inicio=1");
    expect(screen.queryByText("Diagnóstico vehicular")).toBeNull();
  });

  it("la 403 sin permisos de Diagnóstico pide el acceso al administrador", async () => {
    render(await Forbidden({ searchParams: Promise.resolve({ code: "SIN_PERMISOS_DIAGNOSTICO" }) }));

    expect(screen.getByText(/No tienes permisos en Diagnóstico/)).toBeInTheDocument();
  });
});

describe("pantalla base con los paquetes de la suite", () => {
  it("el home muestra la tarjeta título con la compañía de la sesión y el estado vacío", async () => {
    // Desde HU #13852 el estado vacío lo pinta el listado cuando la compañía no tiene diagnósticos.
    vi.stubGlobal("fetch", apiDePrueba());
    render(
      <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
        <Inicio />
      </DiagnosticoShell>,
    );

    const titulo = await screen.findByRole("heading", { level: 1, name: "Diagnóstico vehicular" });
    expect(titulo.closest("section")).toHaveTextContent("Transportes Prueba SAS");
    expect(await screen.findByText("Todavía no hay diagnósticos")).toBeInTheDocument();
  });

  it("el CSS global importa los tokens, el shell y el loader de @flit/ui", () => {
    const css = readFileSync(join(RAIZ, "app", "globals.css"), "utf8");
    expect(css).toContain('@import "@flit/ui/tokens.css"');
    expect(css).toContain('@import "@flit/ui/loader.css"');
    expect(css).toContain('@import "@flit/shell/shell.css"');
  });
});
