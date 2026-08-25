import type {
  ActorRol,
  PrendaDecision,
  WizardCapabilities,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';
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
  /**
   * Interviene un arrendatario (leasing) además del propietario. Se captura en un paso propio: no se
   * unifica con el propietario, porque son dos personas distintas del mismo trámite.
   */
  pideLocatario: boolean;
  /** El trámite lleva valor y fecha de venta. */
  pideValorComercial: boolean;
  /** La decisión de prenda bloquea, en vez de solo declararse. */
  prendaEsPuerta: boolean;
  /** Se valida la identidad de la parte saliente además de la entrante. */
  validaIdentidadDelVendedor: boolean;
  /**
   * El expediente admite declarar transformaciones POR ENCIMA del tipo base (los «trámites
   * simultáneos» del art. 5.1.8). La familia OTROS no: allí el cambio ES el trámite, y agregar un
   * color a un blindaje son dos trámites que el organismo devuelve.
   */
  permiteTransformacionesComplementarias: boolean;
  /**
   * Admite un gravamen por encima del tipo base. Ojo: es distinto de que el TIPO sea de prenda —eso
   * lo responde {@link esTipoDePrenda} y ahí la prenda se pinta igual, porque es el trámite.
   */
  permitePrendaComplementaria: boolean;
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

/** ¿La familia acumula trámites sobre el tipo base (art. 5.1.8)? OTROS no. */
function familiaAcumula(familia: string | null | undefined): boolean {
  return (familia ?? '').trim().toUpperCase() !== 'OTROS';
}

function desdeModalidad(familia: ProcedureFamily | WizardModalidad): CapacidadesEfectivas {
  const esTraspaso = esFamiliaTraspaso(familia);
  return {
    entraPorVin: !esTraspaso && familia !== 'OTROS',
    pideVendedor: esTraspaso,
    pideComprador: true,
    // El respaldo no puede saber de locatarios: los tipos que lo llevan son posteriores a las dos
    // ramas heredadas, así que un borrador sin capacidades nunca es uno de ellos.
    pideLocatario: false,
    pideValorComercial: esTraspaso,
    prendaEsPuerta: esTraspaso,
    validaIdentidadDelVendedor: esTraspaso,
    permiteTransformacionesComplementarias: familiaAcumula(familia),
    permitePrendaComplementaria: familiaAcumula(familia),
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
    pideLocatario: capabilities.requiresLessee ?? false,
    pideValorComercial: capabilities.requiresCommercialValue,
    prendaEsPuerta: capabilities.hasPrendaGate,
    // OWNER es la parte saliente. En la familia OTROS el titular se persiste como comprador y no
    // hay parte saliente que validar, así que la lista trae solo BUYER.
    validaIdentidadDelVendedor: capabilities.requiresBiometrics && actores.includes('OWNER'),
    // El backend ya resolvió perfil → familia. Ausente ⇒ se cae a la familia, que es lo que hace
    // falta para un borrador abierto antes de que estas llaves existieran: leerlo como `false` le
    // apagaría los simultáneos a un traspaso en curso sin que nadie lo hubiera pedido.
    permiteTransformacionesComplementarias:
      capabilities.allowsComplementaryTransformations ?? familiaAcumula(familia),
    permitePrendaComplementaria:
      capabilities.allowsComplementaryPrenda ?? familiaAcumula(familia),
  };
}

/**
 * Atributo del vehículo que un tipo cambia POR SÍ MISMO. Espejo de `ProcedureTypeLayers` en el
 * dominio: los mismos códigos, para que la pantalla capture exactamente lo que el FUR va a imprimir.
 */
export type TransformacionBase = 'color' | 'carroceria' | 'combustible' | 'blindaje' | null;

export function transformacionDelTipo(codigo: string | null | undefined): TransformacionBase {
  switch ((codigo ?? '').trim().toUpperCase()) {
    case 'CAMBIO_COLOR':
      return 'color';
    case 'CAMBIO_CARROCERIA':
      return 'carroceria';
    case 'CONVERSION_COMBUSTIBLE':
      return 'combustible';
    case 'BLINDAJE':
      return 'blindaje';
    default:
      return null;
  }
}

/**
 * El trámite ES el gravamen: inscribirlo, levantarlo o cambiar de acreedor. No confundir con
 * `permitePrendaComplementaria`, que es la prenda AÑADIDA a un trámite de otra naturaleza.
 */
export function esTipoDePrenda(codigo: string | null | undefined): boolean {
  const v = (codigo ?? '').trim().toUpperCase();
  return (
    v === 'PRENDA_INSCRIPCION' ||
    v === 'LEVANTAMIENTO_PRENDA' ||
    v === 'LEVANTAR_INSCRIBIR_PRENDA' ||
    v === 'CAMBIO_ACREEDOR'
  );
}

/**
 * Decisiones de prenda que ofrece un tipo PRENDARIO: son fijas, porque la acción ya la eligió quien
 * eligió el trámite. Ofrecer «omitir» o «sin prenda» en un levantamiento de prenda sería ofrecer no
 * hacer el trámite que se está radicando. `null` si el tipo no es de prenda.
 */
export function decisionesDelTipoDePrenda(
  codigo: string | null | undefined,
): PrendaDecision[] | null {
  switch ((codigo ?? '').trim().toUpperCase()) {
    case 'PRENDA_INSCRIPCION':
      return ['registrar'];
    case 'LEVANTAMIENTO_PRENDA':
      return ['levantar'];
    // Las DOS acciones son el trámite (casillas 11 + 12). El asistente captura una decisión por
    // expediente, así que el gestor elige cuál declara; el FUR marca la casilla base de todos modos.
    case 'LEVANTAR_INSCRIBIR_PRENDA':
    case 'CAMBIO_ACREEDOR':
      return ['levantar', 'registrar'];
    default:
      return null;
  }
}

/**
 * Roles que captura el paso de actores. El orden importa: saliente antes que entrante, que es como
 * lo lee el gestor y como lo ordena el resto del expediente.
 */
export function rolesDeActores(caps: CapacidadesEfectivas): ActorRol[] {
  const roles: ActorRol[] = [];
  if (caps.pideVendedor) roles.push('vendedor');
  if (caps.pideComprador) roles.push('comprador');
  // El locatario va al final y SIEMPRE en paso aparte (ver `rolesDelPasoDeActores`): no se unifica
  // con el propietario porque son dos personas distintas, no las dos caras de una transferencia.
  if (caps.pideLocatario) roles.push('locatario');
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
