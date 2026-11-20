import type { DefinicionTipo, Solicitud } from "@/lib/api/cliente";

export function definicionDe(sectionTypes: string[][], extra: Partial<DefinicionTipo> = {}): DefinicionTipo {
  return {
    code: "TIPO_A",
    name: "Trámite A",
    family: "fam-a",
    version: 1,
    huella: "h1",
    disponible: true,
    motivoNoDisponible: "",
    pasos: sectionTypes.map((st, i) => ({ clave: `p${i + 1}`, titulo: `Sección ${i + 1}`, sectionTypes: st, campos: [] })),
    actores: [],
    documentos: [],
    flags: { entryMode: "PLATE", requiereComercial: false, requierePrenda: false, permiteTransformaciones: false, permitePrendaComplementaria: false, requiereBiometria: false, requiereSolicitudPlaca: false },
    ...extra,
  };
}

export function solicitudDe(extra: Partial<Solicitud> = {}): Solicitud {
  return {
    solicitudId: "s-1",
    codigoPublico: "PN-2026-0042",
    estado: "borrador",
    tipoTramiteCodigo: "TIPO_A",
    definicionVersion: 1,
    definicionHuella: "h1",
    version: 3,
    organismoId: "o-1",
    organismoNombre: "Ciudad Uno",
    placa: "ABC123",
    vin: null,
    fieldValues: {},
    datos: {},
    actores: [],
    adjuntos: [],
    pasos: [],
    pendientes: [],
    completo: false,
    definicionCambio: null,
    ...extra,
  };
}
