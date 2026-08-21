# Guía del mandatario en el Contrato de Mandato

Documento funcional. Explica quién firma el contrato de mandato como **mandatario**, cómo lo elige el sistema, dónde se configura en el aplicativo y qué tablas de base de datos lo soportan.

Fecha: 21 de agosto de 2026 · Alcance: módulo de Trámites y Administración de FLIT.

---

## 1. Lo esencial en un párrafo

En cada trámite hay dos figuras. El **mandante** es quien otorga el poder: el vendedor en un traspaso, o el radicador en una matrícula inicial. El **mandatario** es quien recibe ese poder y gestiona el trámite ante el organismo de tránsito por cuenta de la compañía gestora.

La diferencia práctica es esta: el mandante **no se configura** en ninguna pantalla, sale de los actores que el gestor capturó en el trámite. El mandatario **sí se configura**, y es donde vive toda la parametrización que describe este documento.

---

## 2. Quién puede ser mandatario

Un mandatario es una persona natural que la **compañía gestora** registra en el aplicativo. No es un actor del trámite: es alguien del equipo de la gestora (o vinculado a ella) autorizado a firmar mandatos ante ciertos organismos.

Para que una persona aparezca como opción en un trámite tiene que cumplir **todo** lo siguiente:

| Condición | Dónde se define |
|---|---|
| Estar activa como mandatario | Ficha de compañía → Mandatarios |
| Estar habilitada en el organismo del trámite | Checkbox de organismos en su ficha |
| Pertenecer a la compañía gestora dueña del trámite | Se asigna al registrarla desde la compañía |
| Aplicar a la empresa que otorga el mandato | Checkboxes de empresas dentro de cada organismo |

Sobre el último punto hay una regla que conviene tener clara porque es contraintuitiva:

> Si a un mandatario **no** le marcas ninguna empresa dentro de un organismo, firma para **todas** las empresas de ese organismo. Solo cuando marcas al menos una, queda restringido a esas.

Esto es deliberado: los mandatarios registrados antes de que existiera esta restricción no tienen empresas asociadas, y sin esa regla habrían desaparecido de todos los trámites de golpe.

---

## 3. Dónde se configura en el aplicativo

Hay cuatro lugares distintos. Los tres primeros son de administración; el cuarto ocurre durante la operación del trámite.

### 3.1. Ficha de compañía → pestaña «Mandatarios»

**Ruta:** Admin → Compañías → (seleccionar compañía) → pestaña **Mandatarios**

Es el lugar principal. Aquí la compañía registra a las personas y decide dónde aplican. El formulario «Registrar mandatario» / «Editar mandatario» pide:

| Campo | Para qué sirve |
|---|---|
| **Nombre completo** | Aparece bajo la firma en el PDF del mandato |
| **Tipo de documento** y **Número de documento** | Identificación en el contrato; también sirve para buscar su firma del baúl |
| **Correo** | Con correo, al registrarlo se le envía la validación de identidad. Si ya tiene una vigente, se reutiliza y no se le vuelve a escribir |
| **Firma del baúl** (opcional) | Su firma custodiada. Con ella, el mandato la estampa automáticamente |
| **Organismos donde aplica** | Lista de checkboxes. Solo se muestran los organismos habilitados para esa compañía |
| **Empresas** (dentro de cada organismo marcado) | Acota para qué empresas representadas firma. Vacío = todas |
| **Firma de forma física** (dentro de cada organismo marcado) | El contrato de ese organismo deja la línea para firmar a mano en lugar de estampar |
| **Bloque de identidad** (solo al editar) | Enviar, reenviar o consultar el estado de la validación de identidad |

El formulario **bloquea el guardado** si la persona queda sin ninguna forma de firmar en alguno de los organismos marcados. En ese caso muestra una alerta en rojo indicando en cuáles y qué hacer: capturarle la firma del baúl, registrarle un correo para la validación de identidad, o marcar esos organismos como de firma física.

> Nota histórica: antes los mandatarios se registraban desde el perfil del organismo de tránsito, que elegía compañías. Se invirtió porque el mandatario es de la empresa, no del organismo.

### 3.2. Plataforma → Mandatos → «Configurar mandato» (por organismo)

