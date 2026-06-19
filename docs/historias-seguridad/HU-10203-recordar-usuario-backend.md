# HU #10203 — Recordar usuario registrado (backend)

**Feature:** #10113 · **Tipo:** Backend · **Rama:** feature/AB-10203-recordar-usuario-backend

## Qué se implementó
- **`POST /api/v1/auth/remember-username`** — recibe un **número de documento**; si corresponde a una cuenta activa, envía al correo registrado un recordatorio con el **usuario (email)**. Responde **202 genérico** sin revelar si el documento existe (anti-enumeración / Habeas Data Ley 1581).
- **Columna `document_number`** (nullable, varchar 40, índice `ix_users_document_number`) añadida a `identity.users` como identificador alterno. Migración `HU10203_UserDocumentNumber`.

## Casos de uso
1. Usuario olvidó con qué email se registró → ingresa su documento → recibe el usuario por correo.
2. Documento no asociado a ninguna cuenta → respuesta genérica, sin filtrar existencia.

## Cómo probar
```bash
# AC1 — documento asociado a cuenta activa → 202 + correo con el usuario
curl -X POST http://localhost:5005/api/v1/auth/remember-username \
  -H 'Content-Type: application/json' -d '{"documentNumber":"1020304050"}'
# → 202 {"message":"Si el documento corresponde a una cuenta, enviaremos el usuario al correo registrado."}
# En dev, el correo (con el email/usuario) aparece en el log (ConsoleEmailSender).

# AC2 — documento inexistente → 202 idéntico, sin enviar correo
curl -X POST .../remember-username -d '{"documentNumber":"99999999"}'
```
> Requiere poblar `identity.users.document_number`. Ejemplo de backfill:
> `UPDATE identity.users SET document_number='1020304050' WHERE email='demo@flit.local';`

## Respuestas esperadas
| Escenario | HTTP | Cuerpo |
|---|---|---|
| documento asociado | 202 | mensaje genérico (+ correo enviado) |
| documento inexistente / vacío | 202 | mensaje genérico idéntico (sin correo) |

## Pruebas ejecutadas
- Unitarias: 3 nuevas (`RememberUsernameHandlerTests`). Suite total 35 en Application.
- E2E en DEV (API local + PostgreSQL docker): documento del demo → 202 + email con `demo@flit.local`; documento inexistente → 202 idéntico (0 correos).

## Notas
- La migración añade la columna como **aditiva nullable**; los usuarios existentes quedan sin documento hasta un backfill. La carga masiva de documentos es un proceso de datos fuera del alcance de esta HU.
