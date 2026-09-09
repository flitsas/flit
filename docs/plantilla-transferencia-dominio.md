# Plantilla Normativa — Documento de Transferencia de Dominio de Vehículo Automotor

**Dictamen:** `expert-doc-engine` · Resolución 20233040017145/2023 (Mintransporte)
**Fecha dictamen:** 2026-09-08
**Estado:** Vigente para desarrollo. Cambios al contenido requieren nuevo dictamen de `expert-doc-engine` y actualización de este archivo en el mismo PR.
**Destino:** Módulo de generación masiva de documentos (Feature `DOCUMENTACION-generacion-masiva-docs.md`).

---

## 1. Propósito y disclaimer obligatorio

Este archivo define el contenido mínimo normativo de un «Documento de Transferencia de Dominio de Vehículo Automotor» que FLIT genera como instrumento parametrizable para uso como título de dominio soporte del Formato Único de Solicitud de Trámite (FUR / Anexo 46) en trámites de traspaso ante el Registro Nacional Automotor (RNA) o el Registro Nacional de Remolques y Semirremolques (RNRS).

> ⚠️ **Advertencias que FLIT debe mostrar al usuario antes y después de generar el documento:**
>
> 1. **No reemplaza validación jurídica.** El documento es un instrumento privado parametrizado. Su validez jurídica, suficiencia como prueba de dominio, perfeccionamiento ante notaría, sujeción a impuestos de timbre o de registro, y admisión por el Organismo de Tránsito son responsabilidad de las partes y sus asesores jurídicos. FLIT no es parte del negocio jurídico subyacente.
> 2. **No garantiza admisión por el OT.** El Organismo de Tránsito realiza validaciones propias (RUNT, SOAT, SIMIT, RTM, medidas judiciales) que FLIT no puede anticipar en todos los casos. Generar el documento no equivale a aprobar el trámite.
> 3. **La Resolución 20233040017145 de 2023 no prescribe formato interno** para el «contrato de compraventa u otro título de dominio» mencionado en el art. 5.3.2.1. Las cláusulas modelo de esta plantilla son neutrales e indicativas.

---

## 2. Fuentes normativas

| Fuente | Relevancia |
|--------|-----------|
| **Resolución 20233040017145 de 2023, Ministerio de Transporte** (28 abr 2023, vigencia 5 may 2023, D.O. 52386) | Marco general del RNA/RNRS; traspaso arts. 5.3.2.1 y 5.3.2.2 |
| **Art. 5.1.5** | Persona jurídica: el OT verifica RUES; no exige certificado físico. Ente de derecho público: acto de delegación/autorización. |
| **Art. 5.1.6** | Trámite por tercero: mandatario inscrito en RUNT + contrato de mandato o poder especial. Exterior: apostilla/legalización. |
| **Art. 5.1.8** | Formato Único (Anexo 46): puede contener varios trámites del mismo vehículo. QR / certificación de guarismos / improntas como alternativa documental. |
| **Art. 5.1.9** | No exigir en físico lo que otra entidad puede dar por interoperabilidad con el OT o el RUNT. |
| **Art. 5.3.2.1** | Traspaso ordinario: vendedor + comprador en RUNT; FUR + título de dominio + QR/guarismos/improntas; sin medidas judiciales; gravamen: levantamiento o autorización del acreedor; SOAT + RTM + SIMIT; paz y salvo de infracciones del locatario si el vendedor es entidad financiera; retención en la fuente; derechos de trámite. Remolques: sin impuesto (Ley 488/1998). |
| **Art. 5.3.2.2** | Traspaso unilateral leasing → locatario. La entidad financiera puede transferir de forma unilateral. Soporte: copia del contrato de leasing + declaración de la compañía arrendadora que manifieste que el contrato está terminado o que el locatario ejerció la opción de compra (salvo opción automática: solo el contrato — Parágrafo 1.º). Exenciones textuales: (1) revisión técnico-mecánica, (2) QR/certificación/improntas, (3) paz y salvo de infracciones, (4) **presentación del locatario (Comprador)** ante el OT, (5) **firma del Formato Único de Solicitud de Trámite** por parte del locatario. La norma no exime la firma del locatario en instrumentos privados; eso es consecuencia del carácter unilateral del acto. Párrafos 1.º (opción automática), 2.º (servicio público pasajeros/mixto), 3.º (fusión por absorción). |
| **Ley 488 de 1998** (referenciada en la resolución) | Remolques y semirremolques **exentos del impuesto sobre vehículos**. La resolución la invoca con alcances distintos según el trámite: en **matrícula** (art. 5.3.1.1 num. 3.º) el OT «no debe verificar el pago de impuestos, **ni SOAT**»; en **traspaso** (art. 5.3.2.1 num. 5.º, inciso final) el texto exime únicamente la verificación del **pago de impuestos** y no menciona el SOAT. Para traspaso, la no exigibilidad del SOAT no deriva de la Ley 488/1998 sino de que remolques y semirremolques no son vehículos automotores (Ley 769 de 2002); la decide el OT. **No citar la Ley 488/1998 como fuente de una exención de SOAT en traspaso.** |
| **Ley 527 de 1999**, arts. 5.º, 6.º, 7.º y 10.º | Equivalencia funcional del mensaje de datos. No se niega efecto jurídico a un documento por estar en forma de mensaje de datos (art. 5.º); el requisito de «escrito» se satisface con el mensaje de datos accesible para consulta posterior (art. 6.º); el requisito de **firma** se satisface con un método que identifique al iniciador e indique que aprueba el contenido (art. 7.º); el mensaje de datos es admisible como prueba (art. 10.º). Es el fundamento de que el título de dominio del art. 5.3.2.1 num. 1.º pueda otorgarse y firmarse electrónicamente. |
| **Decreto 2364 de 2012** (reglamentario del art. 7.º de la Ley 527/1999) | Firma electrónica: define confiabilidad, neutralidad tecnológica y los efectos jurídicos del acuerdo sobre el método de firma entre las partes. Base de los modos de firma de §9.0. |
| **Ley 1564 de 2012 (CGP), arts. 243 y 244** | Los mensajes de datos son documentos (art. 243). El documento privado se presume auténtico y quien lo aporta lo hace valer en su contra; la **tacha** procede contra el documento cuya autoría se desconoce (art. 244). Es el riesgo real de estampar una firma custodiada sin evidencia de consentimiento del titular — razón de la decisión de §9.0. |
| **Artefacto canónico FUR** | `docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md` — casillas y observaciones numeral 3. |
| **Artefacto canónico mandato** | `docs/ot/mandato/REGLAS-OBJETO-TRES-CAPAS.md` — objeto `{{tramite}}` en las 4 plantillas. |
| **Ejemplares FUR** | `docs/ot/fur/Fur_TraspasoUnilateral_PJxPJ.pdf`, `Fur_Traspaso_PNvidxPNvid.pdf`, `Fur_Traspaso_PJbaulxPJvid.pdf` |

---

## 3. Clasificación de escenarios

> ⚠️ **Los escenarios A, B y C clasifican el tipo de operación jurídica, NO la naturaleza de las personas (PN / PJ).** En cualquiera de los tres escenarios pueden participar personas naturales o jurídicas en los roles de transferente y adquirente. La distinción PN/PJ afecta únicamente qué datos de identificación se capturan y cómo verifica el OT, pero no define el escenario.

Los tres escenarios son **mutuamente excluyentes** en un mismo documento. El usuario/gestor debe seleccionar uno antes de ingresar datos.

| Escenario | Operación | Artículo | Código FLIT | Partes comparecientes |
|-----------|-----------|----------|------------|----------------------|
| **A — Traspaso ordinario** | Transferencia bilateral cuyo título jurídico puede ser compraventa, dación en pago, permuta u otro negocio traslaticio de dominio conforme al derecho civil y/o mercantil. El art. 5.3.2.1 numeral 1.º dice literalmente: «contrato de compraventa, **documento o declaración** en el que conste la transferencia del derecho de dominio del vehículo, celebrado con las exigencias de las normas civiles y/o mercantiles». El precio no es requisito universal: lo es para la compraventa, no para todos los títulos. No abarca ninguna de las once condiciones especiales de traspaso de los arts. 5.3.2.3 a 5.3.2.13, enumeradas una a una en §4.0 y bloqueadas por `VB-07`. | art. 5.3.2.1 | `TRASPASO_STANDARD` | Transferente **+** Adquirente |
| **B — Transferencia unilateral leasing → locatario** | La entidad financiera (propietaria registrada) transfiere unilateralmente al locatario que ejerció o recibió automáticamente la opción de compra. La norma exime la presentación del locatario ante el OT (excepción 4.ª) y su firma en el **Formato Único** (excepción 5.ª). La estructura unilateral de este documento soporte privado es consecuencia del carácter del acto, no una exención textual adicional de la resolución. | art. 5.3.2.2 | `TRASPASO_UNILATERAL` | Entidad financiera (transferente) **únicamente** |
| **C — Transferencia de entidad financiera a tercero** | La entidad financiera transfiere a un tercero que NO es el locatario histórico del contrato de leasing. No hereda ninguna exención del art. 5.3.2.2. Se rige íntegramente por el art. 5.3.2.1, incluyendo RTM, QR/improntas, paz y salvo y firma del adquirente en el FUR. | art. 5.3.2.1 (sin 5.3.2.2) | `TRASPASO_STANDARD` (flag `es_tercero_leasing = true`) | Entidad financiera **+** Tercero adquirente |

---

## 4. Matriz de escenarios — Partes, soportes, exenciones y firmas

### 4.0 Exclusión previa — traspasos especiales de los arts. 5.3.2.3 a 5.3.2.13

> ⛔ **Gate anterior a la selección de escenario.** Los escenarios A, B y C de esta plantilla cubren
> únicamente el traspaso regido por el art. 5.3.2.1 (y su variante unilateral del art. 5.3.2.2).
> La Sección 3.ª del Capítulo 5.3 tipifica **once condiciones especiales de traspaso** que exigen del
> Organismo de Tránsito requisitos, exenciones o soportes adicionales que **este documento no captura,
> no declara y no puede acreditar**. Si el vehículo o la operación encuadra en cualquiera de ellas,
> **no se genera el documento**: el usuario debe adelantar el trámite por la vía especial que
> corresponda, con los soportes propios de su artículo.

