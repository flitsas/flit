# Parametrización de los 21 tipos de trámite — hoja de revisión

> **Estado: propuesta técnica, no diseño validado.** Se construyó sobre el catálogo existente para
> que los 21 tipos pudieran operarse (DDL `81` y `82`). Nadie de negocio la ha revisado; ese es el
> trabajo de este documento.

**Relacionado:** [ADR-0050](../services/core-api/docs/adr/ADR-0050-tipo-de-tramite-fuente-unica-de-conformacion.md)
· [Handoff](handoff-adr-0050-tipo-tramite-fuente-unica.md)

| Tipos | Requisitos documentales | Requieren decisión |
|---:|---:|---:|
| 21 | 108 | **11** |

## Cómo revisarla

De cada tipo hay tres cosas que confirmar o corregir:

1. **El recorrido** — ¿son esos los pasos, en ese orden? ¿Sobra o falta alguno?
2. **Lo que exige** — quién interviene, si lleva valor de venta, si la prenda bloquea, si se valida identidad.
3. **Los documentos** — ¿está completo lo obligatorio? ¿Alguno opcional debería serlo, o al revés?

Los bloques **⚠ Requiere decisión** son preguntas que no se resuelven desde el código.


---

## Matrículas

Trámites que inscriben el vehículo o cierran su matrícula.

### Cancelación de matrícula

`CANCELACION_MATRICULA`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Certificado de tradición, Documento de identificación del propietario, Tarjeta de Propiedad

**Documentos opcionales:** Oficio judicial, Paz y Salvo de Impuestos, SOAT (vigente)

> ⚠ **Requiere decisión**
> - Está en la familia MATRÍCULAS pero entra por placa y captura «Propietario»: el recorrido es el de OTROS. ¿La familia es correcta, o debería moverse?

### Matrícula Leasing

`MATRICULA_LEASING`

**Exige:** Entra por VIN · titular / comprador · identidad validada

**Recorrido:** Consulta VIN

**Documentos obligatorios:** Resumen del trámite|Contrato de Leasing, Documento de identificación del comprador, Factura de Venta

**Documentos opcionales:** Certificado de Aduana / Declaración de Importación, Improntas

### Matrícula inicial

`MATRICULA_NUEVA`

**Exige:** Entra por VIN · titular / comprador · identidad validada

**Recorrido:** Consulta VIN

**Documentos obligatorios:** Resumen del trámite|Certificado de Aduana / Declaración de Importación, Factura de Venta

**Documentos opcionales:** Acta de remate, Certificado CEPD, Declaración de Importación, Improntas, Mandato, Oficio judicial, Otro documento, SOAT (vigente), Trámite Virtual

> ⚠ **Requiere decisión**
> - Usa el documento genérico «Otro documento»: el catálogo no tiene código propio para el que este trámite exige.

### Rematrícula

`REMATRICULA`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Certificado de tradición, Documento de identificación del propietario, Tarjeta de Propiedad

**Documentos opcionales:** Paz y Salvo de Impuestos, SOAT (vigente)

> ⚠ **Requiere decisión**
> - Mismo caso que la cancelación: familia MATRÍCULAS con recorrido de OTROS. ¿Se queda o se mueve?


---

## Traspaso

Trámites que cambian el propietario. Son los únicos con dos partes.

### Traspaso

`TRASPASO_STANDARD`

**Exige:** Entra por placa · parte vendedora · titular / comprador · valor de venta · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identidad (cédula / carta-selfie), Formato de Compraventa, Paz y Salvo de Impuestos, Revisión Técnico Mecánica (RTM), SOAT (vigente)

**Documentos opcionales:** Certificado de tradición, Improntas, Inscripción / Registro de Prenda, Mandato, Trámite Virtual

### Traspaso con Transferencia de Dominio

`TRASPASO_TRANSFERENCIA_DE_DOMINIO`

**Exige:** Entra por placa · parte vendedora · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Certificado de transferencia de dominio, Documento de identificación del comprador, Documento de identificación del vendedor, Tarjeta de Propiedad

**Documentos opcionales:** _ninguno_

> ⚠ **Requiere decisión**
> - Es el único traspaso que NO exige valor de venta, mientras el estándar sí. ¿Deliberado?

### Traspaso Unilateral

`TRASPASO_UNILATERAL`

**Exige:** Entra por placa · titular / comprador · valor de venta · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del comprador, Formato de Compraventa, Tarjeta de Propiedad

**Documentos opcionales:** SOAT (vigente)

> ⚠ **Requiere decisión**
> - No declara parte vendedora pero sí exige valor de venta. ¿Un traspaso sin vendedor lleva precio, o el valor sobra aquí?


---

## Otros trámites

Cambios sobre un vehículo ya matriculado. Un solo actor: su propio dueño.

### Blindaje

`BLINDAJE`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Otro documento, Tarjeta de Propiedad

**Documentos opcionales:** Paz y Salvo de Impuestos, SOAT (vigente)