**Ruta:** Admin → Plataforma → **Mandatos** → fila del organismo → **Configuración del mandato**

Es configuración de SuperAdmin y aplica **a todo el organismo**, sin importar la compañía. Define cómo está redactado el contrato:

| Opción | Qué hace |
|---|---|
| **Redacción que aplica este OT** | Elige la plantilla del contrato entre las del sistema |
| **Mandatario institucional / UT** | Razón social de la unión temporal que actúa como mandatario |
| **NIT** | NIT de esa unión temporal |
| **Ciudad cámara** | Ciudad de la Cámara de Comercio que cita el texto |
| **Sigla** | Sigla de la unión temporal (por ejemplo, UT-SETSA) |

Las redacciones disponibles son:

| Redacción | Aplica a | Quién firma en el recuadro |
|---|---|---|
| **Automática (según el organismo)** | Opción por defecto: usa la plantilla que el sistema tiene asignada al código del organismo, o la genérica si no tiene | Depende de la que resulte |
| **Genérico** | Cualquier organismo sin plantilla propia | Mandante y mandatario |
| **Sabaneta** | Sabaneta (código 5631000) — mandatario UT-SETSA | Solo el mandante |
| **Bello** | Bello (código 5088000) — mandatario es el RL de UT-MAB | Mandante y mandatario |
| **Envigado, Funza y Medellín** | Los tres organismos comparten una redacción corta | Mandante y mandatario |

⚠️ **Advertencia importante:** desde que la plantilla se elige libremente por organismo, se puede aplicar cualquier redacción a cualquier organismo. Las redacciones de Sabaneta y Bello traen datos fijos en su texto, así que aplicar la de Bello a un organismo de Bogotá haría que el contrato cierre diciendo «en el municipio de Bello, Antioquia». El aplicativo **advierte pero no bloquea** (decisión de producto), así que hay que leer la advertencia antes de guardar.

### 3.3. Plataforma → Mandatos → «Configurar mandatario» (por compañía)

**Ruta:** Admin → Plataforma → **Mandatos** → fila del organismo → **Configuración del mandatario**

Aquí se define, **para cada compañía que trabaja con ese organismo**, qué tipo de mandato se emite y quién firma por defecto. La tabla tiene búsqueda por compañía y filtro por tipo.

| Columna | Opciones |
|---|---|
| **Tipo de mandato** | Persona o RL · Institucional (OT / UT) · Abierto (sin asumir) |
| **Mandatario por defecto** | Lista de mandatarios habilitados. Solo aplica cuando el tipo es «Persona o RL»; en los otros dos muestra «No aplica» |

Qué significa cada tipo:

- **Persona o RL** — Es el default. Una persona natural registrada firma como mandatario. Al aprobar, el sistema exige que haya un mandatario resuelto.
- **Institucional (OT / UT)** — El organismo o la unión temporal actúa como mandatario. Normalmente solo firma el mandante. **No se exige firmante persona al aprobar.**
- **Abierto (sin asumir)** — El contrato se genera sin mandatario asignado: nombre, cédula, firma y hash quedan en líneas abiertas para llenar a mano. **Tampoco se exige firmante al aprobar.**

Esta configuración por compañía **manda sobre** la del organismo cuando ambas existen.

### 3.4. Convenio comercial compañía ↔ organismo

Cuando existe un convenio comercial registrado entre la compañía y el organismo, el contrato de mandato **no lleva bloque de firma del mandatario**: firma solo el mandante. Los datos del mandatario siguen apareciendo en el cuerpo del contrato; lo que desaparece es el recuadro de firma.

El sistema distingue dos situaciones que parecen iguales pero no lo son: un organismo al que **nadie** le ha registrado convenios se comporta distinto de uno donde sí hay convenios pero esta compañía no está incluida.

### 3.5. Durante la operación: aprobación del organismo

**Ruta:** módulo del organismo de tránsito → listado de trámites de la compañía cliente → aprobar

Si al aprobar el trámite hay varios mandatarios posibles y el sistema no puede decidir solo, muestra un diálogo para que el aprobador elija uno. Ese es el punto donde hoy se resuelve la mayoría de los casos.