El usuario declara, antes de elegir escenario, que **ninguna** de las siguientes condiciones aplica:

| # | Condición especial | Artículo | Soporte adicional que exige la norma y que este documento no acredita |
|---|--------------------|----------|----------------------------------------------------------------------|
| 1 | Vehículo de **servicio público de pasajeros o mixto** | art. 5.3.2.3 | Contrato de cesión del derecho de vinculación o afiliación suscrito por cedente y cesionario + aceptación de la empresa; o copia del contrato de afiliación / carta de aceptación si no está vinculado |
| 2 | Traspaso **a compañía de seguros por hurto** del vehículo | art. 5.3.2.4 | El OT exceptúa SOAT, RTM e improntas/certificación/QR — régimen de exenciones distinto al del art. 5.3.2.1 |
| 3 | Traspaso **a compañía de seguros por pérdida o destrucción parcial** | art. 5.3.2.5 | Peritaje de la aseguradora que determina la pérdida o destrucción parcial; el OT exceptúa SOAT y RTM |
| 4 | Vehículo **blindado** | art. 5.3.2.6 | Resolución de la Superintendencia de Vigilancia y Seguridad Privada que autoriza el uso del blindaje o su desmonte, y certificación de la empresa blindadora registrada (no se requiere para niveles I y II) |
| 5 | Traspaso producto de **decisión judicial o administrativa** | art. 5.3.2.7 | Sentencia judicial o acto administrativo de adjudicación; el OT exceptúa la validación de identidad del propietario y registra la autoridad que profirió la decisión |
| 6 | Traspaso por **sucesión** | art. 5.3.2.8 | Sentencia o escritura pública que acredita el derecho |
| 7 | **Importación temporal por sustitución del importador** | art. 5.3.2.9 | Declaración de importación modificatoria con el nuevo importador autorizado por la DIAN; el OT expide licencia de tránsito **provisional** |
| 8 | **Decomiso** por la DIAN o adjudicación a favor de la Nación (procesos concursales o cuando la ley lo determine) | art. 5.3.2.10 | Acto administrativo o providencia de adjudicación de la entidad; no se validan SOAT, RTM, infracciones ni impuestos |
| 9 | **Comiso** por la Fiscalía General de la Nación | art. 5.3.2.11 | Acto administrativo con la orden de comiso; no se validan SOAT, RTM, infracciones ni impuestos |
| 10 | Vehículo **enajenado por declaratoria de abandono** (art. 128 Ley 769/2002, modif. Ley 1730/2014) | art. 5.3.2.12 | Acto administrativo de adjudicación + certificación del fabricante/QR/improntas; no se valida RTM y el vehículo debe ser transportado, no remolcado; registro previo de la declaratoria en RUNT |
| 11 | Vehículo de **carga con PBV superior a 10.500 kg** | art. 5.3.2.13 | Validación en RUNT de la autorización de registro inicial del Ministerio de Transporte; si hay omisión no subsanada (Res. 3913/2019), autorización escrita del comprador aceptando continuar |

> **Nota de alcance.** El art. 5.3.2.14 (expedición de la nueva licencia de tránsito) **no** es una
> condición especial: es el paso final común a todo traspaso y no excluye la generación de este
> documento.
>
> **Nota de método.** La declaración es del usuario. FLIT no puede verificar por sí mismo si el
> vehículo es blindado, si hay una sucesión en curso o si el PBV supera 10.500 kg; el gate es
> declarativo y su falsedad la resuelve el OT al recibir el trámite. La declaración se conserva como
> evidencia de que el bloqueo se ofreció y el usuario lo respondió.


### 4.1 Escenario A — Traspaso ordinario (art. 5.3.2.1)

| Dimensión | Detalle | Verificación |
|-----------|---------|-------------|
| **Transferente** | Propietario/titular actual registrado en RUNT | ✅ RUNT |
| **Adquirente** | Nuevo titular inscrito en RUNT | ✅ RUNT |
| **PJ transferente o adquirente** | OT verifica RUES; no exige certificado físico | ✅ RUES (vía OT / interoperabilidad) |
| **Trámite por mandatario** | Mandatario en RUNT + contrato de mandato o poder especial | ✅ RUNT + art. 5.1.6 |
| **Título jurídico (soporte)** | «Contrato de compraventa, **documento o declaración** en el que conste la transferencia del derecho de dominio del vehículo, celebrado con las exigencias de las normas civiles y/o mercantiles» — texto literal del art. 5.3.2.1 num. 1.º. Puede ser: compraventa, dación en pago, permuta, donación u otro negocio traslaticio válido. Este módulo de generación masiva produce el documento soporte; la naturaleza del título define si hay precio o no. | art. 5.3.2.1 numeral 1.º |
| **Soportes del trámite** | FUR (Anexo 46) + este documento + QR / certificación de guarismos / improntas | art. 5.3.2.1 numeral 1.º |
| **Medidas judiciales** | Sin medidas que impidan el traspaso — validación a cargo del OT | ✅ RUNT — OT verifica |
| **Gravamen activo** | Requiere levantamiento previo o autorización del beneficiario del gravamen o limitación para continuar con el nuevo propietario | art. 5.3.2.1 numeral 3.º |
| **SOAT** | Vigente. Para **remolques y semirremolques** el art. 5.3.2.1 **no** consagra exención textual de SOAT (a diferencia del art. 5.3.1.1 num. 3.º, que sí lo hace para matrícula); su no exigibilidad deriva de que no son vehículos automotores (Ley 769/2002) y la resuelve el OT. **Sí** hay exención textual de **impuesto sobre vehículos** para remolques y semirremolques (Ley 488/1998, art. 5.3.2.1 num. 5.º, inciso final) | ✅ FASECOLDA/RUNT — OT verifica |
| **RTM** | Vigente para los vehículos que la requieren — validación a cargo del OT | ✅ RUNT — OT verifica |
| **SIMIT** | Verificación de infracciones del propietario — a cargo del OT | ✅ SIMIT — OT verifica |
| **Paz y salvo leasing** | Cuando el propietario es establecimiento bancario o compañía de financiamiento: validación de paz y salvo de infracciones del locatario — a cargo del OT | art. 5.3.2.1 numeral 4.º |
| **Precio / contraprestación** | **Condicional al título jurídico.** Para compraventa: precio en letras y números. El OT verifica el pago de la retención en la fuente por la enajenación y, por separado, el pago del impuesto sobre vehículos (que grava el vehículo, no el precio) y los derechos del trámite (art. 5.3.2.1 num. 5.º). Para dación en pago, permuta u otro título: la contraprestación se describe según la naturaleza del negocio. La norma exige pago de retención en la fuente e impuesto, no que el título sea siempre compraventa. | art. 5.3.2.1 numeral 5.º (retención en la fuente e impuesto: condicionales al tipo de negocio) |
| **Firma en este documento** | Transferente **y** adquirente (o representantes legales si son PJ) | art. 5.3.2.1 |
| **Firma en el FUR (Anexo 46)** | Vendedor **y** comprador | `fur-diligenciamiento.md` §Firmas |
| **Casilla FUR numeral 3** | **2** (Traspaso) | `REGLAS-NUMERAL-3-TRES-CAPAS.md` Tabla 1 |
| **Objeto del mandato** | `TRASPASO` | `REGLAS-OBJETO-TRES-CAPAS.md` Tabla 1 |

### 4.2 Escenario B — Transferencia unilateral leasing → locatario (art. 5.3.2.2)

| Dimensión | Detalle | Verificación |
|-----------|---------|-------------|
| **Transferente** | Entidad financiera (establecimiento bancario, cía. de financiamiento o leasing) propietaria registrada en RUNT | ✅ RUNT |
| **Adquirente / locatario** | La resolución exime dos cosas específicas: (4.ª) «Presentación del locatario (Comprador)» ante el OT para el trámite, y (5.ª) «Firma del formato único de solicitud de trámites» (FUR/Anexo 46). **La norma no exime expresamente la firma del locatario en este documento soporte privado.** La decisión de estructurarlo como acto unilateral —sin firma del locatario— es una decisión de diseño del documento basada en la naturaleza unilateral del acto jurídico; no es una exención textual adicional de la resolución. El nombre del locatario aparece en la cláusula declarativa interna y en la observación del FUR (párrafo 23). | art. 5.3.2.2, excepciones 4.ª y 5.ª (texto literal del PDF) |
| **Trigger de la transferencia** | Opción de compra ejercida, automática o terminación del contrato de leasing | art. 5.3.2.2 párr. 1.º |
| **Soporte principal** | Copia del contrato de leasing | art. 5.3.2.2 párr. 1.º |
| **Soporte adicional** | Declaración de terminación / ejercicio de opción de compra — **NO se requiere si la opción es automática** (solo el contrato) | art. 5.3.2.2 Parágrafo 1.º |
| **RTM** | **NO se requiere** | art. 5.3.2.2, excepción 1.ª |
| **QR / guarismos / improntas** | **NO se requieren** | art. 5.3.2.2, excepción 2.ª |
| **Paz y salvo de infracciones** | **NO se requiere** | art. 5.3.2.2, excepción 3.ª |
| **Servicio público pasajeros/mixto** | No se exige paz y salvo de empresa ni cesión/vinculación | art. 5.3.2.2 Parágrafo 2.º |
| **Fusión por absorción de financieras** | Las mismas exenciones aplican | art. 5.3.2.2 Parágrafo 3.º |
| **Precio en el documento** | **No aplica.** La transferencia unilateral no es una compraventa entre partes del documento. No incluir campo de precio. | art. 5.3.2.2 (acto unilateral) |
| **Firma en este documento** | Solo la entidad financiera (transferente / representante legal). Esta es una decisión de diseño del instrumento, no una exención textual de la resolución sobre el documento privado. | Diseño normativo coherente con art. 5.3.2.2 |
| **Firma en el FUR (Anexo 46)** | Solo el vendedor (entidad financiera). El locatario **no firma el FUR** — exención textual del art. 5.3.2.2 excepción 5.ª. | art. 5.3.2.2, excepción 5.ª; `fur-diligenciamiento.md` §Firmas |
| **Casilla FUR numeral 3** | **2** (Traspaso) | `REGLAS-NUMERAL-3-TRES-CAPAS.md` Tabla 1 |
| **Observación FUR párrafo 23** | `Traspaso unilateral por leasing a {{locatario_nombre}}., tipo de documento {{locatario_tipo_doc}}, número de documento {{locatario_no_doc}}.` | `REGLAS-NUMERAL-3-TRES-CAPAS.md` Tabla 1 |
| **Objeto del mandato** | `TRASPASO` (no «UNILATERAL» ni «LEASING») | `REGLAS-OBJETO-TRES-CAPAS.md` Tabla 1 |

