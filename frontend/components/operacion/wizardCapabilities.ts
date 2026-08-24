import type { WizardCapabilities, WizardModalidad } from '@/lib/api/types/procedure-runtime';
import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';

/**
 * Capacidades efectivas del trámite en curso (ADR-0050).
 *
 * El asistente decidía todo con `modalidad === 'traspaso'`: qué partes pedía, si mostraba datos
 * comerciales, si la prenda era una puerta, qué identificador pedía en el paso 1 y cómo se titulaba
 * la pantalla. Con dos modalidades eso funcionaba porque las dos ramas agotaban el catálogo. Con
 * veintiún tipos deja de funcionar: un blindaje entraba por la rama de matrícula y pedía un
 * comprador que no existe, sin que nada fallara.
 *
 * Ahora las declara el tipo, y viajan en el estado del wizard desde el mismo `gate_profile` que
 * gobierna los gates del backend.
 */
export interface CapacidadesEfectivas {
  /** El vehículo entra por VIN (aún sin placa) en vez de por placa. */
  entraPorVin: boolean;
  /** Hay parte vendedora: el trámite transfiere la propiedad. */
  pideVendedor: boolean;
  /** Hay parte compradora o titular. */
  pideComprador: boolean;
  /** El trámite lleva valor y fecha de venta. */
  pideValorComercial: boolean;
  /** La decisión de prenda bloquea, en vez de solo declararse. */
  prendaEsPuerta: boolean;
  /** Se valida la identidad de la parte saliente además de la entrante. */
  validaIdentidadDelVendedor: boolean;
}

/**
 * Respaldo para los expedientes cuyo estado del asistente todavía no trae capacidades: un borrador
 * abierto en el momento del despliegue, o un tipo sin parametrizar. Reproduce exactamente lo que
 * hacían las dos ramas, así que ningún trámite en curso cambia de comportamiento.
 */
/**
 * ¿La familia es traspaso? Acepta los DOS vocabularios: el estado del asistente trae ya la familia
 * (`TRASPASO`) mientras el nombre del campo sigue siendo `modalidad`, y una vía de entrada o un
 * borrador viejo pueden traer la modalidad heredada (`traspaso`). Comparar solo contra una de las
 * dos formas es lo que hacía que estas ramas nunca se tomaran.
 */
export function esFamiliaTraspaso(familia: string | null | undefined): boolean {
  const v = (familia ?? '').trim().toUpperCase();
  return v === 'TRASPASO';
}

function desdeModalidad(familia: ProcedureFamily | WizardModalidad): CapacidadesEfectivas {
  const esTraspaso = esFamiliaTraspaso(familia);
  return {
    entraPorVin: !esTraspaso && familia !== 'OTROS',
    pideVendedor: esTraspaso,
    pideComprador: true,
    pideValorComercial: esTraspaso,
    prendaEsPuerta: esTraspaso,
    validaIdentidadDelVendedor: esTraspaso,
  };
}

export function capacidadesEfectivas(
  capabilities: WizardCapabilities | null | undefined,
  familia: ProcedureFamily | WizardModalidad,
): CapacidadesEfectivas {
  if (!capabilities) return desdeModalidad(familia);

  const actores = capabilities.biometricActors.map((a) => a.toUpperCase());

  return {
    // `PLATE` es explícito; cualquier otro valor —incluido el ausente— se lee como VIN solo si el
    // tipo lo dice, porque pedir un VIN a un trámite que ya tiene placa es un callejón sin salida.
    entraPorVin: (capabilities.entryMode ?? '').toUpperCase() === 'VIN',
    pideVendedor: capabilities.requiresSeller,
    pideComprador: capabilities.requiresBuyer,
    pideValorComercial: capabilities.requiresCommercialValue,
    prendaEsPuerta: capabilities.hasPrendaGate,
    // OWNER es la parte saliente. En la familia OTROS el titular se persiste como comprador y no
    // hay parte saliente que validar, así que la lista trae solo BUYER.
    validaIdentidadDelVendedor: capabilities.requiresBiometrics && actores.includes('OWNER'),
  };
}

/**
 * Roles que captura el paso de actores. El orden importa: saliente antes que entrante, que es como
 * lo lee el gestor y como lo ordena el resto del expediente.
 */
export function rolesDeActores(
  caps: CapacidadesEfectivas,
): ('vendedor' | 'comprador')[] {
  const roles: ('vendedor' | 'comprador')[] = [];
  if (caps.pideVendedor) roles.push('vendedor');
  if (caps.pideComprador) roles.push('comprador');
  // Un tipo sin ninguna parte declarada no existe en el catálogo, pero si llegara, capturar al
  // titular es más útil que pintar un paso vacío.
  return roles.length > 0 ? roles : ['comprador'];
}

/**
 * Adaptadores a la `modalidad` heredada, para los componentes de paso que todavía la reciben.
 *
 * No son un puente perezoso: cada uno traduce la pregunta REAL que ese componente le hace a la
 * modalidad, así que un trámite de la familia OTROS ya cae del lado correcto en vez de heredar el
 * de matrícula. Se retiran cuando esos componentes reciban las capacidades directamente.
 */

/**
 * Para lo que depende de si el vehículo YA está matriculado: qué declaraciones se piden (tipo de
 * servicio, casilla 18), qué documentos pasan por OCR (factura y aduana solo existen en un vehículo
 * que se matricula por primera vez) y quién fija el organismo de tránsito (en los demás, el RUNT).
 */
export function modalidadPorEntrada(caps: CapacidadesEfectivas): WizardModalidad {
  return caps.entraPorVin ? 'matricula_inicial' : 'traspaso';
}

/** Para lo que depende de cuántas partes intervienen: la validación de identidad. */
export function modalidadPorPartes(caps: CapacidadesEfectivas): WizardModalidad {
  return caps.pideVendedor ? 'traspaso' : 'matricula_inicial';
}