> ⚠ **Requiere decisión**
> - Usa el documento genérico «Otro documento»: el catálogo no tiene código propio para el que este trámite exige.

### Cambio acreedor

`CAMBIO_ACREEDOR`

**Exige:** Entra por placa · titular / comprador · puerta de prenda · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Inscripción / Registro de Prenda, Tarjeta de Propiedad

**Documentos opcionales:** Limitación de la propiedad y garantía a favor de, Paz y Salvo de Impuestos, SOAT (vigente)

### Cambio de carrocería

`CAMBIO_CARROCERIA`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Factura de Carrocería, Improntas, Tarjeta de Propiedad

**Documentos opcionales:** Paz y Salvo de Impuestos, SOAT (vigente)

### Cambio de color

`CAMBIO_COLOR`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Tarjeta de Propiedad

**Documentos opcionales:** Otro documento, Paz y Salvo de Impuestos, SOAT (vigente)

> ⚠ **Requiere decisión**
> - Usa el documento genérico «Otro documento»: el catálogo no tiene código propio para el que este trámite exige.

### Cambio de locatario

`CAMBIO_LOCATARIO`

**Exige:** Entra por placa

**Recorrido:** Consulta

**Documentos obligatorios:** _ninguno_

**Documentos opcionales:** _ninguno_

> ⚠ **Requiere decisión**
> - No tiene ningún documento parametrizado. Su recorrido viene de un seed anterior, no de esta propuesta.

### Conversiones de combustible

`CONVERSION_COMBUSTIBLE`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Certificado CEPD, Documento de identificación del propietario, Tarjeta de Propiedad

**Documentos opcionales:** Paz y Salvo de Impuestos, SOAT (vigente)

### Duplicado de placa

`DUPLICADO_PLACA`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Otro documento, Tarjeta de Propiedad

**Documentos opcionales:** Paz y Salvo de Impuestos, SOAT (vigente)

> ⚠ **Requiere decisión**
> - Usa el documento genérico «Otro documento»: el catálogo no tiene código propio para el que este trámite exige.

### Duplicado de tarjeta

`DUPLICADO_TARJETA`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Otro documento, Tarjeta de Propiedad

**Documentos opcionales:** Paz y Salvo de Impuestos, SOAT (vigente)

> ⚠ **Requiere decisión**
> - Exige validación biométrica del titular. ¿Un duplicado de tarjeta requiere identidad validada, o basta el documento?
> - Usa el documento genérico «Otro documento»: el catálogo no tiene código propio para el que este trámite exige.

### Levantar prenda

`LEVANTAMIENTO_PRENDA`

**Exige:** Entra por placa · titular / comprador · puerta de prenda · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Paz y Salvo de Prenda, Tarjeta de Propiedad

**Documentos opcionales:** Paz y Salvo de Impuestos, SOAT (vigente)

### Levantar e inscribir prenda

`LEVANTAR_INSCRIBIR_PRENDA`

**Exige:** Entra por placa · titular / comprador · puerta de prenda · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Inscripción / Registro de Prenda, Paz y Salvo de Prenda, Tarjeta de Propiedad

**Documentos opcionales:** Paz y Salvo de Impuestos, SOAT (vigente)

### Inscribir prenda

`PRENDA_INSCRIPCION`

**Exige:** Entra por placa · puerta de prenda

**Recorrido:** Consulta

**Documentos obligatorios:** _ninguno_

**Documentos opcionales:** _ninguno_

> ⚠ **Requiere decisión**
> - No tiene ningún documento parametrizado. Su recorrido viene de un seed anterior, no de esta propuesta.

### Radicado de cuenta

`RADICADO_CUENTA`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Paz y Salvo de Impuestos, Tarjeta de Propiedad

**Documentos opcionales:** Certificado de tradición, SOAT (vigente)

### Regrabar motor, chasis

`REGRABAR_MOTOR_CHASIS`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Improntas, Tarjeta de Propiedad

**Documentos opcionales:** Paz y Salvo de Impuestos, SOAT (vigente)

### Traslado de cuenta

`TRASLADO_CUENTA`

**Exige:** Entra por placa · titular / comprador · identidad validada

**Recorrido:** Consulta del vehículo

**Documentos obligatorios:** Resumen del trámite|Documento de identificación del propietario, Paz y Salvo de Impuestos, Tarjeta de Propiedad

**Documentos opcionales:** Certificado de tradición, SOAT (vigente)


---

Datos extraídos de `tramites.procedure_types`, `procedure_steps` y
`procedure_document_requirements`. Los recorridos y la matriz vienen de los DDL `81` y `82`;
los de `CAMBIO_LOCATARIO` y `PRENDA_INSCRIPCION`, de un seed anterior.

**Al corregir:** el DDL `82` ya está aplicado. Cada ajuste es un DDL nuevo con su migración, o
—una vez exista— el configurador de Plataforma → Tipos de trámites.
