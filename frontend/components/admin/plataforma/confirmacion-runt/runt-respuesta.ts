/**
 * Lectura NEUTRA del JSON crudo del RUNT para pintarlo en secciones de negocio (patrón de
 * «Detalles técnicos» de Trazabilidad ICT), no como volcado. Espejo en TypeScript de
 * `RuntVehicleSnapshotParser` del backend: detecta Kyverum (`data.vehiculo`) o Verifik
 * (`data.informacionGeneral`) por la forma del documento.
 */
export interface RuntSolicitudVista {
  noSolicitud: string;
  fecha: string;
  estado: string;
  tramites: string;
  entidad: string;
}

export interface RuntGarantiaVista {
  acreedor: string;
  documento: string;
  fechaInscripcion: string;
}

export interface RuntRespuestaVista {
  resultado: "encontrado" | "no_encontrado" | "ilegible";
  proveedor: "kyverum" | "verifik" | "desconocido";
  mensaje: string | null;
  vehiculo: Array<{ etiqueta: string; valor: string | null }>;
  mostrarSolicitudes: string | null;
  solicitudes: RuntSolicitudVista[];
  garantias: RuntGarantiaVista[];
}

type Json = Record<string, unknown>;

function obj(v: unknown): Json | null {
  return v !== null && typeof v === "object" && !Array.isArray(v) ? (v as Json) : null;
}

function str(o: Json | null, key: string): string | null {
  const v = o?.[key];
  if (typeof v === "string") return v.trim() ? v : null;
  if (typeof v === "number") return String(v);
  if (typeof v === "boolean") return v ? "true" : "false";
  return null;
}

function arr(o: Json | null, key: string): Json[] {
  const v = o?.[key];
  return Array.isArray(v) ? v.map(obj).filter((x): x is Json => x !== null) : [];
}

export function leerRespuestaRunt(raw: unknown): RuntRespuestaVista {
  const root = obj(raw);
  const base: RuntRespuestaVista = {
    resultado: "ilegible",
    proveedor: "desconocido",
    mensaje: null,
    vehiculo: [],
    mostrarSolicitudes: null,
    solicitudes: [],
    garantias: [],
  };
  if (!root) return base;

  if (root.notFound === true || root.ok === false) {
    const hint = str(root, "providerKey");
    return {
      ...base,
      resultado: "no_encontrado",
      proveedor: hint === "verifik" ? "verifik" : hint === "kyverum_runt" || root.ok === false ? "kyverum" : "desconocido",
      mensaje: str(root, "message"),
    };
  }

  const data = obj(root.data);
  if (!data) return base;

  const kv = obj(data.vehiculo);
  if (kv) {
    return {
      resultado: "encontrado",
      proveedor: "kyverum",
      mensaje: null,
      vehiculo: [
        { etiqueta: "Placa", valor: str(kv, "placa") },
        { etiqueta: "VIN", valor: str(kv, "vin") ?? str(kv, "numChasis") },
        { etiqueta: "Estado", valor: str(kv, "estadoAutomotor") },
        { etiqueta: "Marca / línea", valor: [str(kv, "marca"), str(kv, "linea")].filter(Boolean).join(" ") || null },
        { etiqueta: "Color", valor: str(kv, "color") },
        { etiqueta: "Carrocería", valor: str(kv, "tipoCarroceria") },
        { etiqueta: "Combustible", valor: str(kv, "tipoCombustible") },
        { etiqueta: "Servicio", valor: str(kv, "tipoServicio") },
        { etiqueta: "Organismo de tránsito", valor: str(kv, "organismoTransito") },
        { etiqueta: "Prendas", valor: str(kv, "prendas") },
        { etiqueta: "Gravámenes", valor: str(kv, "gravamenes") },
      ],
      mostrarSolicitudes: str(kv, "mostrarSolicitudes"),
      solicitudes: solicitudesDe(data),
      garantias: [...garantiasDe(data, "garantias"), ...garantiasDe(data, "garantiasPrendas")],
    };
  }

  const ig = obj(data.informacionGeneral);
  if (ig) {
    return {
      resultado: "encontrado",
      proveedor: "verifik",
      mensaje: null,
      vehiculo: [
        { etiqueta: "Placa", valor: str(ig, "noPlaca") ?? str(data, "plate") },
        { etiqueta: "VIN", valor: str(ig, "noVin") ?? str(data, "vin") },
        { etiqueta: "Estado", valor: str(ig, "estadoDelVehiculo") },
        { etiqueta: "Marca / línea", valor: [str(ig, "marca"), str(ig, "linea")].filter(Boolean).join(" ") || null },
        { etiqueta: "Color", valor: str(ig, "color") },
        { etiqueta: "Carrocería", valor: str(ig, "tipoCarroceria") },
        { etiqueta: "Combustible", valor: str(ig, "tipoCombustible") },
        { etiqueta: "Servicio", valor: str(ig, "tipoServicio") },
        { etiqueta: "Organismo de tránsito", valor: str(ig, "organismoTransito") },
        { etiqueta: "Prendas", valor: str(ig, "prendas") },
        { etiqueta: "Gravámenes", valor: str(ig, "tieneGravamenes") },
      ],
      mostrarSolicitudes: str(ig, "mostrarSolicitudes"),
      solicitudes: solicitudesDe(data),
      garantias: [...garantiasDe(data, "garantiasFavorDe"), ...garantiasDe(data, "garantiasMobiliarias")],
    };
  }

  return base;
}

function solicitudesDe(data: Json): RuntSolicitudVista[] {
  return arr(data, "solicitudes").map((s) => ({
    noSolicitud: str(s, "noSolicitud") ?? "—",
    fecha: diaDe(str(s, "fechaSolicitud")),
    estado: str(s, "estado") ?? "—",
    tramites: (str(s, "tramitesRealizados") ?? "—").replace(/,\s*$/, ""),
    entidad: str(s, "entidad") ?? "—",
  }));
}

function garantiasDe(data: Json, key: string): RuntGarantiaVista[] {
  return arr(data, key).map((g) => ({
    acreedor: str(g, "acreedor") ?? "—",
    documento: [str(g, "tipoDocumentoAcreedor"), str(g, "numeroDocumentoAcreedor")].filter(Boolean).join(" ") || "—",
    fechaInscripcion: diaDe(str(g, "fechaInscripcion")),
  }));
}

/** ISO con hora (Kyverum) o dd/MM/yyyy (Verifik) → dd/MM/yyyy. Mismo criterio de DÍA que el motor. */
export function diaDe(valor: string | null): string {
  if (!valor) return "—";
  if (/^\d{2}\/\d{2}\/\d{4}$/.test(valor)) return valor;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(valor);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : valor;
}
