# Plan técnico — Ajustes del módulo Usuarios

> Generado: 2026-08-14 · rama base `develop` · origen: 4 novedades reportadas por usuario en DEV
>
> Diagnóstico por lectura estática del código (4 barridos `explore-agent` + verificación manual de
> los puntos decisivos). **Ninguna novedad se reprodujo en el DEV desplegado.** Las anclas
> `archivo:línea` derivan con cualquier commit: reverificar antes de citarlas.

---

## 0. Resumen ejecutivo

| # | Novedad reportada | Veredicto del diagnóstico | Caso | SP |
|---|---|---|---|---|
| N1 | Mensaje "Ya existe una invitación pendiente" al reutilizar un correo | El mensaje **es correcto**; el catálogo de mensajes es confuso y está duplicado en 2 endpoints | A — ajuste | 1 |
| N2 | La columna Perfil/Rol muestra "GESTOR" para todos | **No es bug de datos.** "Gestor" es el perfil correcto de todo usuario de compañía; el diseño de la celda oculta el rol | A — ajuste | 2 |
| N3 | Al cancelar una invitación el usuario desaparece del módulo | **Confirmado.** Dos causas encadenadas + no existe reactivación | C — cambio | 5 |
| N4 | No impedir repetir la contraseña anterior | **No es bug: la validación nunca existió** | C — cambio | 3 |

**Total: 11 SP.** Registro en ADO: **1 Feature + 4 HUs** (decisión del PO, 2026-08-14).

---

## 1. Diagnóstico detallado

### N1 · Mensaje al reutilizar un correo

`CreateInvitationHandler` valida duplicidad en **tres ramas separadas**, en este orden
(`services/core-api/src/Flit.Modules.Security.Application/Auth/CreateInvitation/CreateInvitationHandler.cs:32-51`):

| Orden | Condición | Alcance | Excepción → código HTTP | Mensaje actual |
|---|---|---|---|---|
| 1 | Correo de cuenta **soft-deleted** | global | `UserEmailBelongsToDeletedAccountException` | — |
| 2 | Invitación **pendiente** | **por tenant** | `InvitationAlreadyPendingException` → 409 `INVITATION_ALREADY_PENDING` | "Ya existe una invitación pendiente para este correo." |
| 3 | Usuario **activo** con ese correo | global | `UserAlreadyExistsException` → 409 `USER_ALREADY_EXISTS` | "Este correo ya tiene una cuenta activa en el sistema" |

**Hipótesis descartada:** se verificó que la activación **sí cierra la invitación**
(`UserActivationRepository.cs:53-54` pone `Status = "accepted"` y `AcceptedAt`), de modo que las
cuentas ya activadas **no** arrastran un "pending" fantasma. Si el usuario vio ese mensaje, existía
una invitación realmente pendiente (alguien fue invitado y nunca activó). El problema es de
**redacción y utilidad**, no de lógica.

**Duplicación:** los tres `catch` con sus literales están repetidos en dos endpoints —
`Flit.Api/Endpoints/SecurityEndpoints.cs:214-231` y `Flit.Api/Endpoints/AdminOtEndpoints.cs:~1692`.
Corregir uno solo deja la mitad del producto sin arreglar.

### N2 · Columna "Perfil / Rol"

El backend **sí puebla** el perfil; lo deriva en memoria (`SecurityEndpoints.cs:1163-1168`):

```
roleCode == SuperAdmin        → FLIT
tenantType == TRANSIT_OFFICE  → OT
todo lo demás                 → GESTOR
```

Es decir, **por diseño todo usuario de una compañía tiene perfil "Gestor"**, sea Administrador de
Compañía o Radicador. Perfil y rol son dos ejes distintos y el perfil solo admite 3 valores
(`frontend/lib/users/profiles.ts:11`). El frontend replica la misma regla como respaldo
(`profiles.ts:74-83`) — no hay ningún valor hardcodeado.

Lo que falla es la **presentación**: `ProfileRoleCell` apila los dos ejes en una celda — un chip de
color con el perfil y debajo el rol en `text-[11px] opacity-80`
(`frontend/components/atom/modules/users/ProfileRoleCell.tsx:47-53`). El ojo lee el chip "GESTOR" y
el rol real pasa desapercibido.

⚠️ **`UsersTable` es un componente compartido** (módulo Usuarios, ficha de compañía, hub OT). El
grid de encabezados está en `UsersTable.tsx:265-270` y la celda en `:334-339`. `ProfileBadge`
además lo reutiliza `EditUserModal`, así que debe seguir exportado.

