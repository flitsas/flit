import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { crearCliente } from "@/lib/api/cliente";

type Respuesta = Response | Promise<Response>;

export function json(cuerpo: unknown, estado = 200): Response {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { "content-type": "application/json" } });
}

/** fetch falso por ruta: devuelve un 404 para lo que no se haya simulado. */
export function fetchFalso(rutas: Record<string, () => Respuesta>) {
  const llamadas: string[] = [];
  const fetchImpl = (async (url: string) => {
    const ruta = String(url).replace("/api/v1/persona-natural", "");
    llamadas.push(ruta);
    return (rutas[ruta] ?? (() => json({ code: "no_simulado" }, 404)))();
  }) as unknown as typeof fetch;
  return { cliente: crearCliente({ fetchImpl }), llamadas };
}

export function renderConQuery(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

export const ORGANISMOS = [
  { id: "o-1", nombre: "Ciudad Uno" },
  { id: "o-2", nombre: "Villa Dos" },
  { id: "o-3", nombre: "Ñandú del Sur" },
];

export const TIPOS = [
  { code: "TIPO_A", name: "Trámite A", family: "fam-a", version: 1, disponible: true },
  { code: "TIPO_B", name: "Trámite B", family: "fam-b", version: 1, disponible: false, motivoNoDisponible: "sin tarifa vigente" },
  { code: "TIPO_C", name: "Trámite C", family: "fam-c", version: 2, disponible: true },
];
