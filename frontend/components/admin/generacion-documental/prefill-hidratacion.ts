/**
 * Núcleo del prellenado «placa primero»: qué se hidrata, qué queda bloqueado y qué se señala como
 * discrepancia — HU #12209 (Feature #12201, CF-25 / adenda §15.1 del PO).
 *
 * <p>Es una función <b>pura</b> a propósito. El bloqueo anti-pisado es una regla de negocio con
 * cuatro casos de borde (campo vacío, campo escrito, campo escrito e igual, campo que ninguna
 * fuente puede traer) y no debe vivir dentro de un componente donde solo se pueda probar tecleando.
 * Es el mismo criterio de `ActorsForm.tsx` —el valor del operador gana— extraído a un módulo
 * propio: ese archivo tiene 4.212 líneas y no se toca en este Feature.</p>
 *
 * Uso de ejemplo:
 * ```ts
 * const { parche, hidratados, discrepancias } = aplicarPrellenado({
 *   valoresActuales: { placa: "ABC123", marca: "" },
 *   campos: [{ key: "marca", value: "MAZDA", source: "RUNT" }],
 *   fuenteBloque: "RUNT",
 *   siempreEditables: CAMPOS_VEHICULO_SIEMPRE_EDITABLES,
 *   siempreManuales: CAMPOS_VEHICULO_SIEMPRE_MANUALES,
 * });
 * ```
 */
import type { PrefillField, PrefillFuente } from "@/lib/api/types-generacion-documental";

/**
 * Decisión del PO (2026-09-08, adenda §15.1): <b>`color` y `tipoCarroceria` quedan editables</b>
 * tras hidratarse desde RUNT, sin ninguna acción previa.
 *
 * <p>No es una concesión de usabilidad. `FurCommand.cs:820` ya distingue el valor RUNT del valor
 * efectivo para esos dos campos (`RuntOrEffective`) porque el RUNT se desactualiza ahí, y el
 * organismo de tránsito confronta contra la <b>licencia de tránsito</b>, no contra el RUNT. Un
 * repintado o un cambio de carrocería registrado en el cartón y no en el RUNT haría que el
 * documento contradijera el soporte que el OT tiene delante.</p>
 *
 * <p><b>`combustible` no está aquí y no puede estarlo:</b> no es una de las 13 variables de
 * vehículo del anexo normativo (`docs/plantilla-transferencia-dominio.md` §5.1). Este documento no
 * lo captura ni lo imprime, así que no hay nada que volver editable. Incorporarlo exigiría añadir
 * una variable al anexo, lo que requiere dictamen previo de `expert-doc-engine`.</p>
 */
export const CAMPOS_VEHICULO_SIEMPRE_EDITABLES: readonly string[] = ["color", "tipoCarroceria"];

/**
 * Campos que ninguna consulta devuelve y que, por tanto, <b>nunca</b> aparecen bloqueados ni
 * marcados como hidratados.
 *
 * <p>El número de licencia de tránsito está en el cartón físico, no en el RUNT: marcarlo como
 * «dato de la fuente» sería mentir sobre su procedencia y bloquearlo impediría capturarlo.</p>
 */
export const CAMPOS_VEHICULO_SIEMPRE_MANUALES: readonly string[] = ["noLicenciaTransito"];

/**
 * Campos que una fuente concreta <b>no puede</b> aportar, aunque la respuesta los traiga.
 *
 * <p>Dos hechos de contrato, no dos gustos:</p>
 * <ul>
 *   <li><b>RUES no devuelve el representante legal.</b> Certifica la <i>facultad</i> de
 *       representación, no la persona. Si el directorio de representantes legales no responde y la
 *       fuente efectiva es RUES, el representante legal y su documento quedan manuales.</li>
 *   <li><b>`contact-lookup` nunca devuelve nombre ni documento.</b> Por contrato solo resuelve
 *       datos de contacto: si el RUNT persona no responde, el nombre queda manual.</li>
 * </ul>
 */