### N3 · Cancelar invitación borra al usuario de la lista

Dos causas encadenadas:

1. `InvitationRepository.CancelAsync` (`:143-150`) no borra físicamente, pero marca
   `Status = "cancelled"` **y además** `DeletedAt` / `DeletedBy`.
2. Ambos listados incluyen invitaciones **solo si `Status == "pending"`**
   (`SecurityEndpoints.cs:945`, `AdminOtEndpoints.cs:1823-1840`). No hay un filtro que excluya
   canceladas: quedan fuera **por construcción**.

**No existe reactivación.** El único camino de vuelta sería reenviar, pero
`ResendInvitationHandler.cs:31-32` exige `Status == "pending"` y lanza `InvitationNotPendingException`
(409) si está cancelada. Hoy la única salida es crear una invitación nueva — que funciona porque
`ExistsPendingAsync` solo cuenta las pendientes, dejando el correo libre.

Además, el `Status` del listado solo admite `"active" | "inactive" | "pending"`
(`SecurityEndpoints.cs:701,725,932`): **`"cancelled"` no existe como estado presentable**, ni en el
DTO ni en el mapa `STATUS_BADGE` del frontend (`UsersTable.tsx:76-82`).

### N4 · Impedir repetir la contraseña

Ninguno de los flujos que fijan contraseña compara la nueva contra la vigente.
`ChangePasswordHandler` verifica la contraseña *actual* solo para autenticar y persiste la nueva sin
compararlas (`ChangePasswordHandler.cs:19-49`). **No existe tabla de histórico**
(`password_reset_tokens` es otra cosa: tokens de recuperación).

Los flujos que fijan contraseña son cuatro (`Flit.Api/Endpoints/AuthEndpoints.cs`):
`change-password` (:137), `reset-password` (:73), `admin/reset-password` (:102) y `activate` (:174).
**`activate` es NA** — no hay contraseña previa contra la cual comparar.

Con Argon2 se puede verificar el texto plano nuevo contra el hash vigente
(`IPasswordHasher.Verify`), así que **la regla pedida no requiere tabla ni migración**.

---

## 2. Decisiones tomadas (PO, 2026-08-14)

| ID | Decisión | Alternativa descartada |
|---|---|---|
| **D1** | **Unificar** el mensaje de las 3 ramas de duplicidad en `"El correo utilizado ya se encuentra asociado a otra cuenta"`, conservando **códigos de error distintos** | Mensaje accionable por caso — descartado: revela a un atacante si un correo tiene cuenta o invitación (enumeración) |
| **D2** | La invitación cancelada **se ve y se puede reactivar** (regenera enlace y reenvía correo) | Solo verla, sin reactivar |
| **D3** | La contraseña nueva se compara **solo contra la vigente**. Sin histórico, sin migración, sin ADR | Histórico de las últimas N (~+3 SP) |
| **D4** | Registro en ADO: **1 Feature + 4 HUs**, una rama, un PR | 2 Bugs + 2 HUs sueltos |

---

## 3. Descomposición en HUs

### HU-1 · [BACKEND] Unificar el mensaje de correo ya utilizado — 1 SP

**Archivos:** `Flit.Api/Endpoints/SecurityEndpoints.cs:214-231` · `Flit.Api/Endpoints/AdminOtEndpoints.cs:~1692`

Centralizar el literal en **una sola constante compartida** para que los dos endpoints no vuelvan a
divergir. Conservar los tres códigos de error (`INVITATION_ALREADY_PENDING`, `USER_ALREADY_EXISTS`,
y el de cuenta eliminada) para no perder trazabilidad en logs y auditoría.

**Tarea de verificación:** confirmar que el frontend no sobrescribe la copy con un texto propio por
código de error; si lo hace, alinear también ahí.

```gherkin
Escenario: Correo con invitación pendiente
  Dado un correo con una invitación en estado "pending" en el tenant actual
  Cuando intento crear un usuario con ese correo
  Entonces recibo 409 con código "INVITATION_ALREADY_PENDING"
  Y el mensaje es "El correo utilizado ya se encuentra asociado a otra cuenta"

Escenario: Correo de una cuenta activa
  Dado un usuario activo con ese correo
  Cuando intento crear un usuario con ese correo
  Entonces recibo 409 con código "USER_ALREADY_EXISTS"
  Y el mensaje es "El correo utilizado ya se encuentra asociado a otra cuenta"

Escenario: Correo de una cuenta eliminada
  Dado un usuario soft-deleted con ese correo
  Cuando intento crear un usuario con ese correo
  Entonces el mensaje es "El correo utilizado ya se encuentra asociado a otra cuenta"

Escenario: Misma respuesta por la ruta del Organismo de Tránsito
  Dado cualquiera de los tres casos anteriores
  Cuando la petición entra por el endpoint de invitaciones del OT
  Entonces el mensaje es idéntico al de la ruta de compañía
```

