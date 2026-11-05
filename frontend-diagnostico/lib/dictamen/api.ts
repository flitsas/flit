// Lectura y reemplazo del dictamen vigente (corte 7). La compañía lee
// GET /diagnosticos/{id}/vehiculos/{vid}/dictamen. El backoffice lee y reemplaza en
// /backoffice/diagnosticos/{id}/vehiculos/{vid}/dictamen?empresa=. Sin compañía elegida no sale la llamada.
import { ApiError, apiFetch } from "@/lib/api";
import { EmpresaNoElegida } from "@/lib/backoffice/api";
import { esEmpresaValida } from "@/lib/configuracion";

export interface AlertaDictamen {
  codigo: string;
  texto: string;
  efecto: string;
  fuente: string;
}

export interface Dictamen {
  version: number;
  veredicto: "VIABLE" | "BLOQUEADO";
  estado_redaccion: "PENDIENTE" | "COMPLETADA";
  alertamientos: AlertaDictamen[];
  catalogo_version: number;
  creado_en: string;
}

export interface DictamenBackoffice extends Dictamen {
  analisis_override: boolean;
  usuario_override_id: string | null;
  override_en: string | null;
}

export function esSinDictamen(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404 && error.code === "SIN_DICTAMEN";
}

const seg = encodeURIComponent;

export function rutaDictamen(diagnosticoId: string, vehiculoId: string): string {
  return `/diagnosticos/${seg(diagnosticoId)}/vehiculos/${seg(vehiculoId)}/dictamen`;
}

export function rutaDictamenBackoffice(diagnosticoId: string, vehiculoId: string, empresa: string): string {
  const id = empresa.trim();
  if (!esEmpresaValida(id)) throw new EmpresaNoElegida();
  return `/backoffice/diagnosticos/${seg(diagnosticoId)}/vehiculos/${seg(vehiculoId)}/dictamen?${new URLSearchParams({ empresa: id })}`;
}

export function obtenerDictamen(diagnosticoId: string, vehiculoId: string, signal?: AbortSignal): Promise<Dictamen> {
  return apiFetch<Dictamen>(rutaDictamen(diagnosticoId, vehiculoId), { signal });
}

export function obtenerDictamenBackoffice(diagnosticoId: string, vehiculoId: string, empresa: string, signal?: AbortSignal): Promise<DictamenBackoffice> {
  return apiFetch<DictamenBackoffice>(rutaDictamenBackoffice(diagnosticoId, vehiculoId, empresa), { signal });
}

export function corregirDictamen(
  diagnosticoId: string,
  vehiculoId: string,
  empresa: string,
  veredicto: Dictamen["veredicto"],
): Promise<DictamenBackoffice> {
  return apiFetch<DictamenBackoffice>(rutaDictamenBackoffice(diagnosticoId, vehiculoId, empresa), {
    method: "PUT",
    body: { veredicto, confirmado: true },
  });
}
