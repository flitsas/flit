# Pruebas en vivo: Consultas y Notificaciones como servicios

Epic #13316. Pruebas hechas en local los días 2026-10-07 y 2026-10-08, con todo encendido y proveedores reales, antes del
despliegue. Sirve para saber qué se comprobó, qué pasa cuando un servicio se cae y qué quedó pendiente.

**Montaje:**
- Base `flit2dev_13316`, una copia de DEV.
- Servicios encendidos: core-api, core-consultas, core-notificaciones, core-ict, RabbitMQ, hub y Trámites.
- Proveedores: Kyverum RUNT y Kyverum Verify reales; Fasecolda en mock; correo por SMTP real (`info@flitsas.com`).
- El aviso de Kyverum Verify llegó a la máquina local por un túnel de cloudflared.

Los datos de prueba fueron siempre del mismo usuario (CC 1000098455) y de la Secretaría de Movilidad de Bogotá.

Estado: **OK** funcionó · **OK con arreglo** falló, se corrigió en la rama y se volvió a probar · **Conocido** ya pasaba
antes del corte y tiene salida manual.

## 1. Camino feliz

| Caso | Resultado |
|---|---|
| Matrícula inicial completa (FT1-0000049): consultas de vehículo, persona, RNMC, impronta y avalúo | OK. Todas pasan por core-consultas; core-api no llama a ningún proveedor |
| Cambios de estado publicados en `flit.tramites` | OK |
| Correos de asignación de placa, rechazo y aprobado | OK. Salen por core-notificaciones con el remitente del canal |
| Rechazo del proveedor de correo (buzón remitente lleno) | OK. Va a mensajes muertos sin reintentos automáticos; se reintenta desde la pantalla al corregir la causa |
| Kyverum Verify real: creación, aviso, bus y aplicación en el trámite | OK. Aprobada en 1 s desde el aviso; el certificado se descarga por Consultas |
| ICT: registro, consultas directas a core-consultas, borrador en core-api | OK. El borrador trae marca, línea, modelo, SOAT y gravámenes del snapshot, sin volver a consultar el RUNT |
| ICT: edición del precio ya materializado (ICT → core-api) | OK |

## 2. Caídas de servicios

| Caso | Qué se espera | Resultado |
|---|---|---|
| core-consultas caído al consultar en el asistente | Mensaje «Consulta no disponible», sin error 500 | OK. Al volver, hasta ~15 s más de «no disponible» por la protección del SDK; es lo esperado |
| core-notificaciones caído al generar un correo | El correo espera en `notificaciones.email.send` y sale solo al volver | OK |
| RabbitMQ caído al rechazar un trámite | Los mensajes quedan en el outbox de core-api y salen en orden al volver | OK. El ritmo de reintentos se corrigió (ver 4.1) |
| core-consultas caído al crear una validación de identidad | La validación pasa a «Error de envío» | Conocido. Tras 3 reintentos en ~30 s pide «Reenviar» a mano; al reenviar, sale |
| core-consultas caído ~5 min mientras la persona se valida | Al volver queda aprobada sola | OK. Kyverum reintentó el aviso |
| Aviso de Kyverum perdido y core-consultas caído ~13 min | La conciliación de core-api recupera el resultado | OK con arreglo (ver 4.3). Aprobada 3 min después de volver |
| core-api caído cuando llega el aviso de Kyverum | El aviso espera en `tramites.avisos-kyverum` y se aplica al volver | OK. Se aplica una sola vez gracias a `tramites.inbox` |

## 3. Seguridad

| Caso | Resultado |
|---|---|
| gRPC sin token o con token falso (Consultas y Notificaciones) | OK, `Unauthenticated` |
| Secreto de cliente equivocado al pedir token | OK, 401 |
| Token con un scope que no alcanza (p. ej. envío contra mensajes muertos) | OK, `PermissionDenied` |
| Token emitido para un servicio usado en otro | OK, `Unauthenticated` (cada token lleva su `aud`) |
| Token válido sin la empresa (`x-flit-tenant-id`) | OK, `InvalidArgument` |
| Admin de compañía en pantallas y API de SuperAdmin (mensajes muertos, canales, consumo) | OK, «Acceso restringido» y 403 |
| Cliente que pide un scope que no tiene | El token se niega, pero con `invalid_request` en vez de `invalid_scope` (anotado en #13333) |

## 4. Lo que las pruebas obligaron a corregir

1. **El outbox reintentaba una vez por segundo con RabbitMQ caído** (~56 intentos por minuto). Ahora espera cada vez
   más, de 1 s hasta 1 min (`Platform:Messaging:MaxRetryDelay`). Medido: 7 intentos en 2 min. Commit `723ea069a`.
2. **core-api marcaba `enviado` un correo que solo había dejado en cola.** Ahora queda `encolado`; si llegó, lo dice el
   registro de entregas de Notificaciones. **Mensajes muertos** muestra la causa («Rechazado por el proveedor» o «Agotó
   los reintentos») y el código del servidor (p. ej. `SMTP 554 5.2.2 · buzón lleno`). Commit `b364c0814`.
3. **La conciliación de identidad gastaba sus 3 sondeos aunque Consultas no respondiera.** Si además el aviso de Kyverum
   se perdía, la validación quedaba «en proceso» para siempre. Ahora un fallo transitorio no gasta sondeos. Commit `cc7e50cd1`.

## 5. Para el despliegue

- **Aviso de Kyverum Verify:** agregar `CONSULTAS_KYVERUM_WEBHOOK_CALLBACK_URL` con la ruta nueva
  (`/api/v1/consultas/avisos/kyverum-verify`). No borrar la variable vieja en el primer despliegue. Detalle en
  [handoff-vps-suite.md](handoff-vps-suite.md) §4.9, paso 5.
- **Si core-consultas se cae más de ~30 s**, las validaciones de identidad que se creen en ese momento quedan en «Error de
  envío» y hay que reenviarlas desde Identidad.
- **Mensajes muertos** (Plataforma → Mensajes muertos) es la pantalla para revisar correos que no salieron.

## 6. Hallazgos que no son de esta Epic

- **«No se encontró en RUNT» cuando los proveedores fallan:** debería decir «no disponible».
- **RNMC se consulta dos veces a la vez desde el resumen:** da 500 por llave duplicada.
- **ICT acepta el NIT sin dígito de verificación al registrar**, pero el procesamiento exige el `tax_id` exacto. Si core-api
  rechaza el borrador, ICT queda «con novedades» sin comentario.
- **Una compañía sin configuración de trámites tiene Matrículas bloqueada por defecto**, incluidos los clientes ICT.
- **El webhook OT busca suscripciones por la compañía del trámite y no por el OT**, así que una suscripción registrada a
  nombre de un OT nunca se dispara.

## 7. No probado en vivo

- **El aviso de estado de core-api a core-ict:** solo sale con transiciones de negocio y no se forzó. Su código no cambió
  en esta Epic.
- **El webhook OT con un receptor público:** quedó fuera por decisión de producto.
