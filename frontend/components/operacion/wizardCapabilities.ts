import type {
  ActorRol,
  BiometricParte,
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
  /**
   * ADR-0051 — esa parte vendedora se captura tecleando datos en `ActorsForm`, en vez de llegar de
   * otra fuente. Es DISTINTO de {@link pideVendedor}: en `TRASPASO_UNILATERAL` hay vendedor (va en
   * el FUR, el backend lo sincroniza desde el RUNT) pero no se le pinta formulario — salvo la
   * excepción puntual de `revealSellerForm` (`sectionConfig.actor_form`, por instancia, no por
   * tipo). `rolesDeActores()` decide con ESTA llave, no con `pideVendedor`.
   */
  vendedorCapturaPorFormulario: boolean;
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
   * Partes que VALIDAN IDENTIDAD y firman, en el orden de presentación (saliente antes que entrante).
   *
   * <p>Lo declara el tipo en `biometricActors`, y no se deduce de cuántas partes tiene el trámite: en
   * `TRASPASO_UNILATERAL` hay dos partes —propietario y locatario— pero **solo el propietario firma**
   * (art. 5.3.2.2). El paso de identidad preguntaba «¿es un traspaso?» para pintar las dos tarjetas,
   * así que le pedía al locatario una validación que ese trámite no exige.</p>
   */
  partesBiometricas: BiometricParte[];
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
  /**
   * El organismo de tránsito lo escoge el gestor entre los habilitados de su compañía, en vez de
   * imponerlo el RUNT.
   *
   * <p>Antes esto era `entraPorVin`: entra por VIN ⇒ el vehículo aún no tiene organismo ⇒ lo elige el
   * gestor. Con veintiún tipos deja de bastar — un radicado de cuenta entra por PLACA y aun así lo
   * elige, porque el trámite es precisamente llevar la cuenta a otro organismo.</p>
   */
  eligeOrganismo: boolean;
  /**
   * El trámite DECLARA a qué organismo va, además del suyo. Es el traslado de cuenta: lo expide el
   * organismo de ORIGEN —él valida el paz y salvo y él aprueba— y el destino solo se declara, para
   * que el FUR diga a dónde se traslada.
   *
   * <p>Es el ESPEJO de {@link eligeOrganismo}, no lo mismo: allí el organismo elegido es el del
   * trámite (radicado de cuenta); aquí el del trámite lo sigue imponiendo el RUNT.</p>
   */
  declaraOrganismoDestino: boolean;
  /**
   * El trámite PIDE una placa nueva al organismo: matrícula, rematrícula, duplicado de placa.
   *
   * <p>Es lo que decide si la preferencia de dígito de preasignación tiene sentido. Antes se
   * deducía de {@link eligeOrganismo}, y por eso un radicado de cuenta —que elige organismo porque
   * el trámite es llevar la cuenta a otro— acababa preguntando en qué dígito prefiere que termine
   * una placa que el vehículo ya tiene.</p>
   */
  pidePlaca: boolean;
  /**
   * El tipo admite generar la impronta (Kyverum / paso FUR), aunque el documento sea opcional.
   * Lo apaga solo `gate_profile.improntaSource === 'MANUAL'`.
   */
  permiteGenerarImprontaAutomatica: boolean;
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
    // El respaldo reproduce las dos modalidades heredadas: ninguna de las dos tenía captura oculta
    // (eso llegó con `TRASPASO_UNILATERAL`, posterior a estas dos ramas), así que aquí siempre
    // coincide con `pideVendedor`.
    vendedorCapturaPorFormulario: esTraspaso,
    pideComprador: true,
    // El respaldo no puede saber de locatarios: los tipos que lo llevan son posteriores a las dos
    // ramas heredadas, así que un borrador sin capacidades nunca es uno de ellos.
    pideLocatario: false,
    pideValorComercial: esTraspaso,
    prendaEsPuerta: esTraspaso,
    validaIdentidadDelVendedor: esTraspaso,
    // El respaldo reproduce las dos ramas heredadas: en traspaso firmaban las dos partes.
    partesBiometricas: esTraspaso ? ['vendedor', 'comprador'] : ['comprador'],
    permiteTransformacionesComplementarias: familiaAcumula(familia),
    permitePrendaComplementaria: familiaAcumula(familia),
    // El respaldo reproduce el criterio previo: solo elige organismo quien entra por VIN.
    eligeOrganismo: !esTraspaso && familia !== 'OTROS',
    // Ningún tipo del respaldo declara destino: son las dos modalidades heredadas.
    declaraOrganismoDestino: false,
    // El respaldo reproduce el criterio previo: pide placa quien entra por VIN.
    pidePlaca: !esTraspaso && familia !== 'OTROS',
    // Un borrador sin capacidades no debe perder el check de generar impronta.
    permiteGenerarImprontaAutomatica: true,
  };
}

/**
 * Traduce el vocabulario del catálogo (`OWNER` / `BUYER`) al de las partes del asistente, en el orden
 * de presentación: la saliente antes que la entrante, como se lee el expediente.
 *
 * <p>`LESSEE` no se traduce: el arrendatario no valida identidad en ningún tipo del catálogo (en el
 * leasing quien firma es el propietario), y el paso solo sabe pintar comprador y vendedor.</p>
 *
 * <p>Lista vacía ⇒ el tipo no declaró firmantes: se cae al criterio anterior a la llave (las dos
 * partes en traspaso, solo la entrante en el resto) para no dejar un paso de identidad sin nadie.</p>
 */