### HU-2 · [FRONTEND] Separar Perfil y Rol en dos columnas — 2 SP

**Archivos:** `frontend/components/atom/modules/users/UsersTable.tsx:265-270,334-339` ·
`frontend/components/atom/modules/users/ProfileRoleCell.tsx`

Dividir la celda compuesta en dos columnas independientes: **Perfil** (chip) y **Rol** (texto), con
el rol a tamaño legible en vez de `text-[11px] opacity-80`. Ajustar el grid template para acomodar
la columna nueva. `ProfileBadge` debe seguir exportado (lo consume `EditUserModal`).

⚠️ Verificar los **tres** consumidores de `UsersTable`: módulo Usuarios, ficha de compañía y hub OT.
Arrastra la skill `flit-design-guardian`.

```gherkin
Escenario: Administrador de compañía
  Dado un usuario con rol "Administrador de Compañía" en un tenant de tipo COMPANY
  Cuando abro el módulo Usuarios
  Entonces la columna "Perfil" muestra "Gestor"
  Y la columna "Rol" muestra "Administrador de Compañía" en una celda distinta

Escenario: Usuario sin rol asignado
  Dado un usuario sin asignación de rol activa
  Cuando abro el módulo Usuarios
  Entonces la columna "Rol" muestra "Sin rol"

Escenario: Las tres pantallas que comparten la tabla siguen intactas
  Dado el módulo Usuarios, la ficha de compañía y el hub OT
  Cuando se renderiza la tabla en cada uno
  Entonces las columnas se alinean sin desbordes horizontales
```

### HU-3 · [FULLSTACK] Invitación cancelada visible y reactivable — 5 SP

**Backend**
- Incluir `Status == "cancelled"` en las dos consultas de listado (`SecurityEndpoints.cs:945`,
  `AdminOtEndpoints.cs:1823-1840`) y proyectar el estado real en vez del literal `"pending"`.
- Añadir `"cancelled"` a los estados admitidos del listado (hoy `active | inactive | pending`).
- **Endpoint nuevo** `POST /api/v1/security/invitations/{id}/reactivate` + `ReactivateInvitationHandler`.
  Política `AdminCompany`, la misma que ya exige el `DELETE /invitations/{id}` (`SecurityEndpoints.cs:266`).
  Precondiciones: `Status == "cancelled"`; **no** puede existir otra invitación `pending` para ese
  correo en el tenant; **no** puede existir un usuario activo con ese correo. Efecto: token nuevo,
  `Status = "pending"`, `DeletedAt`/`DeletedBy` limpiados, correo reenviado.
- **Decisión técnica pendiente para `architecture-agent`:** `CancelAsync` hoy marca `DeletedAt`
  además del estado. Si la invitación cancelada pasa a ser visible y reversible, `DeletedAt` deja de
  ser semánticamente correcto. Recomendación: dejar de marcarlo en la cancelación (conservando
  `UpdatedAt`/`UpdatedBy` para auditoría) tras verificar que ningún otro consumidor dependa de él.

**Frontend**
- `"cancelled"` → badge "Cancelada" en `STATUS_BADGE` (`UsersTable.tsx:76-82`) y en el tipo `TenantUser`
  (`frontend/lib/api/security.ts`).
- Acción de fila **⟳ Reactivar**, visible solo en filas canceladas (espejo de `ResendInvitationButton`,
  `Usuarios.tsx:365-374`), con su método de cliente en `lib/api/security.ts`.

```gherkin
Escenario: La invitación cancelada permanece en el listado
  Dado un usuario con invitación pendiente
  Cuando cancelo su invitación
  Entonces sigue apareciendo en el módulo Usuarios con estado "Cancelada"

Escenario: Reactivar una invitación cancelada
  Dado un usuario con invitación en estado "Cancelada"
  Cuando pulso "Reactivar"
  Entonces su estado vuelve a "Pendiente"
  Y se genera un enlace de activación nuevo
  Y se reenvía el correo de invitación
  Y el enlace anterior deja de ser válido

Escenario: No se puede reactivar si el correo ya se ocupó
  Dado un usuario con invitación en estado "Cancelada"
  Y otra invitación pendiente o una cuenta activa con el mismo correo
  Cuando intento reactivarla
  Entonces la operación se rechaza con un mensaje explicativo

Escenario: Solo el administrador de compañía puede reactivar
  Dado un usuario autenticado sin rol AdminCompany ni SuperAdmin
  Cuando invoca el endpoint de reactivación
  Entonces recibe 403
```