> **Diferencia entre documento soporte y FUR en el Escenario B:**
>
> El «Documento de Transferencia de Dominio» (esta plantilla) es el **instrumento privado** que sustenta la transferencia. El FUR (Anexo 46) es el **formulario oficial del RNA** que el OT procesa.
>
> La resolución (art. 5.3.2.2) establece dos exenciones textuales:
> - Excepción 4.ª: «Presentación del locatario (Comprador)» — exime su comparecencia ante el OT.
> - Excepción 5.ª: «Firma del formato único de solicitud de trámites, por parte del locatario (Comprador)» — exime su firma en el **FUR**.
>
> La norma no exime explícitamente la firma del locatario en este documento soporte privado. Estructurarlo sin esa firma es una **decisión de diseño** justificada en que el acto es unilateral (la entidad financiera transfiere por su sola voluntad) y en que el locatario ya no necesita aceptar formalmente algo que le corresponde por contrato. Esta decisión debe quedar documentada como tal y puede ser revisada por el Organismo de Tránsito receptor; FLIT no puede garantizar que el OT la acepte sin observación.
>
> Nota sobre art. 5.1.1: la resolución prohíbe a los OTs exigir requisitos **adicionales a los del trámite** establecidos en la norma, pero eso aplica a los requisitos del trámite RNA, no al contenido interno de los instrumentos privados que las partes presenten como soporte.

### 4.3 Escenario C — Transferencia de entidad financiera a tercero (art. 5.3.2.1 sin exenciones)

| Dimensión | Detalle | Verificación |
|-----------|---------|-------------|
| **Transferente** | Entidad financiera propietaria registrada en RUNT | ✅ RUNT |
| **Adquirente** | Tercero que **no es el locatario** del contrato de leasing histórico | ✅ RUNT |
| **Exenciones del art. 5.3.2.2** | **Ninguna aplica.** El tercero es tratado como comprador ordinario del art. 5.3.2.1. | art. 5.3.2.2 a contrario |
| **RTM** | **Sí se requiere** (no hay exención) | art. 5.3.2.1 |
| **QR / guarismos / improntas** | **Sí se requieren** | art. 5.3.2.1 |
| **Paz y salvo de infracciones** | **Sí se requiere** | art. 5.3.2.1 numeral 4.º |
| **SOAT** | Vigente. En remolques y semirremolques su no exigibilidad la resuelve el OT; el art. 5.3.2.1 no la consagra textualmente (la Ley 488/1998 exime **impuesto**, no SOAT, en este artículo) | art. 5.3.2.1 |
| **Soportes** | FUR + este documento + QR/guarismos/improntas | art. 5.3.2.1 párr. 1.º |
| **Precio** | Declarado en el documento; sujeto a retención en la fuente | art. 5.3.2.1 numeral 5.º |
| **Firma en este documento** | Entidad financiera **y** tercero adquirente (o representantes legales si son PJ) | art. 5.3.2.1 |
| **Firma en el FUR (Anexo 46)** | Vendedor **y** comprador (ambos) | art. 5.3.2.1; `fur-diligenciamiento.md` §Firmas |
| **Casilla FUR numeral 3** | **2** (Traspaso) | `REGLAS-NUMERAL-3-TRES-CAPAS.md` Tabla 1 |
| **Objeto del mandato** | `TRASPASO` | `REGLAS-OBJETO-TRES-CAPAS.md` Tabla 1 |

---

## 5. Catálogo de variables parametrizables

Las variables usan la notación `{{variable}}`. Ninguna contiene datos reales. Se resuelven en tiempo de generación a partir del expediente del trámite o de los datos ingresados en el formulario del módulo.

### 5.1 Variables del vehículo (obligatorias en los tres escenarios)

| Variable | Descripción | Requiere verificación externa |
|----------|-------------|-------------------------------|
| `{{placa}}` | Placa del vehículo (formato AAA-000 o el asignado por RUNT) | ✅ RUNT — confrontar vs licencia de tránsito |
| `{{marca}}` | Marca del vehículo | ✅ RUNT |
| `{{linea}}` | Línea / modelo comercial | ✅ RUNT |
| `{{modelo_anio}}` | Año modelo | ✅ RUNT |
| `{{clase_vehiculo}}` | Clase (automóvil, camioneta, bus, camión, etc.) | ✅ RUNT |
| `{{tipo_carroceria}}` | Tipo de carrocería | ✅ RUNT |
| `{{color}}` | Color(es) registrados | ✅ RUNT |
| `{{no_motor}}` | Número de motor | ✅ RUNT / improntas |
| `{{no_chasis}}` | Número de chasis o VIN | ✅ RUNT / improntas |
| `{{no_serie}}` | Número de serie (si aplica al tipo de vehículo) | ✅ RUNT |
| `{{servicio}}` | Tipo de servicio (particular, público, oficial, etc.) | ✅ RUNT |
| `{{no_licencia_transito}}` | Número de licencia de tránsito vigente | ✅ RUNT |
| `{{organismo_transito}}` | Nombre y ciudad del OT donde reposa la matrícula | ✅ RUNT |

### 5.2 Variables del transferente (obligatorias en los tres escenarios)

| Variable | Descripción | Aplica a |
|----------|-------------|---------|
| `{{transferente_tipo_persona}}` | `PN` (persona natural) o `PJ` (persona jurídica) | Ambos |
| `{{transferente_nombre_razon_social}}` | Nombre completo (PN) o razón social (PJ) | Ambos |
| `{{transferente_tipo_doc}}` | CC / CE / NIT / Pasaporte / según catálogo RUNT | Ambos |
| `{{transferente_no_doc}}` | Número de documento | Ambos |
| `{{transferente_digito_verificacion}}` | Dígito de verificación del NIT | Solo PJ |
| `{{transferente_domicilio}}` | Ciudad de domicilio | Ambos |
| `{{transferente_representante_legal}}` | Nombre del representante legal | Solo PJ |
| `{{transferente_cc_rl}}` | CC del representante legal | Solo PJ |

### 5.3 Variables del adquirente (Escenarios A y C únicamente)

> ⚠️ **Escenario B:** estas variables **no se incluyen** en el cuerpo del documento. No generar campos vacíos ni marcadores de posición para el locatario. Un espacio de firma vacío puede inducir al OT o al mandatario a exigirla, convirtiendo una exención del art. 5.3.2.2 en una observación de trámite. Ver §9.2.

| Variable | Descripción | Aplica a |
|----------|-------------|---------|
| `{{adquirente_tipo_persona}}` | `PN` o `PJ` | Ambos |
| `{{adquirente_nombre_razon_social}}` | Nombre completo o razón social | Ambos |
| `{{adquirente_tipo_doc}}` | CC / CE / NIT / Pasaporte | Ambos |
| `{{adquirente_no_doc}}` | Número de documento | Ambos |
| `{{adquirente_digito_verificacion}}` | DV del NIT | Solo PJ |
| `{{adquirente_domicilio}}` | Ciudad de domicilio | Ambos |
| `{{adquirente_representante_legal}}` | Nombre del representante legal | Solo PJ |
| `{{adquirente_cc_rl}}` | CC del representante legal | Solo PJ |

### 5.4 Variables del negocio (Escenarios A y C únicamente)

| Variable | Descripción | Condición |
|----------|-------------|-----------|
| `{{titulo_juridico}}` | Tipo de negocio traslaticio: `COMPRAVENTA` / `DACION_EN_PAGO` / `PERMUTA` / `DONACION` / `OTRO` | Siempre presente — determina si aplican precio y forma de pago |
| `{{descripcion_titulo}}` | Descripción libre del negocio cuando `{{titulo_juridico}}` = `OTRO` | Solo si `OTRO` |
| `{{precio_letras}}` | Valor pactado en letras (pesos colombianos) | Solo para `COMPRAVENTA` y negocios onerosos |
| `{{precio_numeros}}` | Valor pactado en números (COP) | Solo para `COMPRAVENTA` y negocios onerosos |
| `{{contraprestacion_descripcion}}` | Descripción de la contraprestación en negocios no monetarios (p. ej. bien entregado en permuta) | Solo para negocios onerosos no monetarios |
| `{{forma_pago}}` | Descripción de la forma de pago o entrega de la contraprestación | Solo cuando aplique según `{{titulo_juridico}}` |
| `{{asume_retencion_fuente}}` | Parte que asume el pago de la retención en la fuente por la enajenación: `TRANSFERENTE` / `ADQUIRENTE` / `SEGUN_LEY` | Siempre en A y C — el OT valida el **pago**, no quién lo pactó (art. 5.3.2.1 num. 5.º) |
| `{{asume_derechos_tramite}}` | Parte que asume los derechos del trámite ante el Ministerio de Transporte, la tarifa RUNT y los derechos del Organismo de Tránsito: `TRANSFERENTE` / `ADQUIRENTE` / `COMPARTIDOS` | Siempre en A y C |
| `{{asume_impuesto_vehiculo}}` | Parte que asume el impuesto sobre vehículos automotores: `TRANSFERENTE` / `ADQUIRENTE` / `SEGUN_LEY`. **No aplica a remolques y semirremolques** (exentos — Ley 488/1998, art. 5.3.2.1 num. 5.º *in fine*) | Siempre en A y C, salvo remolque/semirremolque |
| `{{ciudad_firma}}` | Ciudad donde se suscribe el documento | Siempre |
| `{{fecha_firma_dia}}` | Día de firma (dd) | Siempre |
| `{{fecha_firma_mes}}` | Mes de firma en letras | Siempre |
| `{{fecha_firma_anio}}` | Año de firma (AAAA) | Siempre |
| `{{modo_firma}}` | Modo de firma del documento: `MANUSCRITA` (único valor vigente) / `ESTAMPADA` (**diferido**, §9.4) | Siempre — en los tres escenarios. Ver §9.0 |

