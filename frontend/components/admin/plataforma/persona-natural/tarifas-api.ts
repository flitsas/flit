// Cliente de API inyectable del módulo de Tarifas. Plataforma usa el valor por defecto (sin cambios); el hub del OT
// inyecta el suyo (`lib/api/admin-ot-portal-tarifas.ts`). Vive aquí, y no en el cliente, para que las pruebas que
// sustituyen las funciones de `admin-pn-tarifas` y `admin-pn-config` sigan interceptando las llamadas.
import {
  fetchHistorialTarifaPn,
  fetchTarifasPn,
  registrarTarifasPn,
  type ConceptoTarifa,
  type NuevasTarifas,
  type TarifaPn,
  type TarifasFiltro,
} from "@/lib/api/admin-pn-tarifas";
import { fetchOrganismosPn, fetchTiposPn, type OrganismoPn } from "@/lib/api/admin-pn-config";

/** Lo que el formulario necesita de un tipo de trámite. */
export interface TipoTarifa {
  id: string;
  nombre: string;
}

export interface TarifasApi {
  listar(filtro: TarifasFiltro, signal?: AbortSignal): Promise<TarifaPn[]>;
  historial(id: string, signal?: AbortSignal): Promise<TarifaPn[]>;
  registrar(body: NuevasTarifas): Promise<TarifaPn[]>;
  catalogos(signal?: AbortSignal): Promise<{ tipos: TipoTarifa[]; organismos: OrganismoPn[] }>;
}

export const TARIFAS_API_PLATAFORMA: TarifasApi = {
  listar: (filtro, signal) => fetchTarifasPn(filtro, signal),
  historial: (id, signal) => fetchHistorialTarifaPn(id, signal),
  registrar: (body) => registrarTarifasPn(body),
  catalogos: async (signal) => {
    const [tipos, organismos] = await Promise.all([
      fetchTiposPn(signal),
      fetchOrganismosPn(signal),
    ]);
    return { tipos, organismos };
  },
};

/** Cómo se presenta y qué permite el módulo según quién lo usa. Por defecto: Plataforma, sin restricciones. */
export interface TarifasModo {
  /** Hub del OT: una sola organización, sin selector ni columna de organismo, y con «Origen» (propio o general). */
  organismoFijo?: boolean;
  /** Conceptos que el formulario muestra sin poder activarse (p. ej. servicio FLIT y envío para el `ot_admin`). */
  conceptosBloqueados?: readonly ConceptoTarifa[];
  titulo?: string;
  descripcion?: string;
  /** Sin círculo de paso (solo la configuración de Plataforma está numerada). */
  sinPaso?: boolean;
}