> **Estado actual:** existe una sección de selección de mandatario pensada para el wizard del gestor (cuando se registra el trámite), pero **no está montada en la aplicación**. El componente está desarrollado y con pruebas, pero ninguna pantalla lo usa. En la práctica, hoy la elección ocurre en la aprobación del organismo o de forma automática.

### 3.6. Excepción: mandato personalizado de la compañía

**Ruta:** Ficha de compañía → pestaña **Configuración Empresa** → Documentos personalizados

Si una compañía carga su propio PDF de mandato y lo activa, ese documento **reemplaza al generado por el sistema**. En ese caso toda la configuración de mandatarios, plantillas y firmas queda sin efecto para esa compañía: el PDF es estático y no tiene bloques de firma que el sistema pueda llenar.

Consecuencia práctica: un trámite con mandato personalizado **no exige mandatario** al aprobar. Si lo exigiera, la aprobación quedaría bloqueada para siempre porque nadie va a firmar ese documento desde el sistema.

---

## 4. Cómo elige el sistema al mandatario

Cuando llega el momento de generar o firmar el mandato, el sistema recorre esta secuencia y se queda con el primer resultado:

1. **¿Ya hay uno elegido?** La elección explícita —la guardada en el trámite o la que hace el aprobador— manda siempre.
2. **¿Hay un mandatario por defecto configurado?** Se usa, pero **solo si sigue siendo un candidato válido** para ese organismo y esa compañía. Un default que ya no aplica no se impone.
3. **¿Hay un único candidato?** Se usa ese.
4. **¿Hay varios y ninguna de las reglas anteriores resolvió?** No se sugiere ninguno.

Si al aprobar todavía no hay uno resuelto, entra una regla adicional: el sistema compara los candidatos con la **cuenta de usuario que está aprobando**. Si hay coincidencia con exactamente una persona, la usa. Si hay varias coincidencias (dato inconsistente) o ninguna, muestra el error `mandatario_requerido` y pide elegir.

Resumen de desenlaces al aprobar:

| Situación | Resultado |
|---|---|
| Cero mandatarios configurados | Aprueba sin firmante persona |
| Un solo candidato | Se asigna automáticamente |
| Varios, coincide la cuenta del aprobador | Se asigna automáticamente |
| Varios, sin coincidencia clara | Error `mandatario_requerido` — hay que elegir |
| Tipo Institucional o Abierto | No aplica: aprueba sin firmante persona |
| Mandato personalizado de la compañía | No aplica: aprueba sin firmante persona |

---

## 5. Cómo firma el mandatario

### Los tres modos del recuadro de firma

En los tres casos los datos del mandatario siguen apareciendo en el **cuerpo** del contrato. Lo que cambia es solo el recuadro de firmas al final.

| Modo | Qué se ve en el PDF | Qué lo activa |
|---|---|---|
| **Estampada** | La firma o el sello del mandatario impresos sobre la línea | Es el caso normal |
| **Manual** | Línea de guiones con sus datos debajo, sin estampa | Marcar «Firma de forma física» en ese organismo, o que el mandatario sea el propio organismo |
| **Sin bloque** | El recuadro solo lleva al mandante | Convenio comercial entre la compañía y el organismo, o tipo Institucional |

### Las tres formas de tener con qué firmar

Para que un mandatario pueda firmar necesita **al menos una** de estas tres. **No son acumulativas**: basta con cualquiera.

1. **Firma del baúl vigente** — la firma custodiada de la persona. Es la de mayor prioridad: si la tiene, es la que se estampa.
2. **Validación de identidad aprobada y vigente** — se envía por correo desde su ficha. Si no hay firma del baúl, se estampa el sello de esta validación.
3. **Firma física en ese organismo** — no necesita ninguna de las anteriores: el documento le deja la línea y él la suscribe en papel.

Si no tiene ninguna de las tres, la aprobación devuelve `mandatario_identidad_requerida`.

### Firma posterior (diferida)

Si el mandatario se queda sin con qué firmar, el trámite no se bloquea: se puede marcar **firma posterior**. Queda registrada una marca pendiente y la firma se aplica más adelante, cuando la persona consiga su firma o su identidad.

La opción **no se ofrece** a quien firma a mano, porque no tiene nada que esperar: el documento ya le deja la línea.

---

## 6. Problemas frecuentes y qué revisar