### 5.5 Variables exclusivas del Escenario B (leasing unilateral)

| Variable | Descripción | Uso en el documento |
|----------|-------------|---------------------|
| `{{locatario_nombre}}` | Nombre / razón social del locatario | Cláusula declarativa interna + observación FUR |
| `{{locatario_tipo_doc}}` | Tipo de documento del locatario | Cláusula declarativa interna + observación FUR |
| `{{locatario_no_doc}}` | Número de documento del locatario | Cláusula declarativa interna + observación FUR |
| `{{no_contrato_leasing}}` | Número / referencia del contrato de leasing | Cláusula segunda |
| `{{fecha_terminacion_leasing}}` | Fecha de terminación del contrato o ejercicio de opción | Cláusula segunda |
| `{{tipo_opcion_compra}}` | `EJERCIDA` / `AUTOMATICA` / `TERMINACION_CONTRATO` | Determina qué soportes se adjuntan (Parágrafo 1.º) |

> **Nota:** `{{locatario_nombre}}`, `{{locatario_tipo_doc}}` y `{{locatario_no_doc}}` alimentan la observación del FUR (párrafo 23), no el bloque de firmas del documento.

---

## 6. Validaciones — Bloqueantes de formulario y avisos de pre-validación

Las validaciones se clasifican en dos tipos:

- **VB (Bloqueante de formulario):** FLIT puede verificar con los datos ingresados en el formulario, sin integración externa en tiempo real. El sistema rechaza la generación si falla.
- **VA (Advisory / pre-validación):** Requiere integración con sistemas externos (RUNT, SOAT, SIMIT, RUES). FLIT puede pre-validarlos si tiene interoperabilidad disponible; si no, los muestra como aviso informativo. La verificación definitiva la realiza el OT al momento del trámite. El documento puede generarse aunque el aviso esté pendiente, pero el usuario debe ser informado.

> ℹ️ FLIT no puede garantizar la disponibilidad ni el resultado de las consultas externas (RUNT, RUES, SOAT, SIMIT, RTM). El disclaimer del §1 aplica a todas las validaciones VA.

### 6.1 Comunes a los tres escenarios

| Código | Tipo | Validación | Artículo |
|--------|------|-----------|---------|
| VB-01 | **Advisory (VA)** | El vehículo tiene matrícula vigente (no cancelada, no inactiva) en el RUNT — validación definitiva a cargo del OT | art. 5.3.2.1 párr. 1.º |
| VB-02 | **Bloqueante** | La placa tiene formato válido (longitud, caracteres alfanuméricos) — verificable en el formulario | art. 5.1.8 |
| VB-03 | **Advisory (VA)** | El transferente está inscrito en RUNT — validación definitiva a cargo del OT | art. 5.3.2.1 párr. 1.º |
| VB-04 | **Advisory (VA)** | Si el transferente es PJ: inscripción RUES verificable — el OT consulta directamente (art. 5.1.5) | art. 5.1.5 |
| VB-05 | **Bloqueante** | El escenario seleccionado es exactamente uno de A, B o C — verificable en el formulario | §3 este dictamen |
| VB-06 | **Bloqueante** | Transferente y adquirente no tienen el mismo número de documento de identidad — verificable en el formulario | Principio de derecho privado |
| VB-07 | **Bloqueante** | El usuario declaró que **ninguna** de las once condiciones especiales de los arts. 5.3.2.3 a 5.3.2.13 aplica a la operación (§4.0). Si declara al menos una, el generador rechaza con el artículo citado y no emite documento — verificable en el formulario | arts. 5.3.2.3 a 5.3.2.13; §4.0 este dictamen |

### 6.2 Escenario A

| Código | Tipo | Validación | Artículo |
|--------|------|-----------|---------|
| VB-A-01 | **Advisory (VA)** | El adquirente está inscrito en RUNT — validación definitiva a cargo del OT | art. 5.3.2.1 párr. 1.º |
| VB-A-02 | **Advisory (VA)** | Si el adquirente es PJ: inscripción RUES verificable | art. 5.1.5 |
| VB-A-03 | **Advisory (VA)** | Sin medidas judiciales que impidan el traspaso — verificación a cargo del OT mediante RUNT | art. 5.3.2.1 numeral 3.º |
| VB-A-04 | **Bloqueante** | Si el usuario declara gravamen activo: debe declarar también si existe levantamiento o autorización del beneficiario — verificable en formulario con declaración del usuario | art. 5.3.2.1 numeral 3.º |
| VB-A-05 | **Advisory (VA)** | SOAT vigente — verificación a cargo del OT. En remolques y semirremolques el art. 5.3.2.1 no consagra exención textual de SOAT; su no exigibilidad la resuelve el OT (no invocar la Ley 488/1998, que exime impuesto, no SOAT, en este artículo) | art. 5.3.2.1 numeral 4.º |
| VB-A-06 | **Bloqueante** | Si `{{titulo_juridico}}` = `COMPRAVENTA`: precio declarado en letras y números. Para otros títulos: la contraprestación se describe según la naturaleza del negocio — verificable en formulario | art. 5.3.2.1 numeral 5.º — aplica a negocios onerosos |
| VB-A-07 | **Bloqueante** | `{{titulo_juridico}}` declarado (no vacío ni ambiguo) | art. 5.3.2.1 numeral 1.º — «documento o declaración en el que conste la transferencia» |
| VB-A-08 | **Advisory (VA)** | Pago de la retención en la fuente por la enajenación — el interesado adjunta copia de los recibos ante el OT; FLIT no lo verifica ni lo liquida. El documento se genera con el aviso pendiente | art. 5.3.2.1 numeral 5.º |
| VB-A-09 | **Advisory (VA)** | Pago de los derechos del trámite (Ministerio de Transporte + tarifa RUNT + derechos del OT) — validación en RUNT a cargo del OT | art. 5.3.2.1 numeral 5.º |
| VB-A-10 | **Advisory (VA)** | Pago del impuesto sobre vehículos automotores — a cargo del OT ante la entidad territorial. **No aplica a remolques ni semirremolques** (exentos — Ley 488/1998) | art. 5.3.2.1 numeral 5.º e inciso final |

### 6.3 Escenario B

| Código | Tipo | Validación | Artículo |
|--------|------|-----------|---------|
| VB-B-01 | **Bloqueante** | El transferente declara ser entidad financiera (establecimiento bancario, cía. de financiamiento o leasing) — declaración en formulario; verificación RUNT a cargo del OT | art. 5.3.2.2 párr. 1.º |
| VB-B-02 | **Bloqueante** | Existe número de contrato de leasing declarado (no vacío) | art. 5.3.2.2 párr. 1.º |
| VB-B-03 | **Bloqueante** | El tipo de opción de compra está declarado (`EJERCIDA` / `AUTOMATICA` / `TERMINACION_CONTRATO`) | art. 5.3.2.2 y Parágrafo 1.º |
| VB-B-04 | **Bloqueante** | El destinatario (`{{locatario_nombre}}` + `{{locatario_no_doc}}`) está declarado — FLIT no puede verificar si es el locatario real del contrato; el OT lo valida | art. 5.3.2.2 párr. 1.º |
| VB-B-05 | **Bloqueante** | El formulario de generación no incluye campos de precio — verificable en la plantilla del escenario B | Diseño de instrumento; art. 5.3.2.2 acto unilateral |
| VB-B-06 | **Advisory (VA)** | Pago de la retención en la fuente, del impuesto sobre vehículos y de los derechos del trámite — **el art. 5.3.2.2 NO exime el numeral 5.º del art. 5.3.2.1**; sus cinco exenciones son RTM, QR/improntas, paz y salvo, presentación del locatario y firma del FUR. Verificación a cargo del OT | art. 5.3.2.1 numeral 5.º; art. 5.3.2.2 a contrario |

### 6.4 Escenario C

| Código | Tipo | Validación | Artículo |
|--------|------|-----------|---------|
| VB-C-01 | **Bloqueante** | El adquirente tiene número de documento distinto al `{{locatario_no_doc}}` declarado en el historial leasing, si se conoce — verificable en formulario si el dato está disponible | art. 5.3.2.2 a contrario |
| VB-C-02 | **Advisory (VA)** | El adquirente está inscrito en RUNT — validación a cargo del OT | art. 5.3.2.1 |
| VB-C-03 | **Advisory (VA)** | SOAT vigente — verificación a cargo del OT. En remolques y semirremolques el art. 5.3.2.1 no consagra exención textual de SOAT | art. 5.3.2.1 |
| VB-C-04 | **Advisory (VA)** | RTM vigente para el tipo de vehículo — sin exención; verificación a cargo del OT | art. 5.3.2.1 (sin exención de 5.3.2.2) |
| VB-C-05 | **Advisory (VA)** | Sin medidas judiciales que impidan el traspaso — verificación a cargo del OT | art. 5.3.2.1 numeral 3.º |
| VB-C-06 | **Advisory (VA)** | QR / certificación de guarismos / improntas — sin exención; a cargo del OT | art. 5.3.2.1 numeral 1.º |
| VB-C-07 | **Bloqueante** | El documento generado no invoca exenciones del art. 5.3.2.2 — verificable en la plantilla del escenario C | art. 5.3.2.2 a contrario |
| VB-C-08 | **Advisory (VA)** | Pago de la retención en la fuente por la enajenación — recibos aportados por el interesado; verificación a cargo del OT | art. 5.3.2.1 numeral 5.º |
| VB-C-09 | **Advisory (VA)** | Pago de los derechos del trámite (Ministerio de Transporte + tarifa RUNT + derechos del OT) — validación en RUNT a cargo del OT | art. 5.3.2.1 numeral 5.º |
| VB-C-10 | **Advisory (VA)** | Pago del impuesto sobre vehículos automotores — a cargo del OT. **No aplica a remolques ni semirremolques** (Ley 488/1998) | art. 5.3.2.1 numeral 5.º e inciso final |