### HU-4 · [FULLSTACK] Impedir reutilizar la contraseña vigente — 3 SP

**Backend** — nueva excepción `PasswordReusedException` → 409 `PASSWORD_REUSED`, aplicada en los
**tres** handlers que fijan contraseña sobre una cuenta existente:
`ChangePasswordHandler`, `ResetPasswordHandler` y `AdminResetPasswordHandler`. La comparación es
`passwordHasher.Verify(nuevaContraseña, hashVigente)` antes de persistir. `ActivateAccountHandler`
queda **fuera de alcance** (no hay contraseña previa).

**Frontend** — mostrar el mensaje del error 409 en las pantallas de cambio y de restablecimiento.
La validación **no se puede espejar en cliente** (no hay hash disponible), así que
`frontend/lib/auth/password-policy.ts` no cambia: es un error de servidor, no una regla de formato.

```gherkin
Escenario: Cambio de contraseña con la misma contraseña
  Dado un usuario autenticado
  Cuando cambia su contraseña por una idéntica a la vigente
  Entonces recibe 409 con código "PASSWORD_REUSED"
  Y la contraseña no se modifica

Escenario: Restablecimiento por token con la misma contraseña
  Dado un token de recuperación válido
  Cuando fija una contraseña idéntica a la vigente
  Entonces recibe 409 con código "PASSWORD_REUSED"

Escenario: Restablecimiento administrativo con la misma contraseña
  Dado un administrador restableciendo la contraseña de un usuario de su ámbito
  Cuando fija una contraseña idéntica a la vigente de ese usuario
  Entonces recibe 409 con código "PASSWORD_REUSED"

Escenario: Contraseña distinta
  Dado cualquiera de los flujos anteriores
  Cuando la contraseña nueva difiere de la vigente y cumple la política
  Entonces el cambio se aplica correctamente
```

---

## 4. Secuencia de ejecución

| Fase | Qué | Agente | Gate |
|---|---|---|---|
| 0 | Feature + 4 HUs en ADO (sprint siguiente, tag DOR) | `po-agent` → `tech-lead-agent` | — |
| 1 | Diseño N3: máquina de estados de la invitación + contrato de reactivación + decisión sobre `DeletedAt` | `architecture-agent` | ADR `Propuesto` si cambia la semántica del estado |
| 2 | HU-1 | `backend-agent` | Activar HU |
| 3 | HU-4 | `backend-agent` → `frontend-agent` | Activar HU |
| 4 | HU-3 | `backend-agent` → `frontend-agent` | Activar HU |
| 5 | HU-2 | `frontend-agent` (+ `flit-design-guardian`) | Activar HU |
| 6 | Tests unitarios + evidencias PASO 6 | `dev-tester` | — |
| 7 | Review formal + seguridad | `code-review-agent`, `security-agent` | — |
| 8 | PR y merge a `develop` | `integration-agent` | **Confirmación humana + reviewer humano real + work items registrados** |

HU-2 va al final a propósito: es la única que toca un componente compartido por tres pantallas, y
conviene aislarla del ruido de las otras tres.

---

## 5. Riesgos y notas

| Riesgo | Mitigación |
|---|---|
| **Regla FLIT 9 (PR ≤ 800 líneas).** HU-3 es la más pesada (endpoint nuevo + 2 listados + UI) | Si el diff acumulado se acerca al límite, HU-3 sale en su propio PR |
| `UsersTable` compartido por 3 pantallas | HU-2 verifica las tres antes de dar por cerrada |
| El literal del mensaje está duplicado en 2 endpoints | HU-1 lo centraliza en una constante única |
| La suite backend arrastra fallos preexistentes en `develop` | Correr **tests filtrados** por HU, nunca la suite completa como gate |
| `DeletedAt` en invitaciones canceladas queda semánticamente inconsistente al hacerlas visibles | Decisión explícita en Fase 1 antes de implementar HU-3 |

### Defecto latente detectado (fuera de alcance)

