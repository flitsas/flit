# Inventario de marca — HU #12415

Generado: 2026-09-15T23:06:45.540Z · regenerar con `pnpm brand:inventory` (frontend/scripts/brand-color-inventory.mjs).

Inventario **cerrado**: script de solo lectura, no modifica ningún componente ni estilo de producto (AC5). El saneamiento es HU #12420.

## Resumen repositorio-completo (AC2)

| Ámbito | Archivos con hex de marca | Apariciones de marca |
|---|---|---|
| Dentro del alcance | 1 | 2 |
| Fuera del alcance | 379 | 3006 |
| **Total** | — | **3008** |

Metodología: directorios `app, components, lib`, extensiones `.ts, .tsx, .js, .jsx, .css, .svg`, excluyendo `node_modules, .next, __tests__`. Conteo repositorio-completo con metodología propia (ver comentario junto a SCAN_DIRS); no reproduce literalmente la cifra de referencia citada en el AC2 de la HU.

## Superficie: Pantalla de acceso (`acceso`)

Marca: 0 · Neutro: 0 · Estado: 0

| Archivo | Existe |
|---|---|
| `components/atom/Login.tsx` | sí |
| `app/login/page.tsx` | sí |

## Superficie: Activación de cuenta (`activacion`)

Marca: 0 · Neutro: 0 · Estado: 0

| Archivo | Existe |
|---|---|
| `app/invite/activate/page.tsx` | sí |
| `components/auth/ActivateAccountForm.tsx` | sí |

## Superficie: Recuperación de contraseña (`recuperacion`)

Marca: 0 · Neutro: 1 · Estado: 0

| Archivo | Existe |
|---|---|
| `app/auth/forgot-password/page.tsx` | sí |
| `app/auth/reset-password/page.tsx` | sí |
| `components/auth/ForgotPasswordForm.tsx` | sí |
| `components/auth/ResetPasswordForm.tsx` | sí |
| `components/auth/AuthCard.tsx` | sí |

| Archivo | Línea | Hex | Clasificación |
|---|---|---|---|
| `components/auth/AuthCard.tsx` | 29 | `#0B0F14` | neutro |

## Superficie: Cabecera y menú (`cabecera-menu`)

Marca: 0 · Neutro: 9 · Estado: 1

| Archivo | Existe |
|---|---|
| `components/atom/Shell.tsx` | sí |
| `components/atom/dock/DockDesktop.tsx` | sí |

| Archivo | Línea | Hex | Clasificación |
|---|---|---|---|
| `components/atom/Shell.tsx` | 588 | `#05060A` | neutro |
| `components/atom/Shell.tsx` | 589 | `#FFFFFF` | neutro |
| `components/atom/Shell.tsx` | 651 | `#0B0F14` | neutro |
| `components/atom/Shell.tsx` | 651 | `#FFFFFF` | neutro |
| `components/atom/Shell.tsx` | 654 | `#FFFFFF` | neutro |
| `components/atom/Shell.tsx` | 783 | `#ffffff` | neutro |
| `components/atom/dock/DockDesktop.tsx` | 135 | `#ffffff` | neutro |
| `components/atom/dock/DockDesktop.tsx` | 156 | `#ffffff` | neutro |
| `components/atom/dock/DockDesktop.tsx` | 180 | `#ffffff` | neutro |
| `components/atom/dock/DockDesktop.tsx` | 204 | `#4F74C9` | estado |

## Superficie: Icono y título de la pestaña (`pestana`)

Marca: 2 · Neutro: 2 · Estado: 0

| Archivo | Existe |
|---|---|
| `app/layout.tsx` | sí |
| `app/icon.svg` | sí |

| Archivo | Línea | Hex | Clasificación |
|---|---|---|---|
| `app/icon.svg` | 1 | `#557eff` | marca |
| `app/icon.svg` | 1 | `#00dbd5` | marca |
| `app/icon.svg` | 1 | `#fafafa` | neutro |
| `app/icon.svg` | 1 | `#fafafa` | neutro |

## Excluidas del alcance a propósito

| Archivo | Motivo |
|---|---|
| `app/profile/change-password/page.tsx` | Cambio de contraseña autenticado (ajustes de perfil), no 'recuperación de contraseña' (flujo no autenticado de AC1). |
| `components/auth/ChangePasswordForm.tsx` | Formulario exclusivo de app/profile/change-password/page.tsx — mismo motivo. |

