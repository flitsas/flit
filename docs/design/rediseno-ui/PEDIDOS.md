# Pedidos a la sesión coordinadora

Una zona que necesita un token, un componente o un cambio en `packages/` lo anota aquí y no lo crea por su cuenta.

| Fecha | Zona | Qué hace falta | Dónde se vio | Estado |
|---|---|---|---|---|
| 2026-11-09 | S3 · HU14485 | La pantalla de ingreso es `frontend/components/atom/Login.tsx` (raíz de atom, de S0): el AC1 de la HU pide el logo y la ilustración `heroe-ingreso.webp` del kit, y esa ilustración aún no está en `public/`. | `/login` | Abierto |
| 2026-11-09 | S3 · HU14485 | `BrandLogo variant="dark"` en `AuthCard` (acceso/recuperación) es fijo: en tema oscuro queda el logo de tinta sobre la tarjeta oscura. ¿`BrandLogo` puede elegir variante por tema? Igual el `<img src="/assets/logo-flit-dark.svg">` de `CaptureCard` (verificación). | `/auth/forgot-password`, `/verificacion/[token]` | Abierto |
| 2026-11-09 | S3 · HU14485 | Velo de modal (scrim): no hay token oscuro constante en ambos temas; uso `bg-flit-primary/40 dark:bg-background/70`. Si quieres un `--overlay`, lo cambio. | `SessionExpiredListener`, `AuthCard`, `CopiarEnlaceManual`, `ManualShell`, Dr. Flit | Abierto |
