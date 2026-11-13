import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { AccesoBackoffice } from "@/components/backoffice/AccesoBackoffice";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import { sesion } from "@/__tests__/configuracion/datos";
import type { CapacidadesDiagnostico } from "@/lib/sesion";
import type { ReactNode } from "react";
import LotesBackoffice from "@/app/(app)/backoffice/diagnosticos/page";
import GrillaLoteBackoffice from "@/app/(app)/backoffice/diagnosticos/[id]/page";
import DetalleConsultaBackofficePagina from "@/app/(app)/backoffice/diagnosticos/[id]/vehiculos/[vehiculoId]/consultas/[fuente]/page";
import PaginaFuentesObligatorias from "@/app/(app)/backoffice/fuentes-obligatorias/page";

// Uso de ejemplo: las páginas /backoffice/* envuelven su pantalla en <AccesoBackoffice>; sin la capacidad `backoffice` de
// GET /sesion (permiso diagnostico.backoffice.ver + compañía FLIT) llevan a la página 403. DiagnosticoShell suma la entrada
// «Backoffice» al dock solo para ese perfil.

const h = vi.hoisted(() => ({ replace: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => ({ replace: h.replace, push: vi.fn(), refresh: vi.fn() }) }));

beforeEach(() => h.replace.mockReset());
afterEach(() => vi.unstubAllGlobals());

const ADMIN_COMPANIA: Partial<CapacidadesDiagnostico> = { verDiagnosticos: true, verConfiguracion: true, editarConfiguracion: true, reprocesarConsultas: true };
const BACKOFFICE: Partial<CapacidadesDiagnostico> = { backoffice: true, llenarConsultas: true, verAuditoria: true };

function montar(capacidades: Partial<CapacidadesDiagnostico>, permissions: string[], tipo = "COMPANY") {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json(sesion(capacidades, tipo))));
  return render(
    <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions, roles: [], isSuperAdmin: false }} tramitesUrl="">
      <AccesoBackoffice>
        <p>pantalla de backoffice</p>
      </AccesoBackoffice>
    </DiagnosticoShell>,
  );
}

// Con dos módulos (diagnósticos y fuentes obligatorias) la entrada del dock es la píldora «Backoffice» que abre su menú, no
// un enlace directo.
const enlacesBackoffice = () => screen.queryAllByRole("button", { name: "Backoffice" });

describe("AC4 — el admin de la compañía no ve el backoffice y la ruta muestra la 403", () => {
  it("admin de la compañía: sin entrada de backoffice en el dock y la ruta lleva a /403", async () => {
    montar(ADMIN_COMPANIA, ["diagnostico.configuracion.ver", "diagnostico.configuracion.editar", "diagnostico.diagnosticos.ver"]);
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith(expect.stringMatching(/^\/403\?code=/)));
    expect(screen.queryByText("pantalla de backoffice")).toBeNull();
    expect(enlacesBackoffice()).toHaveLength(0);
  });

  it("rol de backoffice en una compañía que no es FLIT (capacidad false aunque el token traiga el permiso): tampoco", async () => {
    montar({ verDiagnosticos: true }, ["diagnostico.backoffice.ver", "diagnostico.diagnosticos.ver"]);
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith(expect.stringMatching(/^\/403/)));
    expect(enlacesBackoffice()).toHaveLength(0);
  });

  it("backoffice FLIT: ve la entrada «Backoffice» en el dock y la pantalla", async () => {
    montar(BACKOFFICE, ["diagnostico.backoffice.ver", "diagnostico.consultas.llenar"], "FLIT");
    expect(await screen.findByText("pantalla de backoffice")).toBeInTheDocument();
    expect(enlacesBackoffice().length).toBeGreaterThan(0);
    expect(h.replace).not.toHaveBeenCalled();
  });
});

// HU #14562 AC57 (negativo): cada ruta real del backoffice (lotes, grilla, detalle de consulta y fuentes obligatorias) lleva
// a la 403 sin la capacidad `backoffice` y no pide nada del backoffice: ni lotes, ni grilla, ni el catálogo de fuentes.
const EMPRESA_RUTA = "00000000-0000-7000-8000-0000000000a1";
const RUTAS_BACKOFFICE: [string, () => Promise<ReactNode>][] = [
  ["/backoffice/diagnosticos", () => LotesBackoffice({ searchParams: Promise.resolve({ empresa: EMPRESA_RUTA }) })],
  ["/backoffice/diagnosticos/{id}", () => GrillaLoteBackoffice({ params: Promise.resolve({ id: "lote-1" }), searchParams: Promise.resolve({ empresa: EMPRESA_RUTA }) })],
  [
    "/backoffice/diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}",
    () =>
      DetalleConsultaBackofficePagina({
        params: Promise.resolve({ id: "lote-1", vehiculoId: "vehiculo-1", fuente: "RUNT" }),
        searchParams: Promise.resolve({ empresa: EMPRESA_RUTA }),
      }),
  ],
  ["/backoffice/fuentes-obligatorias", async () => PaginaFuentesObligatorias()],
];

describe("HU #14562 AC57 — sin la capacidad backoffice, cada ruta del backoffice va a la 403", () => {
  it.each(RUTAS_BACKOFFICE)("%s: 403 SIN_PERMISO_BACKOFFICE y ninguna consulta del backoffice", async (_ruta, pagina) => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json(sesion(ADMIN_COMPANIA)));
    vi.stubGlobal("fetch", fetchMock);
    render(
      <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
        {await pagina()}
      </DiagnosticoShell>,
    );
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO_BACKOFFICE"));
    const pedidas = fetchMock.mock.calls.map(([u]) => new URL(String(u), "http://localhost").pathname);
    // La sesión y la campana del shell (AC50) son de toda la app; nada del backoffice se pide.
    expect(pedidas.filter((ruta) => !ruta.endsWith("/sesion") && !ruta.endsWith("/notificaciones"))).toEqual([]);
    expect(enlacesBackoffice()).toHaveLength(0);
  });
});
