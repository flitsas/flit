# ADR-0074: Diagnóstico arranca con una puerta mínima de plataforma y un guard de suscripción propio hasta que exista el del SDK

**Fecha**: 2026-10-21
**Status**: Propuesto
**Deciders**: Líder Técnico FLIT (aceptación exclusiva humana, regla FLIT 15). Redactado durante la implementación de la HU #13728.
**Tags**: arquitectura, plataforma, seguridad, suscripción, despliegue
**Épica / Feature / HU**: #12881 / #13715 / #13726 (guard propio, F00.1-H4) y #13728 (F00.1-H5, redacción) · **Plan**: `docs/diagnosticos/plan-implementacion-diagnostico-vehicular-v2.md` D-P, §7.1 (P1–P8), §8, RD-3 · **Relacionados**: ADR-0070, ADR-0072, ADR-0073

## Contexto

Para operar con clientes, Diagnóstico necesita una «puerta» de plataforma completa: cliente de servicio y bus (P1),
guard de suscripción del SDK con evento de invalidación (P2), validación de firma y audiencia en el gateway YARP (P3),
OIDC encendido en DEV y QA (P4), la tarjeta del hub (P5), además del cambio de compañía activa, las pantallas de
administración y la plantilla de producto (P6–P8). Esperar todo eso antes del esqueleto del producto
retrasaría D0–D2 varias semanas sin reducir el riesgo del producto. Exponer el producto sin parte de la puerta, en
cambio, abre una brecha: sin P3, el gateway reenvía tokens sin verificar la firma.

## Decisión (D-P = b, en paralelo)

1. **P1 y P4 corren junto con D0.** Sin P1 (cliente `svc-diagnostico`, exchange, usuario de base dueño del schema) el
   servicio no arranca fuera de DEV. Sin P4 no hay login en el DEV compartido.
2. **Guard propio por petición mientras P2 no exista** (HU #13726, `Plataforma/GuardSuscripcion.cs`). Toda petición bajo
   `/api/v1/diagnostico` pregunta a Identidad por gRPC (`ObtenerProductosHabilitados`, `IProductosHabilitados`, caché de
   1 minuto) si la compañía del token tiene Diagnóstico activo. Es *fail-closed*: si no lo tiene, si no hay compañía o si
   no se puede saber (Identidad caída, sin Identidad configurada), responde 403 `PRODUCTO_NO_HABILITADO`. Solo un entorno
   local sin Identidad lo enciende, con `Diagnostico:Identidad:SimularSuscripcionActiva=true` explícito y nunca por el
   nombre del ambiente, porque DEV, QA y PDN corren como Development. Cuando llegue P2 (`RequireProductoActivo` del SDK
   con el evento `platform.tenant_product.changed`), se reemplaza `UseGuardSuscripcion` por el del SDK y se borra el guard
   propio. Las pruebas de `GuardSuscripcionTests` se conservan contra el nuevo guard.
3. **P3 es condición dura.** Sin validación de firma y audiencia en el gateway, el producto **no** se expone en QA ni se
   libera. Hasta P3 la API valida el token ella misma (JWKS del SDK, audiencia `diagnostico`), pero esa validación no
   reemplaza la del borde.
4. **No bloquean D0–D2:** P2 (lo cubre el guard propio), P5 (solo liberación), P6 (una sesión = una compañía, D-03), P7 (los
   roles se asignan por la vía actual) y P8 (se clona Comparendos).
5. **La matriz de dependencias es la de §7.1 del plan**, que es la fuente para planear las olas. Este ADR solo fija la regla.

## Alternativas consideradas

- **a — Esperar la puerta completa.** Es la opción más segura, pero bloquea D0–D2 detrás de trabajo de otros equipos
  (SDK, gateway, hub) y el producto no avanza. Se descarta.
- **c — Sin guard de suscripción hasta P2.** Cualquier compañía con un token de la audiencia entraría aunque no pague.
  Incumple el CA 5 de MOD00. Se descarta.
- **d — Validar la firma solo en la API, sin P3.** Deja el gateway como paso abierto y cada producto repite la defensa.
  Se acepta solo como estado transitorio en DEV, no como condición para QA.

## Tradeoff aceptado

- **Doble trabajo acotado:** el guard propio se descarta cuando llegue P2. Es un middleware pequeño con pruebas, portado de
  Comparendos (HU #13521).
- **Una suspensión tarda hasta 1 minuto** en notarse (caché del guard propio). P2 la acorta con el evento de invalidación.
- **Cada petición de negocio consulta a Identidad** (con caché). Si Identidad cae, el producto deja de responder con 403
  en vez de abrirse. Se prefiere así.
- DEV puede correr sin P3. Mientras tanto, DEV no se trata como un ambiente con datos reales de clientes.

## Consecuencias

- Al llegar P2 hay que abrir una HU para reemplazar el guard. La prueba `GuardSuscripcionTests` sirve de contrato.
- El checklist de salida a QA de Diagnóstico incluye P3 verificado: un token sin firma recibe 401 en el gateway.
- El backoffice FLIT (ADR-0073) también pasa por este guard: la compañía FLIT necesita Diagnóstico encendido.
