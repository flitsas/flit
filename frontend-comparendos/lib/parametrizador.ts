// Lógica pura del parametrizador: armado de payloads y validaciones que la pantalla muestra antes de llamar a la API
// (la API vuelve a validar todo).
import type { ConfiguracionEmpresa, Copia } from "@/lib/tipos";

export interface CapacidadesForm {
  hasRightPetitionModule: boolean;
  allowsContravenerConsultation: boolean;
  allowsContravenerChange: boolean;
  hasOcrAccess: boolean;
  hasReportesModule: boolean;
  tipoIntegracionConductor: string;
  conjuntoPlantillas: string;
  requiereRepresentanteLegal: boolean;
}

/** El cuerpo de PUT /admin/empresas/{id}/capacidades a partir de la configuración leída. */
export function capacidadesDeConfiguracion(c: ConfiguracionEmpresa): CapacidadesForm {
  return {
    hasRightPetitionModule: c.hasRightPetitionModule,
    allowsContravenerConsultation: c.allowsContravenerConsultation,
    allowsContravenerChange: c.allowsContravenerChange,
    hasOcrAccess: c.hasOcrAccess,
    hasReportesModule: c.hasReportesModule,
    tipoIntegracionConductor: c.tipoIntegracionConductor,
    conjuntoPlantillas: c.conjuntoPlantillas,
    requiereRepresentanteLegal: c.requiereRepresentanteLegal,
  };
}

/** Los cambios de un registro de auditoría (JSON `[{campo, anterior, nuevo}]`) como texto legible. */
export function cambiosDeAuditoria(json: string): { campo: string; anterior: string; nuevo: string }[] {
  try {
    const lista = JSON.parse(json) as { campo: string; anterior: unknown; nuevo: unknown }[];
    const texto = (v: unknown) => (v === null || v === undefined || v === "" ? "—" : typeof v === "boolean" ? (v ? "sí" : "no") : String(v));
    return lista.map((c) => ({ campo: c.campo, anterior: texto(c.anterior), nuevo: texto(c.nuevo) }));
  } catch {
    return [];
  }
}

export const ESTADOS_TIPOLOGIA = [
  { valor: "EnTiempo", texto: "En tiempo" },
  { valor: "Vencido", texto: "Vencido" },
  { valor: "Caducado", texto: "Caducado" },
  { valor: "Prescrito", texto: "Prescrito" },
];

export const ADJUNTOS_TIPOLOGIA = [
  { valor: "Cedula", texto: "Cédula del representante" },
  { valor: "CertificadoRepresentacion", texto: "Certificado de representación" },
  { valor: "EscrituraPoder", texto: "Escritura o poder" },
];

export interface TipologiaForm {
  codigo: string;
  nombre: string;
  descripcionJuridica: string;
  prefijoRadicado: string;
  estadosLegalesPermitidos: string[];
  permiteMasivo: boolean;
  plazoRespuestaOtDiasHabiles: number;
  adjuntosRequeridos: string[];
  activo: boolean;
}

/** Los mismos límites que la API (TipologiasEndpoints.Validar), para mostrarlos antes de enviar. */
export function erroresTipologia(t: TipologiaForm): string[] {
  const errores: string[] = [];
  if (!/^[A-Za-z0-9-]{1,20}$/.test(t.codigo.trim())) errores.push("El código admite letras, números y guiones (hasta 20).");
  if (!/^[A-Za-z]{1,6}$/.test(t.prefijoRadicado.trim())) errores.push("El prefijo del radicado son de 1 a 6 letras.");
  if (!t.nombre.trim() || !t.descripcionJuridica.trim()) errores.push("El nombre y la descripción jurídica son obligatorios.");
  if (t.estadosLegalesPermitidos.length === 0) errores.push("Elige al menos un estado legal en que procede.");
  if (!Number.isInteger(t.plazoRespuestaOtDiasHabiles) || t.plazoRespuestaOtDiasHabiles < 1 || t.plazoRespuestaOtDiasHabiles > 90)
    errores.push("El plazo de respuesta del OT va de 1 a 90 días hábiles.");
  return errores;
}

/** Normaliza una lista de copias: correos en minúsculas, sin vacíos ni repetidos. */
export function limpiarCopias(copias: Copia[]): Copia[] {
  const vistas = new Set<string>();
  const salida: Copia[] = [];
  for (const c of copias) {
    const correo = c.correo.trim().toLowerCase();
    if (!correo || vistas.has(correo)) continue;
    vistas.add(correo);
    salida.push({ correo, tipos: [...new Set(c.tipos)] });
  }
  return salida;
}