function partesBiometricasDe(actores: string[], esTraspaso: boolean): BiometricParte[] {
  const partes: BiometricParte[] = [];
  if (actores.includes('OWNER')) partes.push('vendedor');
  if (actores.includes('BUYER')) partes.push('comprador');
  if (partes.length > 0) return partes;
  return esTraspaso ? ['vendedor', 'comprador'] : ['comprador'];
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
    // Ausente ⇒ el criterio anterior a la llave: `requiresSeller`. Todo tipo que hoy captura al
    // vendedor por formulario (todo el que declara `requiresSeller:true` salvo
    // `TRASPASO_UNILATERAL`) sigue haciéndolo sin cambio; y un tipo SIN parte vendedora (OTROS,
    // MATRICULA_*) nunca la infla a `true` por la sola ausencia de la llave — si no,
    // `rolesDeActores()` (que ya NO mira `pideVendedor`) le pintaría un formulario de vendedor a un
    // trámite que no tiene parte vendedora.
    vendedorCapturaPorFormulario: capabilities.sellerCapturedViaForm ?? capabilities.requiresSeller,
    pideComprador: capabilities.requiresBuyer,
    pideLocatario: capabilities.requiresLessee ?? false,
    pideValorComercial: capabilities.requiresCommercialValue,
    prendaEsPuerta: capabilities.hasPrendaGate,
    // OWNER es la parte saliente. En la familia OTROS el titular se persiste como comprador y no
    // hay parte saliente que validar, así que la lista trae solo BUYER.
    validaIdentidadDelVendedor: capabilities.requiresBiometrics && actores.includes('OWNER'),
    partesBiometricas: partesBiometricasDe(actores, esFamiliaTraspaso(familia)),
    // El backend ya resolvió perfil → familia. Ausente ⇒ se cae a la familia, que es lo que hace
    // falta para un borrador abierto antes de que estas llaves existieran: leerlo como `false` le
    // apagaría los simultáneos a un traspaso en curso sin que nadie lo hubiera pedido.
    permiteTransformacionesComplementarias:
      capabilities.allowsComplementaryTransformations ?? familiaAcumula(familia),
    permitePrendaComplementaria:
      capabilities.allowsComplementaryPrenda ?? familiaAcumula(familia),
    // Ausente ⇒ el criterio anterior a la llave: lo elige quien entra por VIN.
    eligeOrganismo:
      capabilities.operatorChoosesTransitOffice ??
      (capabilities.entryMode ?? '').toUpperCase() === 'VIN',
    declaraOrganismoDestino: capabilities.requiresDestinationTransitOffice ?? false,
    // Ausente ⇒ el criterio anterior a la llave: pide placa quien entra por VIN.
    pidePlaca:
      capabilities.requiresPlateRequest ??
      (capabilities.entryMode ?? '').toUpperCase() === 'VIN',
    // Ausente ⇒ se puede generar (también si el documento es opcional). Solo MANUAL la apaga.
    permiteGenerarImprontaAutomatica: permiteGenerarImprontaAutomatica(capabilities.improntaSource),
  };
}

/** `MANUAL` apaga Kyverum / diferir al FUR. Cualquier otro valor (o ausente) la permite. */
export function permiteGenerarImprontaAutomatica(source: string | null | undefined): boolean {
  return (source ?? '').trim().toUpperCase() !== 'MANUAL';
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
 * Tipos prendarios de UNA sola acción sobre el gravamen. Espejo de
 * `ProcedureTypeLayers.EsPrendaDeAccionUnica` en el dominio.
 *
 * <p>En ellos el certificado NO es opcional aunque la compañía haya desactivado la exigencia del
 * organismo: es el soporte del acto que se está radicando, no un requisito añadido por el OT. El
 * gate del servidor lo exige siempre, así que la pantalla no puede rotularlo «Opcional» y bloquear
 * después al radicar.</p>
 */
export function esPrendaDeAccionUnica(codigo: string | null | undefined): boolean {
  const v = (codigo ?? '').trim().toUpperCase();
  return v === 'PRENDA_INSCRIPCION' || v === 'LEVANTAMIENTO_PRENDA';
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
  // ADR-0051 — LAS DOS condiciones, y en este orden: que el tipo tenga parte vendedora, y que esa
  // parte se capture aquí. `TRASPASO_UNILATERAL` tiene la primera y no la segunda (el propietario se
  // sincroniza desde el RUNT); pintar su formulario es la excepción `revealSellerForm`, que añade
  // `TramiteWizard.tsx` por instancia y no la capacidad del tipo.
  //
  // Antes bastaba `vendedorCapturaPorFormulario`, y funcionaba solo porque el backend NUNCA mandaba
  // la llave: al caer a `?? requiresSeller`, las dos preguntas colapsaban en una. Desde que el DTO la
  // publica de verdad, un tipo SIN parte vendedora la recibe en `true` —es su valor por defecto, y
  // describe «si hubiera vendedor, se capturaría por formulario»— y la condición sola le pintaba una
  // tarjeta de VENDEDOR a una matrícula inicial o a un blindaje.
  if (caps.pideVendedor && caps.vendedorCapturaPorFormulario) roles.push('vendedor');
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
