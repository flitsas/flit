# Pedidos a la sesión coordinadora

Una zona que necesita un token, un componente o un cambio en `packages/` lo anota aquí y no lo crea por su cuenta.

| Fecha | Zona | Qué hace falta | Dónde se vio | Estado |
|---|---|---|---|---|
| 2026-11-09 | S1 Trámites | `--color-flit-primary` no tiene valor en `.dark`: queda `#162744` sobre `--background` oscuro (`#0a1326`, ≈1,1:1). Solo en `components/operacion` hay 135 `text-flit-primary`. Propuesta: en `.dark`, `--brand-primary` o un alias que resuelva a `--foreground` (`#e6f0ff`) sin romper la marca blanca. S1 sigue usando `text-flit-primary` en títulos. | `components/operacion/**` | Abierto |
| 2026-11-09 | S1 Trámites | Tokens de estado de trámite: `ESTADO_CHIP_STYLES` (`lib/tramites/estados.ts`) tiene 10 tonos propios (bg/color/border/accent) atados a los SVG de `public/assets/estados`, sin variante oscura (p. ej. `#4465CC` como texto sobre tarjeta oscura). Lo consumen también LOG QX, ICT, admin OT y Dr. FLIT, y dos pruebas de contrato exigen `accent` en hex (`estados-catalogo.test.ts`, `hu12726-badge-timeline-alerta-extra.test.ts`). Propuesta: `--estado-{borrador,preparado,preasignacion,asignado,entregado,aprobado,rechazado,anulado,revocado,subsanacion}-{bg,fg,border}` en claro y oscuro; `accent` se queda en hex porque es el color del icono. Mientras no exista, S1 NO toca `estados.ts`. ¿O se prefiere colapsar a los `--badge-*` del kit (y repintar los 10 SVG)? Eso es decisión de diseño. | `lib/tramites/estados.ts` | Abierto |
