import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { AccesoBackoffice } from "@/components/backoffice/AccesoBackoffice";
import { GrillaBackoffice } from "@/components/backoffice/GrillaBackoffice";
import { DiagnosticoShell, useSesion } from "@/components/DiagnosticoShell";
import { sesion } from "@/__tests__/configuracion/datos";
import { EMPRESA_A, LOTE, grillaBo, simularApi } from "@/lib/backoffice/__tests__/datos";
import type { CapacidadesDiagnostico } from "@/lib/sesion";

// Uso de ejemplo (Punto 1 de la ola 2026-11-11): GET /sesion trae `capacidades.backoffice` (SuperAdmin de cualquier compañía o
// usuario FLIT con diagnostico.backoffice.ver) y `capacidades.backofficeOperacion` (solo FLIT). Con la primera se ve el dock
// «Backoffice» y sus pantallas; el override del dictamen y el llenado manual exigen además la segunda. Si el back no la manda,
// cuenta como false.

const h = vi.hoisted(() => ({ replace: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => ({ replace: h.replace, push: vi.fn(), refresh: vi.fn() }) }));

beforeEach(() => h.replace.mockReset());
afterEach(() => vi.unstubAllGlobals());

const RUTA_GRILLA = `GET /backoffice/diagnosticos/${LOTE}/grilla`;
const RUTA_DICTAMEN = `GET /backoffice/diagnosticos/${LOTE}/vehiculos/vehiculo-1/dictamen`;
const DICTAMEN = {
  version: 1,
  veredicto: "BLOQUEADO",
  estado_redaccion: "PENDIENTE",
  alertamientos: [{ codigo: "SOAT_NO_VIGENTE", texto: "El SOAT no está vigente.", efecto: "BLOQUEA", fuente: "RUNT" }],
  catalogo_version: 1,
  creado_en: "2026-11-03T15:00:00+00:00",
};

/** SuperAdmin de una compañía cliente: el back le da las capacidades de detalle, pero no la de operación FLIT. */
const SUPERADMIN_NO_FLIT: Partial<CapacidadesDiagnostico> = {
  backoffice: true,
  backofficeOperacion: false,
  llenarConsultas: true,
  corregirDictamen: true,
  editarContrato: true,
};
/** Back anterior a P1: sin la clave backofficeOperacion. */
const SIN_CLAVE_OPERACION: Partial<CapacidadesDiagnostico> = { backoffice: true, llenarConsultas: true, corregirDictamen: true };
const FLIT: Partial<CapacidadesDiagnostico> = { ...SUPERADMIN_NO_FLIT, backofficeOperacion: true };

function sesionDe(capacidades: Partial<CapacidadesDiagnostico>, tipo: string) {
  const s = sesion(capacidades, tipo);
  if (!("backofficeOperacion" in capacidades)) delete (s.capacidades as Partial<CapacidadesDiagnostico>).backofficeOperacion;
  return s;
}

/** Shell real con su GET /sesion; dentro, la grilla del backoffice (o una pantalla de prueba). */
function montar(capacidades: Partial<CapacidadesDiagnostico>, tipo: string, pantalla: "grilla" | "texto" = "grilla") {
  simularApi({
    "GET /sesion": () => Response.json(sesionDe(capacidades, tipo)),
    [RUTA_GRILLA]: () => Response.json(grillaBo()),
    [RUTA_DICTAMEN]: () => Response.json(DICTAMEN),
  });
  return render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: true }} tramitesUrl="">
      <AccesoBackoffice>{pantalla === "grilla" ? <GrillaBackoffice diagnosticoId={LOTE} empresa={EMPRESA_A} /> : <p>pantalla de backoffice</p>}</AccesoBackoffice>
    </DiagnosticoShell>,
  );
}