| Síntoma | Qué revisar |
|---|---|
| No aparece ningún mandatario para elegir | Que la persona esté activa, marcada en ese organismo, y que la compañía del trámite sea la correcta |
| Aparece un mandatario pero no el esperado | Las empresas marcadas dentro de ese organismo en su ficha (puede estar acotado por NIT) |
| Error `mandatario_requerido` al aprobar | Hay varios candidatos y ninguno resuelve solo: elegir en el diálogo o definir un mandatario por defecto en Plataforma → Mandatos |
| Error `mandatario_identidad_requerida` | La persona no tiene firma del baúl, ni identidad vigente, ni está marcada como firma física en ese organismo |
| El mandato sale sin firma del mandatario | Revisar si hay convenio comercial con ese organismo, o si el tipo es Institucional |
| El contrato nombra a un municipio o una UT que no corresponde | Se aplicó al organismo una redacción de otro (Sabaneta o Bello). Cambiar a «Automática» o a la genérica |
| No se puede guardar el mandatario | La alerta roja indica en qué organismos queda sin poder firmar. Asignarle firma, correo de identidad, o marcarlo como firma física |

---

## 7. Tablas de base de datos (referencia técnica)

Para quien necesite consultar o auditar directamente.

### Mandatarios y su alcance

| Tabla | Qué guarda |
|---|---|
| `admin.mandate_signers` | La persona: nombre, tipo y número de documento, correo, firma del baúl, cuenta de usuario, activo/inactivo |
| `admin.mandate_signer_transit_offices` | En qué organismos aplica cada mandatario. Incluye la marca `signs_physically` (firma a mano) |
| `admin.mandate_signer_companies` | Vínculo del mandatario con la compañía gestora en un organismo |
| `admin.mandate_signer_represented_companies` | A qué empresas representadas aplica en cada organismo. Sin filas = aplica a todas |
| `admin.admin_identity_validations` | Validaciones de identidad (`subject_type = mandate_signer`), con estado y vigencia |

### Configuración del contrato

| Tabla | Qué guarda |
|---|---|
| `admin.transit_office_mandate_config` | Configuración por organismo: redacción, familia del mandatario, datos de la UT (nombre, NIT, ciudad cámara, sigla), plantilla propia |
| `admin.company_ot_mandate_rules` | Configuración por compañía y organismo: tipo de mandato y mandatario por defecto. Manda sobre la anterior |
| `admin.company_transit_office_agreements` | Convenios comerciales que suprimen el bloque de firma del mandatario |
| `admin.company_personalized_documents` | PDF de mandato propio de la compañía que reemplaza al generado |

### Rastro en el trámite

| Tabla / columna | Qué guarda |
|---|---|
| `tramites.procedure_instances.mandate_signer_id` | El mandatario efectivo de ese trámite |
| `tramites.deferred_signature_marks` | Marcas de firma posterior, incluida la del mandatario |
| `tramites.procedure_instance_attachments` | El PDF del mandato. La columna `source` distingue `system` (generado) de `company` (personalizado) |

---

## 8. Puntos abiertos

- **Selección en el wizard no disponible.** El componente de selección de mandatario por parte del gestor existe y tiene pruebas, pero no está montado en ninguna pantalla. La elección ocurre hoy en la aprobación del organismo.
- **Texto legal pendiente de validación.** Las redacciones de las plantillas están transcritas de la versión anterior del producto y siguen marcadas en el código como pendientes de revisión del Product Owner.
- **Plantillas propias por organismo ocultas.** La función de cargar una plantilla propia (PDF o editor) está desactivada en la interfaz mediante un interruptor, aunque el backend la conserva intacta. Los organismos que ya tienen una la siguen usando.
- **Combinaciones incoherentes de redacción y organismo.** El sistema advierte pero no impide aplicar la redacción de un organismo a otro.

---

## Origen de este documento

Levantamiento realizado sobre el código fuente del repositorio el 21 de agosto de 2026, verificando las reglas directamente en el backend (`Flit.Tramites`, `Flit.Infrastructure`) y las pantallas en el frontend. **No tiene validación normativa** (no se contrastó contra la Resolución 20233040017145) **ni certificación de QA**. Es documentación descriptiva del comportamiento actual, no una especificación aprobada.
