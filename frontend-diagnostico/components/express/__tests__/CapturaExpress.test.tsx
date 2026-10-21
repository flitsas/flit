import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { CapturaExpress } from "@/components/express/CapturaExpress";
import { DiagnosticoShell } from "@/components/DiagnosticoShell";
import Inicio from "@/app/(app)/page";
import { sesion } from "@/__tests__/configuracion/datos";
import { FECHA, ID_EXPRESS, configuracion, creadoOk, cuerpoDe, llamadas, simularApi, tiposOk } from "./datos";

// Uso de ejemplo: <CapturaExpress /> en el home: el usuario escribe «abc 123», ve «ABC123», pulsa Diagnosticar, ajusta
// alcance y modo en la parametrización compacta y, con el 201 de POST /express, llega a /diagnosticos/{id}.

const router = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => router }));

beforeEach(() => {
  router.push.mockReset();
  router.replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

type Rutas = Parameters<typeof simularApi>[0];

function montar(rutas: Rutas = {}) {
  const api = simularApi({
    "GET /configuracion": () => Response.json(configuracion()),
    "GET /tipos-tramite": tiposOk,
    "POST /express": creadoOk,
    ...rutas,
  });
  render(<CapturaExpress fecha={FECHA} />);
  return api;
}

const campo = () => screen.getByRole("textbox", { name: /placa o vin/i });
const escribir = (valor: string) => fireEvent.change(campo(), { target: { value: valor } });
const diagnosticar = () => fireEvent.click(screen.getByRole("button", { name: "Diagnosticar" }));

describe("CapturaExpress — normalización en pantalla (AC 2)", () => {
  it("«abc 123» se muestra como ABC123", () => {
    montar();
    escribir("abc 123");
    expect(campo()).toHaveValue("ABC123");
  });

  it("no recorta: una entrada larga se ve completa para poder corregirla", () => {
    montar();
    escribir("ab 1234 5678");
    expect(campo()).toHaveValue("AB12345678");
  });
});

describe("CapturaExpress — identificador inválido (AC 3)", () => {
  it("vacío: pide la placa o el VIN y no abre el panel", () => {
    const api = montar();
    diagnosticar();
    expect(campo()).toHaveAttribute("aria-invalid", "true");
    expect(campo()).toHaveAccessibleDescription(/escribe la placa o el vin/i);
    expect(screen.queryByRole("form", { name: /parametrización del diagnóstico express/i })).not.toBeInTheDocument();
    expect(llamadas(api, "GET", "/configuracion")).toHaveLength(0);
  });

  it("ni placa ni VIN: muestra el error en el campo y no crea nada", () => {
    const api = montar();
    escribir("12");
    diagnosticar();
    expect(campo()).toHaveAttribute("aria-invalid", "true");
    expect(campo()).toHaveAccessibleDescription(/no es una placa ni un vin/i);
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });

  it("placa de más de 8 caracteres: EXCEDE_LONGITUD, sin recortar", () => {
    montar();
    escribir("AB12345678");
    diagnosticar();
    expect(campo()).toHaveAccessibleDescription(/la placa tiene 10 caracteres y el máximo es 8/i);
  });

  it("el 422 de la API se muestra en el campo y no navega", async () => {
    montar({
      "POST /express": () =>
        Response.json(
          { code: "IDENTIFICADOR_DEMASIADO_LARGO", error: "El identificador es demasiado largo.", campo: "identificador" },
          { status: 422 },
        ),
    });
    escribir("ABC123");
    diagnosticar();
    fireEvent.click(await screen.findByRole("button", { name: "Continuar" }));
    await waitFor(() => expect(campo()).toHaveAccessibleDescription("El identificador es demasiado largo."));
    expect(campo()).toHaveAttribute("aria-invalid", "true");
    expect(router.push).not.toHaveBeenCalled();
  });

  it("al editar el identificador se cierra el panel (el nombre autogenerado dependía de él)", async () => {
    montar();
    escribir("ABC123");
    diagnosticar();
    await screen.findByRole("button", { name: "Continuar" });
    escribir("ABC124");
    expect(screen.queryByRole("button", { name: "Continuar" })).not.toBeInTheDocument();
  });
});

describe("CapturaExpress — Express de punta a punta (AC 1)", () => {
  it("escribe ABC123, ajusta alcance y modo, lanza y llega a /diagnosticos/{id}", async () => {
    const api = montar();
    escribir("abc123");
    diagnosticar();
    expect(await screen.findByRole("textbox", { name: "Nombre del diagnóstico" })).toHaveValue("Express ABC123 21/10/2026");
    fireEvent.click(screen.getByRole("checkbox", { name: "SIMIT vehículo" }));
    fireEvent.click(screen.getByRole("radio", { name: "Con Análisis IA" }));
    fireEvent.change(await screen.findByRole("combobox", { name: /tipo de trámite/i }), { target: { value: "TRASPASO_STANDARD" } });
    fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

    await waitFor(() => expect(router.push).toHaveBeenCalledWith(`/diagnosticos/${ID_EXPRESS}`));
    expect(cuerpoDe(llamadas(api, "POST", "/express")[0])).toEqual({
      nombre: "Express ABC123 21/10/2026",
      modo: "CON_ANALISIS",
      tipo_tramite_codigo: "TRASPASO_STANDARD",
      alcance: { por_vehiculo: ["RUNT", "SIMIT_VEHICULO"], por_propietario: [], por_locatario: [] },
      identificador: "ABC123",
      tipo_documento_locatario: null,
      documento_locatario: null,
    });
  });
});

describe("Home — bloque Express", () => {
  function montarHome(crearDiagnosticos: boolean) {
    simularApi({ "GET /sesion": () => Response.json(sesion({ crearDiagnosticos, verDiagnosticos: true })) });
    render(
      <DiagnosticoShell user={{ email: "ana@empresa-prueba.co", permissions: [], roles: [], isSuperAdmin: false }} tramitesUrl="">
        <Inicio />
      </DiagnosticoShell>,
    );
  }

  it("con permiso de crear muestra la captura Express en el home", async () => {
    montarHome(true);
    expect(await screen.findByRole("heading", { name: "Diagnóstico Express" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: /placa o vin/i })).toBeInTheDocument();
  });

  it("sin permiso de crear no la muestra", async () => {
    montarHome(false);
    await screen.findByRole("heading", { level: 1, name: "Diagnóstico vehicular" });
    expect(screen.queryByRole("heading", { name: "Diagnóstico Express" })).not.toBeInTheDocument();
  });
});
