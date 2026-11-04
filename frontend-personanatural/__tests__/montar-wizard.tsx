import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import type { RegistroRenderers } from "@/components/tramite/ContextoWizard";
import { PaginaTramite } from "@/components/tramite/PaginaTramite";
import { REGISTRO_RENDERERS } from "@/components/tramite/renderers/registro";
import { crearCliente, type DefinicionTipo, type Diagnostico, type VehiculoMinimo } from "@/lib/api/cliente";
import { fijarContexto } from "@/lib/tramite/contexto";
import { definicionDe, solicitudDe } from "./fixtures-wizard";
import { json, TIPOS } from "./utilidades";

// Arnés compartido de las pruebas del wizard: un fetch falso por ruta, el contexto que deja la landing y los datos de prueba.
export type Manejador = (init: RequestInit) => Response | Promise<Response>;

export const DEFINICION: DefinicionTipo = definicionDe([["vehicle_query"], ["actor_form"], ["document_checklist"]]);
export const VEHICULO: VehiculoMinimo = { existe: true, placa: "ABC123", clase: "CAMIONETA", marca: "MARCA X", linea: "LINEA Y", modeloAno: "2022", tipoServicio: "PARTICULAR", estado: "REGISTRADO", tienePrenda: false, tieneLimitacion: false, tieneRestricciones: false };
export const DIAGNOSTICO_OK: Diagnostico = {
  puedeContinuar: true,
  requisitos: [
    { codigo: "soat", descripcion: "SOAT", estado: "cumple" },
    { codigo: "rtm", descripcion: "RTM", estado: "cumple" },
    { codigo: "multas", descripcion: "Multas asociadas", estado: "no_aplica" },
  ],
};
export const DIAGNOSTICO_BLOQUEADO: Diagnostico = { puedeContinuar: false, requisitos: [{ codigo: "soat", descripcion: "SOAT", estado: "falta" }] };
export const guardado = (d: Diagnostico) => solicitudDe({ datos: { diagnostico: { ...d, fecha: "2026-10-28T20:23:00Z" } }, pendientes: [{ codigo: "actor_requerido", referencia: "vendedor", paso: "p2" }], version: 4 });

export const CONTEXTO = { intencion: "iniciar" as const, tipoCodigo: "TIPO_A", solicitudId: "s-1", radicado: "PN-2026-0042", organismoId: "o-1", cedula: "1020456789", placa: "ABC123" };

export function montar(rutas: Record<string, Manejador> = {}, { registro = REGISTRO_RENDERERS, tipo = "TIPO_A" }: { registro?: RegistroRenderers; tipo?: string } = {}) {
  const todas: Record<string, Manejador> = {
    "GET /tipos-tramite": () => json(TIPOS),
    "GET /solicitudes/s-1": () => json(solicitudDe()),
    "GET /tipos-tramite/TIPO_A/definicion?organismoId=o-1": () => json(DEFINICION),
    "POST /cotizaciones": () => json({ lineas: [], totalCop: 0, versionTarifa: "v1", adicionalesNoAplicados: [] }),
    ...rutas,
  };
  const llamadas: { clave: string; init: RequestInit }[] = [];
  const fetchImpl = (async (url: string, init: RequestInit) => {
    const clave = `${init.method} ${String(url).replace("/api/v1/persona-natural", "")}`;
    llamadas.push({ clave, init });
    return (todas[clave] ?? (() => json({ code: "no_simulado" }, 404)))(init);
  }) as unknown as typeof fetch;
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <PaginaTramite tipoCodigo={tipo} cliente={crearCliente({ fetchImpl })} registro={registro} />
    </QueryClientProvider>,
  );
  return { llamadas, client };
}

export const cuerpo = (llamadas: { clave: string; init: RequestInit }[], clave: string) => JSON.parse(String(llamadas.find((l) => l.clave === clave)?.init.body));
export const nodo = (n: number) => screen.getByRole("button", { name: new RegExp(`Paso ${n} ·`) });

export async function wizardAbierto(rutas: Record<string, Manejador> = {}, opciones?: Parameters<typeof montar>[1]) {
  fijarContexto(CONTEXTO);
  const m = montar(rutas, opciones);
  await screen.findByRole("heading", { level: 1, name: "Trámite de Trámite A" });
  return m;
}

/** Resuelve el captcha del modo de desarrollo del paso activo (ya no hay casilla: el token se entrega solo). */
export const resolverCaptcha = () => {};
