import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { AccesoBackoffice } from "@/components/backoffice/AccesoBackoffice";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import { sesion } from "@/__tests__/configuracion/datos";
import type { CapacidadesDiagnostico } from "@/lib/sesion";

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

const enlacesBackoffice = () => screen.queryAllByRole("link").filter((a) => a.getAttribute("href") === "/backoffice/diagnosticos");

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