export const CAMPOS_NO_PROVISTOS_POR_FUENTE: Record<string, readonly string[]> = {
  RUES: ["representanteLegal", "ccRepresentanteLegal"],
  CONTACT_LOOKUP: ["nombreRazonSocial"],
};

/** Estado de un campo hidratado, tal como lo pinta la interfaz. */
export interface CampoHidratado {
  /** Fuente declarada para ese campo concreto. */
  fuente: PrefillFuente;
  /** `false` para `color`/`tipoCarroceria` y para cualquier campo liberado por el usuario. */
  bloqueado: boolean;
}

/** Un valor de la fuente que NO se escribió porque el usuario ya había capturado otro. */
export interface DiscrepanciaPrellenado {
  campo: string;
  /** Valor propuesto por la fuente. El valor del usuario sigue siendo el del formulario. */
  valorFuente: string;
  fuente: PrefillFuente;
}

export interface EntradaPrellenado {
  /** Valores vigentes del bloque en el formulario. */
  valoresActuales: Record<string, string | undefined>;
  /**
   * Campos que el usuario tocó mientras la consulta estaba en vuelo. Defensa contra la carrera
   * entre la edición manual y la respuesta asíncrona, igual que `touchedContact` en `ActorsForm`.
   */
  tocados?: ReadonlySet<string>;
  campos: readonly PrefillField[];
  fuenteBloque?: PrefillFuente | null;
  siempreEditables?: readonly string[];
  siempreManuales?: readonly string[];
}

export interface ResultadoPrellenado {
  /** Solo los campos que efectivamente se escriben. Nunca pisa un valor del usuario. */
  parche: Record<string, string>;
  hidratados: Record<string, CampoHidratado>;
  discrepancias: DiscrepanciaPrellenado[];
}

/** Comparación tolerante: espacios y mayúsculas no son una discrepancia real. */
function normaliza(valor: string): string {
  return valor.trim().replace(/\s+/g, " ").toLocaleUpperCase("es-CO");
}

/**
 * Aplica una respuesta de prellenado sobre el estado del formulario.
 *
 * <p><b>Regla dura:</b> si el usuario ya escribió un valor y la fuente propone otro, gana el del
 * usuario y la discrepancia se <i>señala</i>. Sobrescribir sería perder captura deliberada; callar
 * sería ocultar que la fuente dice otra cosa.</p>
 */
export function aplicarPrellenado({
  valoresActuales,
  tocados,
  campos,
  fuenteBloque,
  siempreEditables = [],
  siempreManuales = [],
}: EntradaPrellenado): ResultadoPrellenado {
  const parche: Record<string, string> = {};
  const hidratados: Record<string, CampoHidratado> = {};
  const discrepancias: DiscrepanciaPrellenado[] = [];

  for (const campo of campos) {
    const clave = campo.key;
    if (siempreManuales.includes(clave)) {
      continue;
    }

    const fuente = (campo.source ?? fuenteBloque ?? "").toString();
    if (!fuente) {
      // Sin fuente declarada no se hidrata: la interfaz no puede decir de dónde salió el dato.
      continue;
    }
    if ((CAMPOS_NO_PROVISTOS_POR_FUENTE[fuente] ?? []).includes(clave)) {
      continue;
    }

    const propuesto = (campo.value ?? "").trim();
    if (!propuesto) {
      continue;
    }

    const actual = (valoresActuales[clave] ?? "").trim();

    if (!actual) {
      // El usuario dejó el campo vacío a propósito mientras la consulta viajaba: no se rellena.
      if (tocados?.has(clave)) continue;
      parche[clave] = propuesto;
      hidratados[clave] = { fuente, bloqueado: !siempreEditables.includes(clave) };
      continue;
    }

    if (normaliza(actual) === normaliza(propuesto)) {
      // Coincide con lo capturado (caso típico de la placa, que es la llave de la consulta):
      // se marca como dato de la fuente sin reescribirlo.
      hidratados[clave] = { fuente, bloqueado: !siempreEditables.includes(clave) };
      continue;
    }

    discrepancias.push({ campo: clave, valorFuente: propuesto, fuente });
  }

  return { parche, hidratados, discrepancias };
}
