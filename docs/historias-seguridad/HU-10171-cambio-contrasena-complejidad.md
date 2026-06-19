# HU #10171 — Cambio voluntario de contraseña + validación de complejidad

**Feature:** #10113 · **Tipo:** Backend · **Rama:** feature/AB-10171-cambio-contrasena

## Qué se implementó
- **`PUT /api/v1/auth/change-password`** (autenticado): el propio usuario cambia su contraseña aportando la actual. Verifica la contraseña actual, valida la **complejidad** de la nueva y persiste el nuevo hash (Argon2), limpiando `must_change_password` y actualizando `PasswordChangedAt`.
- **Política de complejidad** centralizada (`PasswordPolicy`): mínimo 8 caracteres, con al menos **una mayúscula, una minúscula y un dígito** (RNF02). Se aplica también al `reset-password` (HU #10169) para consistencia.

## Casos de uso
1. Usuario autenticado quiere rotar su contraseña desde el perfil → la cambia con la actual + una nueva robusta.
2. Usuario intenta una contraseña débil → se rechaza con política.
3. Usuario aporta la contraseña actual equivocada → se rechaza sin cambiar nada.

## Cómo probar
```bash
# 1) Obtener token de sesión
TOKEN=$(curl -s -X POST http://localhost:5005/api/v1/auth/login \
  -H 'Content-Type: application/json' -d '{"email":"demo@flit.local","password":"DemoPass1!"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")

# AC2 — nueva débil → 400 PASSWORD_POLICY_VIOLATION
curl -X PUT http://localhost:5005/api/v1/auth/change-password -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"currentPassword":"DemoPass1!","newPassword":"abc123"}'

# Contraseña actual incorrecta → 400 INVALID_CURRENT_PASSWORD
curl -X PUT .../change-password -H "Authorization: Bearer $TOKEN" \
  -d '{"currentPassword":"WRONG","newPassword":"NewDemo123"}'

# Sin token → 401

# AC1 — cambio válido → 200; luego login con la nueva funciona y con la vieja falla
curl -X PUT .../change-password -H "Authorization: Bearer $TOKEN" \
  -d '{"currentPassword":"DemoPass1!","newPassword":"NewDemo123"}'
```

## Respuestas esperadas
| Escenario | HTTP | Código |
|---|---|---|
| nueva no cumple complejidad | 400 | `PASSWORD_POLICY_VIOLATION` |
| contraseña actual incorrecta | 400 | `INVALID_CURRENT_PASSWORD` |
| sin autenticación | 401 | — |
| cambio exitoso | 200 | mensaje OK |

## Pruebas ejecutadas
- Unitarias: 15 nuevas (`ChangePasswordHandlerTests` + `PasswordPolicyTests`). Suite total 32 en el proyecto de Application.
- E2E en DEV (API local + PostgreSQL docker): 400/400/401/200 verificados, login con nueva (200) y vieja (401), restauración OK.

## Notas
- "Invalida sesiones previas": se actualiza `PasswordChangedAt` como marca. La invalidación efectiva de JWT (stateless) requiere un mecanismo de versión de token/`security stamp` validado por middleware — pendiente como mejora transversal (no había infraestructura de revocación).