const tabla = () => screen.findByRole("table", { name: /semáforo de consultas/i });
/** Entrada «Backoffice» del dock: con dos módulos (diagnósticos y fuentes obligatorias) es la píldora que abre su menú. */
const enlacesBackoffice = () => screen.queryAllByRole("button", { name: "Backoffice" });
const botonesLlenar = () => screen.queryAllByRole("button", { name: /^Llenar a mano/ });

async function abrirDictamen() {
  fireEvent.click(screen.getByRole("button", { name: "Ver dictamen de TST001" }));
  expect(await screen.findByText("El SOAT no está vigente.")).toBeInTheDocument();
}

describe("P1 AC1 — SuperAdmin de una compañía no FLIT entra al backoffice", () => {
  it("ve la entrada «Backoffice» en el dock y la pantalla, sin ir a la 403", async () => {
    montar(SUPERADMIN_NO_FLIT, "COMPANY", "texto");
    expect(await screen.findByText("pantalla de backoffice")).toBeInTheDocument();
    expect(enlacesBackoffice().length).toBeGreaterThan(0);
    expect(h.replace).not.toHaveBeenCalled();
  });

  it("abre la grilla de cualquier compañía (?empresa=)", async () => {
    montar(SUPERADMIN_NO_FLIT, "COMPANY");
    expect(await tabla()).toBeInTheDocument();
  });
});

describe("P1 AC3 — sin backofficeOperacion no hay override del dictamen ni llenado manual", () => {
  it("SuperAdmin no FLIT: ninguna celda ofrece «Llenar a mano»", async () => {
    montar(SUPERADMIN_NO_FLIT, "COMPANY");
    await tabla();
    expect(botonesLlenar()).toHaveLength(0);
  });

  it("SuperAdmin no FLIT: el dictamen se lee, pero sin «Nuevo veredicto» ni «Reemplazar veredicto»", async () => {
    montar(SUPERADMIN_NO_FLIT, "COMPANY");
    await tabla();
    await abrirDictamen();
    expect(screen.queryByRole("group", { name: "Nuevo veredicto" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Reemplazar veredicto" })).toBeNull();
  });

  it("back sin la clave backofficeOperacion: se trata como false (ni llenado ni override)", async () => {
    montar(SIN_CLAVE_OPERACION, "COMPANY");
    await tabla();
    expect(botonesLlenar()).toHaveLength(0);
    await abrirDictamen();
    expect(screen.queryByRole("button", { name: "Reemplazar veredicto" })).toBeNull();
  });
});

describe("P1 AC5 — FLIT conserva todo", () => {
  it("con backofficeOperacion: «Llenar a mano» en las celdas llenables y override del dictamen", async () => {
    montar(FLIT, "FLIT");
    await tabla();
    expect(botonesLlenar().length).toBeGreaterThan(0);
    await abrirDictamen();
    expect(screen.getByRole("group", { name: "Nuevo veredicto" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reemplazar veredicto" })).toBeInTheDocument();
  });
});

describe("P1 AC4 — sin la capacidad backoffice no se ve el backoffice", () => {
  it("sin entrada en el dock y la ruta lleva a la 403", async () => {
    montar({ verDiagnosticos: true, backofficeOperacion: false }, "COMPANY", "texto");
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith(expect.stringMatching(/^\/403\?code=SIN_PERMISO_BACKOFFICE/)));
    expect(screen.queryByText("pantalla de backoffice")).toBeNull();
    expect(enlacesBackoffice()).toHaveLength(0);
  });
});

describe("P1 — contrato de la sesión", () => {
  it("useSesion expone backofficeOperacion tal como llega de GET /sesion", async () => {
    function Sonda() {
      const { sesion: s } = useSesion();
      return <p>operacion:{String(s?.capacidades.backofficeOperacion)}</p>;
    }
    simularApi({ "GET /sesion": () => Response.json(sesionDe(FLIT, "FLIT")) });
    render(
      <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: true }} tramitesUrl="">
        <Sonda />
      </DiagnosticoShell>,
    );
    expect(await screen.findByText("operacion:true")).toBeInTheDocument();
  });
});