---

## 7. Encabezado común (todos los escenarios)

El encabezado se incluye en todos los documentos generados, antes del cuerpo específico del escenario.

```
DOCUMENTO DE TRANSFERENCIA DE DOMINIO DE VEHÍCULO AUTOMOTOR

Ciudad:     {{ciudad_firma}}
Fecha:      {{fecha_firma_dia}} de {{fecha_firma_mes}} de {{fecha_firma_anio}}
Placa:      {{placa}}
Escenario:  [A — Traspaso ordinario / B — Transferencia unilateral leasing /
             C — Transferencia a tercero (sin exenciones art. 5.3.2.2)]
```

---

## 8. Cláusulas modelo por escenario

Las cláusulas son indicativas y neutrales. Los textos entre corchetes `[...]` son instrucciones de renderización condicional que el generador resuelve; no deben aparecer en el PDF final.

---

### 8.1 Escenario A — Traspaso ordinario (art. 5.3.2.1)

**COMPARECIENTES**

Que entre los suscritos, de una parte, **{{transferente_nombre_razon_social}}**, identificado(a) con {{transferente_tipo_doc}} No. {{transferente_no_doc}}[, DV {{transferente_digito_verificacion}}], [actuando en calidad de representante legal **{{transferente_representante_legal}}**, identificado(a) con C.C. No. {{transferente_cc_rl}}] (en adelante «EL TRANSFERENTE»), con domicilio en {{transferente_domicilio}}; y de otra parte, **{{adquirente_nombre_razon_social}}**, identificado(a) con {{adquirente_tipo_doc}} No. {{adquirente_no_doc}}[, DV {{adquirente_digito_verificacion}}], [actuando en calidad de representante legal **{{adquirente_representante_legal}}**, identificado(a) con C.C. No. {{adquirente_cc_rl}}] (en adelante «EL ADQUIRENTE»), con domicilio en {{adquirente_domicilio}};

hemos acordado celebrar el presente documento de transferencia de dominio, que se regirá por las siguientes cláusulas:

---

**PRIMERA — IDENTIFICACIÓN DEL VEHÍCULO**

Las partes declaran que el vehículo objeto de la presente transferencia tiene las siguientes características registradas en el RNA:

| Campo | Valor |
|-------|-------|
| Marca | `{{marca}}` |
| Línea | `{{linea}}` |
| Año modelo | `{{modelo_anio}}` |
| Clase | `{{clase_vehiculo}}` |
| Carrocería | `{{tipo_carroceria}}` |
| Color(es) | `{{color}}` |
| Motor No. | `{{no_motor}}` |
| Chasis / VIN No. | `{{no_chasis}}` |
| Serie No. | `{{no_serie}}` |
| Servicio | `{{servicio}}` |
| Licencia de Tránsito No. | `{{no_licencia_transito}}` |
| Organismo de Tránsito | `{{organismo_transito}}` |

---

**SEGUNDA — TRANSFERENCIA DE DOMINIO**

EL TRANSFERENTE, siendo propietario registrado del vehículo descrito en la cláusula primera, transfiere el pleno dominio, la posesión y la propiedad de dicho vehículo al ADQUIRENTE mediante **{{titulo_juridico}}** [renderizar según catálogo: «contrato de compraventa» / «dación en pago» / «permuta» / «donación» / «{{descripcion_titulo}}»], de conformidad con las normas civiles y/o mercantiles vigentes (art. 5.3.2.1 numeral 1.º de la Resolución 20233040017145 de 2023).

[Solo si `{{titulo_juridico}}` es oneroso (COMPRAVENTA u otro con precio pactado):] La contraprestación acordada es **{{precio_letras}} pesos ($ {{precio_numeros}} COP)**, que el ADQUIRENTE paga / pagará de la siguiente forma: {{forma_pago}}.

[Solo si `{{titulo_juridico}}` = PERMUTA u otro oneroso no monetario:] La contraprestación consiste en: {{contraprestacion_descripcion}}.

[Si hay gravamen activo:] La presente transferencia se realiza adjuntando el documento en el que consta el levantamiento o la autorización otorgada por el beneficiario del gravamen o limitación para continuar con el nuevo propietario, de conformidad con el art. 5.3.2.1 numeral 3.º de la Resolución 20233040017145 de 2023.

---

**TERCERA — TRADICIÓN Y ENTREGA**

EL TRANSFERENTE hace entrega material del vehículo y de todos los documentos pertinentes para que el ADQUIRENTE pueda adelantar el trámite de traspaso ante el organismo de tránsito competente del RNA, conforme a la Resolución 20233040017145 de 2023, art. 5.3.2.1.

---

**CUARTA — DECLARACIONES DEL TRANSFERENTE**

EL TRANSFERENTE declara bajo la gravedad del juramento: (i) que es el legítimo propietario del vehículo; (ii) que el vehículo no se encuentra sujeto a medidas cautelares, embargos ni limitaciones de dominio que impidan la presente transferencia, distintas de las expresamente señaladas en la cláusula segunda; y (iii) que la información suministrada al RNA es fiel reflejo de la situación jurídica y técnica del vehículo a la fecha de este documento.

---

**QUINTA — OBLIGACIONES REGISTRALES**

Las partes se obligan a adelantar, dentro de los términos legales, el trámite de traspaso ante el organismo de tránsito, presentando este documento junto con el Formato Único de Solicitud de Trámite (Anexo 46) y los demás requisitos del art. 5.3.2.1 de la Resolución 20233040017145 de 2023.

---

**SEXTA — RETENCIÓN EN LA FUENTE, IMPUESTOS Y DERECHOS DEL TRÁMITE**

Las partes declaran conocer que el Organismo de Tránsito verificará el pago de la retención en la fuente, el pago del impuesto sobre vehículos automotores y el pago de los derechos del trámite a favor del Ministerio de Transporte, de la tarifa RUNT y de los derechos del propio Organismo de Tránsito, conforme al art. 5.3.2.1 numeral 5.º de la Resolución 20233040017145 de 2023.

Para efectos internos entre las partes y sin que ello altere la obligación legal frente a la administración, la retención en la fuente será asumida por **{{asume_retencion_fuente}}**, [el impuesto sobre vehículos automotores por **{{asume_impuesto_vehiculo}}**,] y los derechos del trámite, la tarifa RUNT y los derechos del Organismo de Tránsito por **{{asume_derechos_tramite}}**.

[Solo si la clase del vehículo es remolque o semirremolque, se omite la mención del impuesto y se agrega:] Tratándose de un remolque o semirremolque, el Organismo de Tránsito no verifica el pago del impuesto sobre vehículos, por encontrarse exento conforme a la Ley 488 de 1998 (art. 5.3.2.1 numeral 5.º, inciso final).


---

### 8.2 Escenario B — Transferencia unilateral leasing → locatario (art. 5.3.2.2)

**COMPARECIENTE**

Que el suscrito, **{{transferente_nombre_razon_social}}**, identificado con {{transferente_tipo_doc}} No. {{transferente_no_doc}}, DV {{transferente_digito_verificacion}}, representado legalmente por **{{transferente_representante_legal}}**, identificado con C.C. No. {{transferente_cc_rl}} (en adelante «LA ENTIDAD FINANCIERA»), con domicilio en {{transferente_domicilio}}, actuando en ejercicio de las facultades conferidas por el contrato de leasing referenciado a continuación y en virtud del art. 5.3.2.2 de la Resolución 20233040017145 de 2023 del Ministerio de Transporte:

> **Nota de renderización:** el locatario (adquirente) **no** comparece ni firma este documento. No incluir ningún campo, espacio ni sección de firma para el locatario.

---

**PRIMERA — IDENTIFICACIÓN DEL VEHÍCULO**

[Misma estructura de tabla que §8.1 — Primera]

---

**SEGUNDA — ANTECEDENTE LEASING Y FUNDAMENTO DE LA TRANSFERENCIA**

El vehículo descrito fue entregado en leasing mediante contrato No. **{{no_contrato_leasing}}**, suscrito con **{{locatario_nombre}}**, identificado con {{locatario_tipo_doc}} No. {{locatario_no_doc}}.

Con fecha **{{fecha_terminacion_leasing}}**, se configuró la siguiente causal de transferencia:

[Renderizar según `{{tipo_opcion_compra}}`:]
- **EJERCIDA:** el locatario ejerció expresamente la opción de compra pactada en el contrato de leasing.
- **AUTOMATICA:** la opción de compra se activó automáticamente conforme a las condiciones del contrato, sin necesidad de declaración expresa del locatario (art. 5.3.2.2 Parágrafo 1.º). Para este caso, basta la copia del contrato de leasing como soporte; no se requiere declaración adicional.
- **TERMINACION_CONTRATO:** el contrato de leasing llegó a su término por cumplimiento del plazo pactado.

---

**TERCERA — TRANSFERENCIA UNILATERAL DE DOMINIO**

En virtud del art. 5.3.2.2 de la Resolución 20233040017145 de 2023, LA ENTIDAD FINANCIERA transfiere unilateralmente el pleno dominio del vehículo descrito en la cláusula primera al locatario **{{locatario_nombre}}**, de conformidad con los términos del contrato de leasing No. {{no_contrato_leasing}}. Esta transferencia se realiza en ejercicio de la facultad legal que asiste a la entidad financiera, sin que sea necesaria la comparecencia, aceptación ni firma del adquirente en este documento ni en el Formato Único de Solicitud de Trámite (Anexo 46).

