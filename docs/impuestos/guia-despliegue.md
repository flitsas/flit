# Impuestos: guía de despliegue por ambiente

HU #14694 (Epic #14692, ADR-0093). Pasos de operación que **no** hace ninguna migración y que hay que correr en cada
ambiente (DEV, QA, PDN) al desplegar Impuestos. Los gates del Líder Técnico están en
[plan-implementacion.md §11](plan-implementacion.md); aquí solo va lo que toca ejecutar.

## Orden

1. **G-8 (broker) antes de desplegar core-api y core-identity.** Es requisito del despliegue de **core-identity**: en
   cuanto core-identity (y core-api) corren con Impuestos, encender o reintentar Impuestos para una compañía deja un
   trabajo en `flit.impuestos`; sin el permiso, la publicación falla con `ACCESS_REFUSED` y el trabajo queda pendiente en
   la outbox del host. Pasos y verificación: runbook MEN-10 de
   [deploy/rabbitmq/README.md](../../deploy/rabbitmq/README.md#runbook-men-10-sumar-flitimpuestos-a-tramites-y-plataforma-sin-cambiar-la-clave),
   más el usuario `impuestos` (`deploy/rabbitmq/usuario-de-servicio.sh impuestos "$CLAVE" notificaciones`).
2. Desplegar core-api (aplica la migración `20261125041316_ImpuestosProductoYAprovisionamiento`, DDL 205) y core-identity.
3. **Activar el producto en el catálogo** (§1), cuando el ambiente vaya a usar Impuestos.
4. Arrancar (o reiniciar) core-impuestos. Registra su manifiesto de permisos al arrancar, y la plataforma lo rechaza con
   `400 PRODUCT_INACTIVE` mientras el producto esté inactivo; ante un 4xx el servicio **no reintenta** hasta el siguiente
   arranque. Por eso va después del paso 3.
5. Encender Impuestos para cada compañía desde el admin de compañías (SuperAdmin, pestaña «Productos»).

## 1. Activar el producto `impuestos` (BLOQ-1 a)

El DDL 205 siembra `impuestos` en `platform.products` con `status = 'inactive'`. Mientras siga así:

- `PUT /api/v1/platform/admin/tenants/{tenantId}/products/impuestos` con `{"enabled": true}` responde
  **409 `PRODUCT_INACTIVE`** («El producto 'impuestos' está inactivo y no se puede encender.»). Apagarlo sí se permite.
- `PUT /api/v1/platform/products/impuestos/manifest` responde **400 `PRODUCT_INACTIVE`**.
- `GET /api/v1/platform/me/apps` no lo lista (tampoco al SuperAdmin).

El catálogo no tiene endpoint para cambiar el estado de un producto: se activa por SQL, con el usuario dueño del esquema
`platform` de core-api, en la base del ambiente.

```sql
-- Activar Impuestos en este ambiente (idempotente).
UPDATE platform.products
   SET status = 'active', updated_at = now()
 WHERE code = 'impuestos' AND status <> 'active';

-- Comprobar: una fila con status = active.
SELECT code, status, updated_at FROM platform.products WHERE code = 'impuestos';
```

Para retirarlo otra vez de un ambiente: primero apagarlo en cada compañía (el PUT con `enabled: false` sigue permitido) y
luego `UPDATE platform.products SET status = 'inactive', updated_at = now() WHERE code = 'impuestos';`.

`platform.products` no tiene trigger de auditoría (DDL 119 solo audita `tenant_products`): anotar quién lo activó y
cuándo en la bitácora del despliegue del ambiente.
