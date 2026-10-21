import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import Inicio from "@/app/(app)/page";
import Forbidden from "@/app/403/page";
import NotFound from "@/app/not-found";

// Uso de ejemplo: render(await Forbidden({ searchParams: Promise.resolve({ code: "PRODUCTO_NO_HABILITADO" }) }))

vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => ({ replace: vi.fn(), push: vi.fn(), refresh: vi.fn() }) }));

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
      // Los campos de la pantalla 10 (HU #13744) son de configuración, no de credenciales: el resto de la app sigue sin
      // ningún <input>.
      if (!archivo.includes(join("components", "configuracion"))) expect(codigo, archivo).not.toMatch(/<input\b/);
      expect(codigo, archivo).not.toMatch(/autoComplete=["'](username|current-password)["']/);
    }
  });

  it("el home, la 403 y la not-found se renderizan sin ningún campo", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(sesion)));
    const { container, unmount } = render(
      <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
        <Inicio />
      </DiagnosticoShell>,
    );
    await screen.findByRole("heading", { level: 1, name: "Diagnóstico vehicular" });
    expect(container.querySelectorAll("input, textarea, select")).toHaveLength(0);
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
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(sesion)));
    render(
      <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
        <Inicio />
      </DiagnosticoShell>,
    );

    const titulo = await screen.findByRole("heading", { level: 1, name: "Diagnóstico vehicular" });
    expect(titulo.closest("section")).toHaveTextContent("Transportes Prueba SAS");
    expect(screen.getByText("Todavía no hay diagnósticos")).toBeInTheDocument();
  });

  it("el CSS global importa los tokens, el shell y el loader de @flit/ui", () => {
    const css = readFileSync(join(RAIZ, "app", "globals.css"), "utf8");
    expect(css).toContain('@import "@flit/ui/tokens.css"');
    expect(css).toContain('@import "@flit/ui/loader.css"');
    expect(css).toContain('@import "@flit/shell/shell.css"');
  });
});