---

**CUARTA — EXENCIONES NORMATIVAS APLICABLES AL TRÁMITE (art. 5.3.2.2)**

De conformidad con el art. 5.3.2.2 de la Resolución 20233040017145 de 2023, en este trámite no se exigen:
- Revisión Técnico-Mecánica y de emisiones contaminantes (excepción 1.ª).
- Presentación de imagen del código QR, certificación del fabricante/ensamblador/importador ni improntas de guarismos de identificación (excepción 2.ª).
- Validación de paz y salvo por concepto de multas por infracciones de tránsito (excepción 3.ª).
- Presentación del locatario (Comprador) ante el Organismo de Tránsito (excepción 4.ª).
- Firma del Formato Único de Solicitud de Trámite (Anexo 46) por parte del locatario (Comprador) (excepción 5.ª).

> **Nota:** las exenciones anteriores son las establecidas literalmente por el art. 5.3.2.2 para el trámite ante el OT. Este documento soporte privado se estructura sin firma del locatario porque el acto es unilateral; esa es una decisión de diseño del instrumento, no una exención expresa de la norma sobre documentos privados.

---

**QUINTA — SOPORTES APORTADOS**

Se adjuntan como soporte de esta transferencia:
- Copia del contrato de leasing No. {{no_contrato_leasing}}.
- [Solo si `{{tipo_opcion_compra}}` es `EJERCIDA` o `TERMINACION_CONTRATO`:] Declaración de terminación del contrato / ejercicio de la opción de compra.

---

### 8.3 Escenario C — Transferencia de entidad financiera a tercero (art. 5.3.2.1 sin exenciones)

**COMPARECIENTES**

Que entre los suscritos, de una parte, **{{transferente_nombre_razon_social}}** (entidad financiera), identificada con {{transferente_tipo_doc}} No. {{transferente_no_doc}}, DV {{transferente_digito_verificacion}}, representada legalmente por **{{transferente_representante_legal}}**, identificado con C.C. No. {{transferente_cc_rl}} (en adelante «EL TRANSFERENTE»), con domicilio en {{transferente_domicilio}}; y de otra parte, **{{adquirente_nombre_razon_social}}**, identificado(a) con {{adquirente_tipo_doc}} No. {{adquirente_no_doc}}[, DV {{adquirente_digito_verificacion}}], [actuando en calidad de representante legal **{{adquirente_representante_legal}}**, identificado(a) con C.C. No. {{adquirente_cc_rl}}] (en adelante «EL ADQUIRENTE»), con domicilio en {{adquirente_domicilio}};

---

**PRIMERA — IDENTIFICACIÓN DEL VEHÍCULO**

[Misma estructura de tabla que §8.1 — Primera]

---

**SEGUNDA — ANTECEDENTE DE DOMINIO Y RÉGIMEN APLICABLE**

EL TRANSFERENTE es el propietario registrado del vehículo descrito ante el RNA. El ADQUIRENTE es un tercero distinto del locatario histórico del contrato de leasing anterior. En consecuencia, la presente operación se rige íntegramente por el art. 5.3.2.1 de la Resolución 20233040017145 de 2023, **sin que apliquen las exenciones previstas en el art. 5.3.2.2**, las cuales son exclusivas del locatario beneficiario de la opción de compra.

---

**TERCERA — TRANSFERENCIA DE DOMINIO**

EL TRANSFERENTE transfiere el pleno dominio, la posesión y la propiedad del vehículo al ADQUIRENTE mediante **{{titulo_juridico}}** [renderizar según catálogo], de conformidad con las normas civiles y/o mercantiles vigentes (art. 5.3.2.1 numeral 1.º de la Resolución 20233040017145 de 2023).

[Solo si `{{titulo_juridico}}` es oneroso con precio pactado:] La contraprestación acordada es **{{precio_letras}} pesos ($ {{precio_numeros}} COP)**, que el ADQUIRENTE paga / pagará de la siguiente forma: {{forma_pago}}.

[Solo si `{{titulo_juridico}}` es oneroso no monetario:] La contraprestación consiste en: {{contraprestacion_descripcion}}.

---

**CUARTA — DECLARACIONES DEL TRANSFERENTE**

EL TRANSFERENTE declara bajo la gravedad del juramento: (i) que es el legítimo propietario del vehículo; (ii) que el vehículo no se encuentra sujeto a medidas que impidan el traspaso, distintas de las expresamente informadas; y (iii) que la información registrada en el RNA es fiel reflejo de la situación del vehículo.

---

**QUINTA — REQUISITOS PLENOS DEL TRÁMITE**

Las partes reconocen que este trámite exige, además de este documento y el FUR (Anexo 46), los requisitos del art. 5.3.2.1 de la Resolución 20233040017145 de 2023, incluyendo: QR / certificación de guarismos / improntas, SOAT vigente, RTM vigente (si aplica al tipo de vehículo), verificación de infracciones en SIMIT y, si el vehículo tiene gravamen activo, levantamiento o autorización del beneficiario del gravamen. El Organismo de Tránsito realiza estas validaciones al momento del trámite; este documento no las certifica ni las reemplaza.

---

**SEXTA — RETENCIÓN EN LA FUENTE, IMPUESTOS Y DERECHOS DEL TRÁMITE**

Las partes declaran conocer que el Organismo de Tránsito verificará el pago de la retención en la fuente, el pago del impuesto sobre vehículos automotores y el pago de los derechos del trámite a favor del Ministerio de Transporte, de la tarifa RUNT y de los derechos del propio Organismo de Tránsito, conforme al art. 5.3.2.1 numeral 5.º de la Resolución 20233040017145 de 2023.

Para efectos internos entre las partes y sin que ello altere la obligación legal frente a la administración, la retención en la fuente será asumida por **{{asume_retencion_fuente}}**, [el impuesto sobre vehículos automotores por **{{asume_impuesto_vehiculo}}**,] y los derechos del trámite, la tarifa RUNT y los derechos del Organismo de Tránsito por **{{asume_derechos_tramite}}**.

[Solo si la clase del vehículo es remolque o semirremolque, se omite la mención del impuesto y se agrega:] Tratándose de un remolque o semirremolque, el Organismo de Tránsito no verifica el pago del impuesto sobre vehículos, por encontrarse exento conforme a la Ley 488 de 1998 (art. 5.3.2.1 numeral 5.º, inciso final).


---

## 9. Bloque de firmas por escenario

### 9.0 Modo de firma — `{{modo_firma}}`

| Variable | Valor vigente | Estado |
|----------|---------------|--------|
| `{{modo_firma}}` | **`MANUSCRITA`** — único valor admitido en el alcance actual | Vigente |
| `{{modo_firma}}` | `ESTAMPADA` — firma electrónica del baúl estampada en el bloque | **Diferido.** Ver §9.4 |

**Qué significa `MANUSCRITA`:** el PDF se emite con líneas de firma en blanco (`______`), nombre de la parte y número de documento debajo. **Ninguna leyenda de firma electrónica, ningún sello de baúl, ningún sello de validación de identidad, en ningún bloque y en ningún escenario, aunque exista firma custodiada vigente de esa persona.** El documento se imprime, se firma de puño y letra y se digitaliza para acompañar el FUR.

#### 9.0.1 Por qué la norma **sí** admitiría la firma electrónica

Se deja constancia expresa de que la restricción de §9.0 **no** proviene de la resolución:

1. **La resolución no prescribe la forma de la firma en este instrumento.** En todo el capítulo, «firma» aparece como requisito una sola vez: el art. 5.3.2.2 num. 5.º, que precisamente **exime** al locatario de firmar el FUR. Sobre el título de dominio, el art. 5.3.2.1 num. 1.º remite a «las exigencias de las **normas civiles y/o mercantiles**» — es decir, al régimen general, no a un formato de firma propio del RNA.
2. **Equivalencia funcional.** El régimen general al que remite el art. 5.3.2.1 num. 1.º incluye la Ley 527 de 1999: el requisito de firma se entiende satisfecho por un método confiable que identifique al iniciador e indique que aprueba el contenido (art. 7.º), y el mensaje de datos no pierde efecto jurídico por su soporte (art. 5.º) y es admisible como prueba (art. 10.º), reglamentado por el Decreto 2364 de 2012.
3. **El OT no puede exigir firma autógrafa.** El art. 5.1.1 dispone que «ningún Organismo de Tránsito podrá, en la realización de los trámites aquí previstos, exigir requisitos diferentes a los establecidos en el presente capítulo». Exigir manuscrito sobre un título que la norma solo describe como «contrato de compraventa, documento o declaración» sería un requisito adicional.
4. **La práctica del RNA lo respalda.** Ya circulan ante OTs documentos de esta familia con firma electrónica.

#### 9.0.2 Por qué, aun así, este módulo no estampa

El riesgo que cierra la decisión **no es el Organismo de Tránsito: es el consentimiento.**

En los documentos donde FLIT sí estampa la firma custodiada, la estampa ocurre dentro de un expediente de trámite con la parte identificada, validación de identidad y consentimiento trazable a ese negocio concreto. **Este módulo genera sin trámite y en lote.** Estampar la firma custodiada de una persona en un **título traslaticio de dominio** —el instrumento que transfiere la propiedad, con precio y condiciones que esa persona no confirmó dentro de ningún expediente— produce un documento cuya autoría podría desconocerse, con la consecuencia procesal del **art. 244 del CGP** (tacha del documento privado), y deja a FLIT en posición de custodio que estampó sin mandato acreditable para ese negocio.

La restricción es, entonces, de **evidencia de consentimiento**, no de derecho probatorio ni de derecho del tránsito. Habilitar `ESTAMPADA` exige, antes que código, definir qué evidencia de consentimiento acredita que el titular quiso **ese** negocio: sin esa definición, el modo estampado no debe implementarse.

#### 9.0.3 Regla estructural que sobrevive al modo de firma

> **El escenario decide cuántos bloques de firma existen. El modo de firma solo decide qué va dentro de un bloque que ya existe.**