`ExistsPendingAsync` filtra **por tenant** pero `UserExistsWithEmailAsync` es **global**
(`InvitationRepository.cs:9-15`). Dos compañías pueden crear invitaciones pendientes para el mismo
correo; la segunda que intente activar revienta contra el índice único global de `users.email` con
un error crudo de constraint. **No se incluye en este plan** — se propone radicarlo como Bug aparte.

---

## 6. Fase 1 — Resultado del diseño (2026-08-14, `architecture-agent`)

Diseño completo en **`services/core-api/docs/adr/ADR-0048-invitacion-cancelada-estado-vivo-reversible.md`**
(estado `Propuesto`). Resumen de lo que cambia respecto a este plan:

### 6.1 Correcciones a las anclas de §1 y §3

| Este plan decía | Verificado |
|---|---|
| Un solo filtro `Status == "pending"` en `SecurityEndpoints.cs:945` | **Cuatro** filtros: `SecurityEndpoints.cs:801`, `:859`, `:945` y `AdminOtEndpoints.cs:1825`. Tocar solo uno deja al SuperAdmin y al hub OT sin ver canceladas |
| `TenantUserDto` en `:1150` | `:1156`. Además el literal `"pending"` está hardcodeado en la proyección (`:812`, `:868`, `:954`, `AdminOtEndpoints.cs:1836`) |
| Policy del `DELETE /invitations/{id}` en `:266` | `:344` (`AdminCompanyPolicy`) |

### 6.2 Decisión resuelta: `CancelAsync` deja de marcar `DeletedAt`

Ningún consumidor de producción lee `UserInvitation.DeletedAt` — solo se escribe
(`InvitationRepository.cs:147`). El costo son **dos aserciones de test**
(`AdminOtUsersEndpointsTests.cs:575`, `SecurityInvitationsRoleResolutionTests.cs:211`).
Conservarlo consagraría un dato falso: cualquier query futura con el criterio estándar
`DeletedAt == null` volvería a esconder las canceladas — el mismo bug, reintroducido por la
puerta de atrás.

### 6.3 Contrato de reactivación

`POST /api/v1/security/invitations/{id}/reactivate` **y** `POST /api/v1/admin/ot/invitations/{id}/reactivate`.
Hacen falta las dos: `AdminCompanyPolicy` no incluye `ot_admin`, así que con una sola ruta el OT
podría cancelar pero no reactivar. Sin cuerpo; token **siempre nuevo** (el original solo existe
como hash); reactivar es **una sola acción** (vuelve a `pending` + reenvía correo); **no
idempotente** (segunda llamada → 409) y sujeta al mismo `ResendCooldown`, porque
`cancel → reactivate` en bucle sería un bypass del antiabuso de `/resend`.

### 6.4 Hallazgos nuevos que afectan la implementación

- **Índice único parcial** `uq_user_invitations_tenant_email_pending ON (tenant_id, email) WHERE status = 'pending'`
  (`Sql/Ddl/03-HU10147-invitations.sql:23`): volver a `pending` con otra pendiente del mismo correo
  **revienta en BD**. Hay que pre-validar *y* mapear la violación de unicidad a 409.
- **Sin migración de esquema.** `status` es `varchar(20)` sin `CHECK`; `"cancelled"` ya se escribe hoy.
  Solo un `UPDATE` opcional de normalización del histórico.
- **El riesgo real del frontend no es el badge:** hoy `status !== "pending"` se usa como sinónimo de
  «esta fila es un usuario de verdad». Con `"cancelled"` en el listado, nueve guardas
  (`Usuarios.tsx:210,221,231,241,266`; `OtUsersSection.tsx:175,185,195,219`;
  `CompanyUsersPanel.tsx:60,104`) ofrecerían Editar / Suspender / Eliminar sobre una fila cuyo `id`
  es un `invitationId`. Mitigación: helper único `isInvitationRow` y ampliar la unión de TS para que
  el compilador señale cada sitio.

### 6.5 Deuda destapada, no incluida en el alcance

1. `user_invitations.expires_at` es **columna muerta** (entidad + migraciones, nunca leída ni escrita
   en `src/`): **las invitaciones no caducan nunca**, un enlace de activación es válido indefinidamente.
2. `POST /invitations` y `POST /invitations/{id}/resend` **no tienen policy propia** — solo el
   `RequireAuthorization()` del grupo (`SecurityEndpoints.cs:32`), mientras el `DELETE` sí exige
   `AdminCompanyPolicy`. Cualquier usuario autenticado del tenant puede invitar y reenviar.
3. El defecto latente de §5 (`ExistsPendingAsync` por tenant vs `UserExistsWithEmailAsync` global).