Ningún cambio de `{{modo_firma}}` puede crear ni resucitar el bloque del adquirente en el Escenario B (§9.2 y §10 regla #4). La regla es anterior e independiente del modo.


### 9.1 Escenario A — Firmas bilaterales

```
En {{ciudad_firma}}, a los {{fecha_firma_dia}} días del mes de {{fecha_firma_mes}} de {{fecha_firma_anio}}.


TRANSFERENTE                                ADQUIRENTE
_______________________________             _______________________________
{{transferente_nombre_razon_social}}        {{adquirente_nombre_razon_social}}
{{transferente_tipo_doc}} {{transferente_no_doc}}    {{adquirente_tipo_doc}} {{adquirente_no_doc}}
[RL: {{transferente_representante_legal}}]  [RL: {{adquirente_representante_legal}}]
[C.C. RL: {{transferente_cc_rl}}]          [C.C. RL: {{adquirente_cc_rl}}]
```

### 9.2 Escenario B — Solo firma de la entidad financiera

> ⚠️ **Regla estructural — el bloque del adquirente/locatario NO SE INSTANCIA.**
>
> En el Escenario B el documento tiene **un solo bloque de firma**: el de la entidad financiera. El bloque del adquirente/locatario **no existe**. No es un bloque vacío, no es un bloque oculto, no es un bloque suprimido en render, no es un bloque con visibilidad condicional ni con ancho cero: **nunca entra al árbol del documento**. La estructura de firmas del Escenario B se construye con un elemento, no con dos de los cuales uno se apaga.
>
> **Formulaciones prohibidas, todas equivalentes al defecto:** columna del adquirente con contenido vacío; celda de tabla en blanco; línea `______` sin rótulo; rótulo `ADQUIRENTE` o `LOCATARIO` sin línea; bloque generado y luego condicionado a no mostrarse; bloque con texto de marca de agua o «no requiere firma»; espacio reservado «para simetría visual» del bloque de dos columnas.
>
> **Por qué:** (a) el art. 5.3.2.2 excepción 4.ª exime la presentación del locatario ante el OT y la excepción 5.ª exime su firma **en el FUR**; (b) el acto es unilateral por naturaleza jurídica — la entidad financiera transfiere por su sola voluntad, sin que el locatario deba aceptar formalmente. Un hueco de firma, aun vacío, es una **invitación a que el OT o el mandatario lo exijan**, y convierte una exención de la norma en una observación de trámite.
>
> **Trampa de implementación advertida.** El componente de bloque de firma reutilizable del repositorio produce **por defecto** «espacio en blanco para firma manuscrita» cuando no hay firma que estampar. Reutilizarlo tal cual en el Escenario B genera exactamente el hueco prohibido. El generador del Escenario B **no debe apoyarse en la cascada por defecto de ese componente**: debe construir la estructura de un solo bloque.
>
> **Independencia del modo de firma.** Esta regla no depende de `{{modo_firma}}` (§9.0). El escenario decide cuántos bloques existen; el modo de firma solo decide qué va dentro de un bloque que ya existe. Ningún valor de `{{modo_firma}}` —presente o futuro— puede instanciar el bloque del adquirente en el Escenario B.
>
> **Verificación obligatoria — textual, no visual.** El PDF del Escenario B **no contiene los literales `ADQUIRENTE` ni `LOCATARIO` en el bloque de firmas**. La comprobación se hace extrayendo el texto del PDF y buscando esos dos literales; una inspección visual del layout **no** satisface este requisito. El nombre del locatario sí puede aparecer en la cláusula segunda (declarativa) y en la cláusula tercera; la verificación se acota al bloque de firmas.
>
> Esta decisión puede ser revisada si un OT receptor documenta un criterio distinto; en ese caso corresponde al usuario ajustarlo y a `expert-doc-engine` reemitir dictamen.

```
En {{ciudad_firma}}, a los {{fecha_firma_dia}} días del mes de {{fecha_firma_mes}} de {{fecha_firma_anio}}.


LA ENTIDAD FINANCIERA
_______________________________
{{transferente_nombre_razon_social}}
{{transferente_tipo_doc}} {{transferente_no_doc}}, DV {{transferente_digito_verificacion}}
Representante Legal: {{transferente_representante_legal}}
C.C. RL: {{transferente_cc_rl}}

Actúa en virtud del art. 5.3.2.2, Resolución 20233040017145 de 2023.
La transferencia se realiza de forma unilateral; el locatario no firma el Formato Único
de Solicitud de Trámite (art. 5.3.2.2, excepción 5.ª).
```

### 9.3 Escenario C — Firmas bilaterales (igual que A, con nota de régimen)

```
En {{ciudad_firma}}, a los {{fecha_firma_dia}} días del mes de {{fecha_firma_mes}} de {{fecha_firma_anio}}.


TRANSFERENTE (entidad financiera)           ADQUIRENTE (tercero)
_______________________________             _______________________________
{{transferente_nombre_razon_social}}        {{adquirente_nombre_razon_social}}
{{transferente_tipo_doc}} {{transferente_no_doc}}    {{adquirente_tipo_doc}} {{adquirente_no_doc}}
RL: {{transferente_representante_legal}}    [RL: {{adquirente_representante_legal}}]
C.C. RL: {{transferente_cc_rl}}            [C.C. RL: {{adquirente_cc_rl}}]

Rige íntegramente el art. 5.3.2.1.
No aplican exenciones del art. 5.3.2.2 (el adquirente no es el locatario).
```

### 9.4 Modo `ESTAMPADA` — DIFERIDO

> 🚧 **DIFERIDO — no implementado y no dictaminado en detalle.** El modo `{{modo_firma}} = ESTAMPADA`
> (estampa de la firma electrónica custodiada en el bloque de firmas) **no forma parte del alcance
> vigente**. Esta sección existe para que quien lea el anexo sepa que el contenido normativo del modo
> estampado **falta a propósito**, no por omisión.
>
> **Contenido normativo pendiente de redactar cuando se habilite el modo:**
>
> | # | Pieza pendiente | Ubicación destino |
> |---|-----------------|-------------------|
> | 1 | Definición del **sello de firma estampada**: rótulo `Cód. verificación:` en lugar de `Hash:` (el valor mostrado es un código de verificación digitado, **no** el SHA-256 del artefacto: rotularlo «Hash» afirma una integridad criptográfica que ese dato no acredita), fecha y hora de estampado, y calidad en que actúa el firmante | §9.4, cuerpo |
> | 2 | **Regla #10** de §10: prohibido emitir el sello sin la imagen de la firma (sello sin grafo = afirmación de firma sin firma) | §10 |
> | 3 | **Regla #11** de §10: prohibido usar el sello de validación de identidad como sustituto del sello de firma cuando no hay firma custodiada vigente | §10 |
> | 4 | Ítems de checklist propios del modo estampado | §13.2 |
>
> **Por qué está diferido:** el impedimento **no es normativo** — §9.0.1 acredita que la resolución y
> la Ley 527/1999 admiten la firma electrónica en este instrumento. El impedimento es la **evidencia
> de consentimiento**: este módulo genera sin expediente de trámite, y estampar una firma custodiada
> en un título traslaticio sin evidencia de que el titular quiso ese negocio expone el documento a
> tacha (CGP art. 244) — §9.0.2.
>
> **Qué lo desbloquea:** una decisión de arquitectura previa que defina **qué evidencia de
> consentimiento acredita la voluntad del titular respecto del negocio concreto** cuando no hay
> expediente. Sin esa definición, redactar estas piezas sería normalizar el riesgo, no resolverlo.
> Al habilitarse, esta sección se reemplaza por el dictamen completo, se numeran las reglas #10 y #11
> en §10 y se añaden los ítems de §13.2 — **en el mismo cambio**, no después.


---

## 10. Reglas para no mezclar roles

Las siguientes combinaciones están prohibidas en la parametrización. El generador debe rechazarlas con mensaje de error específico.

> 🚧 Las reglas **#10 y #11** (modo de firma `ESTAMPADA`) están **diferidas**. Ver §9.4. La numeración
> 10 y 11 queda **reservada**: no reutilizarla para reglas nuevas.


| # | Combinación prohibida | Motivo | Validación |
|---|----------------------|--------|-----------|
| 1 | Locatario como TRANSFERENTE en Escenario B | El locatario es el destinatario, no el origen de la transferencia unilateral | VB-B-04 |
| 2 | Mismo número de documento en TRANSFERENTE y ADQUIRENTE | Una persona no puede transferirse dominio a sí misma | VB-06 |
| 3 | Campo de precio en Escenario B | La transferencia es un acto unilateral; no corresponde declarar precio entre las partes del instrumento. El acto del art. 5.3.2.2 no es una compraventa entre la entidad y el locatario en este documento. | VB-B-05 |
| 4 | **Instanciar el bloque de firma del adquirente/locatario en Escenario B**, en cualquier forma: bloque vacío, celda en blanco, línea `______` sin rótulo, rótulo sin línea, bloque oculto, bloque suprimido en render, bloque con visibilidad condicional o espacio reservado por simetría | El bloque **no existe**: nunca entra al árbol del documento. El Escenario B se construye con **un** bloque, no con dos de los cuales uno se apaga. Un hueco de firma, aun vacío, invita al OT o al mandatario a exigir una firma que el art. 5.3.2.2 (excepciones 4.ª y 5.ª) no requiere, y convierte una exención de la norma en una observación de trámite. La regla es independiente de `{{modo_firma}}` (§9.0.3). **Verificación textual:** el PDF del Escenario B no contiene los literales `ADQUIRENTE` ni `LOCATARIO` en el bloque de firmas | §9.0.3, §9.2, §13.2 |
| 5 | Cláusula de exenciones del art. 5.3.2.2 en Escenario C | El tercero no hereda ninguna exención | VB-C-07 |
| 6 | Datos de dos vehículos distintos en un mismo documento | La resolución opera por vehículo; el FUR es por vehículo (art. 5.1.8) | VB-02 |
| 7 | Mandatario no inscrito en RUNT actuando como parte | art. 5.1.6 — el OT rechazará el poder | VB-03 |
| 8 | PJ sin inscripción RUES confirmada como parte | El OT no puede verificar la representación legal | VB-04 |
| 9 | Escenario B con tipo de opción de compra no declarado | El soporte varía según el tipo (art. 5.3.2.2 Parágrafo 1.º) | VB-B-03 |

---

## 11. Casos de rechazo — no generar documento

| Caso | Motivo | Código VB |
|------|--------|----------|
| Matrícula cancelada o inactiva en RUNT | No hay dominio registrable que transferir | VB-01 |
| Transferente no inscrito en RUNT | El OT rechazará el trámite | VB-03 |
| Escenario B y el destinatario NO es el locatario del contrato de leasing | Debe reclasificarse al Escenario C | VB-B-04 |
| Escenario B sin número de contrato de leasing | Requisito mínimo del art. 5.3.2.2 | VB-B-02 |
| Escenario C y el adquirente tiene los mismos datos del locatario histórico | Debe reclasificarse al Escenario B | VB-C-01 |
| Escenario A o C con gravamen activo sin levantamiento ni autorización del acreedor | El OT bloqueará el traspaso | VB-A-04, VB-C-05 |
| Escenario C con RTM vencida | No hay exención; el OT rechazará | VB-C-04 |
| Transferente = Adquirente (mismo documento) | Auto-transferencia inválida | VB-06 |
| PJ sin inscripción RUES confirmada | El OT no puede verificar la PJ | VB-04 |
| Mandatario no inscrito en RUNT | art. 5.1.6 — el OT rechazará | VB-03 |
| Escenario B con campo de precio capturado en el formulario | El instrumento del Escenario B no declara precio entre sus partes; incluirlo contradice el diseño del acto unilateral | VB-B-05 |
| Ningún escenario seleccionado o escenario ambiguo | El generador no puede determinar reglas aplicables | VB-05 |
| El usuario declara que aplica **al menos una** de las once condiciones especiales de los arts. 5.3.2.3 a 5.3.2.13 (§4.0) | La operación no se rige por el art. 5.3.2.1: exige soportes o exenciones propios de su artículo que este documento no captura ni acredita. El mensaje de rechazo debe citar el artículo concreto declarado | VB-07 |
| Servicio público de pasajeros o mixto en Escenario A o C sin declarar el régimen del art. 5.3.2.3 | El OT exige contrato de cesión de vinculación y aceptación de la empresa; esta plantilla no los prevé | VB-07 |
| Vehículo de carga con PBV superior a 10.500 kg | El OT valida la autorización de registro inicial del Ministerio de Transporte (art. 5.3.2.13); fuera del alcance de esta plantilla | VB-07 |

---

## 12. Datos que requieren verificación externa (RUNT / RUES / SIMIT)

| Dato | Sistema | Quién verifica | Momento |
|------|---------|---------------|---------|
| Inscripción del transferente | RUNT | OT (FLIT puede pre-validar si tiene interoperabilidad) | Antes del trámite |
| Inscripción del adquirente (A y C) | RUNT | OT | Antes del trámite |
| Características del vehículo (motor, chasis, VIN) | RUNT | OT — confronta vs licencia de tránsito | En el trámite |
| PJ — razón social, representante legal, NIT | RUES | OT (art. 5.1.5 — no exige certificado físico) | En el trámite |
| PJ derecho público — acto de delegación | Acto administrativo propio | Parte | Antes del trámite |
| Medidas judiciales que impidan el traspaso | RUNT | OT | En el trámite |
| Gravamen / prenda vigente y beneficiario | RUNT + Registro de Garantías Mobiliarias | OT | En el trámite |
| SOAT vigente | FASECOLDA / RUNT | OT | En el trámite |
| RTM vigente (solo A y C) | RUNT — CDA registrado | OT | En el trámite |
| SIMIT — infracciones del propietario | SIMIT | OT | En el trámite |
| Impuesto vehicular | Entidad territorial (interoperabilidad facultativa — art. 5.1.9) | OT | En el trámite |
| Retención en la fuente por la enajenación | Recibo de pago aportado por el interesado (art. 5.3.2.1 num. 5.º) | OT — el interesado adjunta copia de los recibos | En el trámite |
| Derechos del trámite ante el Ministerio de Transporte y tarifa RUNT | RUNT | OT — valida el pago en el sistema RUNT | En el trámite |
| Derechos del Organismo de Tránsito | Recaudo propio del OT | OT — verifica la realización del pago | En el trámite |

---

## 13. Checklist de aprobación del PDF generado

Antes de entregar el PDF al usuario, el sistema debe verificar:

### 13.1 Estructura y contenido

- [ ] El encabezado contiene placa, ciudad y fecha sin variables sin resolver.
- [ ] El escenario seleccionado (A, B o C) está declarado en el encabezado.
- [ ] Todas las variables `{{variable}}` están resueltas; no hay ninguna sin valor en el PDF final.
- [ ] No aparecen las etiquetas de renderización condicional `[...]` en el PDF.
- [ ] La tabla de datos del vehículo incluye los 13 campos de la §5.1.
- [ ] Las cláusulas corresponden al escenario seleccionado (no hay mezcla de escenarios).
- [ ] La declaración de régimen aplicable (§4.0) fue respondida y **ninguna** de las once condiciones de los arts. 5.3.2.3 a 5.3.2.13 quedó marcada. Si alguna quedó marcada, **no debe existir PDF**: el intento se registra en estado de error con el artículo citado (`VB-07`).
- [ ] El PDF no contiene referencia alguna a los arts. 5.3.2.3 a 5.3.2.13 ni a sus soportes especiales (cesión de vinculación, peritaje de aseguradora, resolución de la Superintendencia de Vigilancia, sentencia, acto de adjudicación, comiso, decomiso, declaración de importación modificatoria, declaratoria de abandono, autorización de registro inicial por PBV).

### 13.2 Firmas

> 🚧 Los ítems de checklist propios del modo `ESTAMPADA` están **diferidos**. Ver §9.4. En el alcance
> vigente `{{modo_firma}}` = `MANUSCRITA` siempre.


- [ ] **Escenario A:** bloque de firma con dos columnas — transferente y adquirente.
- [ ] **Escenario B:** el documento tiene **un solo** bloque de firma — la entidad financiera. El bloque del adquirente/locatario no fue instanciado (no existe como nodo del documento, ni vacío, ni oculto, ni suprimido en render).
- [ ] **Escenario B — verificación textual obligatoria:** extraído el texto del PDF, el bloque de firmas **no contiene** el literal `ADQUIRENTE` ni el literal `LOCATARIO`. La inspección visual del layout no sustituye esta comprobación.
- [ ] **Todos los escenarios:** `{{modo_firma}}` = `MANUSCRITA`. El PDF no contiene leyendas de firma electrónica, sellos de baúl de firmas ni sellos de validación de identidad en ningún bloque (§9.0).
- [ ] **Escenario C:** bloque de firma con dos columnas — transferente (entidad financiera) y adquirente (tercero), con nota de inaplicabilidad del art. 5.3.2.2.

### 13.3 Exclusiones del Escenario B

- [ ] No aparece campo de precio en el cuerpo ni en el encabezado.
- [ ] La cláusula cuarta lista las cinco exenciones del art. 5.3.2.2 con su numeración literal, y la nota aclara que el instrumento privado se estructura sin firma del locatario por decisión de diseño (no como exención adicional de la norma).
- [ ] El bloque de firmas indica que la transferencia es unilateral y que el locatario no firma el FUR (art. 5.3.2.2 excepción 5.ª) — **sin** afirmar que la norma prohíbe su firma en documentos privados.
- [ ] Los datos del locatario (`{{locatario_nombre}}`, etc.) aparecen solo en la cláusula segunda (declarativa), no en el bloque de firmas.

### 13.4 Exclusiones del Escenario C

- [ ] La cláusula segunda declara explícitamente que el régimen es el art. 5.3.2.1 sin exenciones del art. 5.3.2.2.
- [ ] No hay referencia a exenciones de leasing en ninguna cláusula.
- [ ] La cláusula quinta lista los requisitos plenos (RTM, QR/improntas, SIMIT).

### 13.5 Integridad general

- [ ] El disclaimer del §1 está presente en el documento o como texto adjunto al correo/descarga.
- [ ] No hay datos reales de PII de ejemplo visibles (el PDF de muestra usa datos sintéticos).
- [ ] La fuente normativa (Resolución 20233040017145/2023, artículo aplicable) está citada en al menos una cláusula.

---

## 14. Relación con otros documentos del módulo

| Documento | Relación con esta plantilla |
|-----------|---------------------------|
| **FUR (Formato Único de Solicitud de Trámite, Anexo 46)** | Documento oficial del RNA que acompaña este soporte. Este documento es el «título de dominio» exigido por el art. 5.3.2.1. Casilla numeral 3: **2** (Traspaso) en los tres escenarios. |
| **Contrato de Mandato** | Si el trámite lo adelanta un tercero (art. 5.1.6), el contrato de mandato es un soporte adicional independiente de este documento. Objeto del mandato: `TRASPASO` (sin calificativos adicionales). |
| **Certificado RUES** | No se exige en físico (art. 5.1.5). El OT consulta directamente. |
| **Contrato de Leasing** | Soporte exclusivo del Escenario B. Se adjunta al trámite junto con la declaración de terminación/ejercicio de opción (salvo opción automática). **Este documento no sustituye el contrato de leasing ni la declaración** — son documentos independientes exigidos por el art. 5.3.2.2; este instrumento es la constancia de la transferencia unilateral que los complementa. |

---

*Dictamen emitido por `expert-doc-engine` · Resolución 20233040017145/2023 · Solo lectura · No modifica código, mappers, migraciones ni ramas.*
*Fuentes: `resolucion-20233040017145.md`, `REGLAS-NUMERAL-3-TRES-CAPAS.md`, `REGLAS-OBJETO-TRES-CAPAS.md`, `fur-diligenciamiento.md`, ejemplares FUR `docs/ot/fur/` y `docs/ot/mandato/`.*
