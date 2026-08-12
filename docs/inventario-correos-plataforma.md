# Inventario de correos electrónicos de la plataforma FLIT

> Generado: 2026-08-12 · Rama `feature/notificaciones-banco-plantillas-flit-renting` @ `c1d8c794`
> Reemplaza la versión del 2026-08-10 (`develop` @ `2f9a9ea9`), que quedó **obsoleta en sus tres
> hallazgos principales** tras los Features #11347 / #11348 / #11349.
> Método: barrido en solo lectura de `services/core-api/`, `frontend/` y `services/python-ml/`,
> más las migraciones SQL y los ADR.

**Qué cambió respecto de la versión anterior** — si vienes de ella, corrige estas tres creencias:

| Afirmación del doc del 2026-08-10 | Estado real hoy |
|---|---|
| «No hay catálogo de plantillas» | **Falso.** `NotificationTemplateCatalog` enumera 8 plantillas con id estable |
| «`notification_channel` no enruta ningún correo» | **Falso.** `TenantChannelEmailRouter` enruta cada envío. Cerraba el Bug #11311 |
| «El canal API Renting no tiene adaptador» | **Falso.** `RentingEmailApiSender` existe, con login, caché de token y mTLS |
| «Cero correos de negocio del trámite» | **Sigue siendo cierto en producción** — ver §5.1 |

---

## 1. Arquitectura de envío

```
IEmailSender.SendAsync(EmailMessage, ct) → EmailSendResult
  EmailMessage(TenantId?, TemplateKey, ToEmail, ToName, Subject, HtmlBody)   ← sin adjuntos, sin CC/BCC

  └── NotificationDeliveryLoggingEmailSender     ← decorador de bitácora
       └── TenantChannelEmailRouter              ← resuelve el canal del tenant
            ├── SmtpEmailSender (MailKit)        → FLIT_SMTP
            ├── ConsoleEmailSender               → solo Development sin Smtp:Host
            └── RentingEmailApiSender            → TENANT_API (login + caché de token + mTLS + multipart)
```

| Pieza | Ruta |
|---|---|
| Puerto de dominio | `Flit.Modules.Security.Domain/Auth/IEmailSender.cs:123-133` |
| Contrato del mensaje | `IEmailSender.cs:25-26` — ahora lleva `TenantId` y `TemplateKey` |
| Enrutador por canal | `Flit.Infrastructure/Notifications/Routing/TenantChannelEmailRouter.cs:90-211` |
| Decorador de bitácora | `Notifications/DeliveryLog/NotificationDeliveryLoggingEmailSender.cs:46-103` |
| Adaptador Renting | `Notifications/Renting/RentingEmailApiSender.cs:41-266` |
| Registro en DI | `Flit.Infrastructure/InfrastructureExtensions.cs:304-341` (Scoped) y `:776` (`AddRentingChannel`) |

**Invariantes que el código sostiene hoy:**

- Si el canal es `TENANT_API` y el adaptador Renting no está registrado en el ambiente, el envío
  devuelve `ConfigurationIncomplete`. **Nunca cae al SMTP de FLIT como respaldo silencioso**
  (`TenantChannelEmailRouter.cs:153-159`).
- Las 4 plantillas del módulo Seguridad **ignoran el canal del tenant** y salen siempre por el SMTP
  de FLIT (bypass AC3, `IsAccountEmail` en `TenantChannelEmailRouter.cs:57-67`). Es una decisión del
  PO: los correos de cuenta no dependen de un tercero.
- `IExplicitChannelEmailSender` es una **segunda vía deliberadamente separada** de `IEmailSender`,
  para que el banco de pruebas fuerce un canal sin resolver la política del tenant. No se añadió un
  campo «canal forzado» a `EmailMessage` porque sería una puerta trasera en los 6 puntos de envío de
  producción. Un test de baseline falla si aparece un tercer consumidor
  (`ExplicitChannelEmailSenderRegistrationTests`).

`frontend/` y `services/python-ml/` **no envían correo**: cero `mailto:`, cero `smtplib`, cero
`sendgrid`. Todo el correo saliente pasa por el puerto único.

---

## 2. Catálogo de plantillas — 8, todas en código

`Flit.Infrastructure/Notifications/Catalog/NotificationTemplateCatalog.cs:42-84`.

**No existe ninguna plantilla en base de datos.** Las dos tablas nuevas son de infraestructura, no
de contenido: `admin.notification_delivery_logs` (bitácora) y la configuración del banco de pruebas
(`NotificationTestSettings`, buzón + enfriamiento, sembrada con una sola fila por la migración
`67-HU11365`).

Los ids son **literales escritos a mano**, nunca `nameof`/`typeof`
(`NotificationTemplateCatalog.cs:24-26`): renombrar una clase no debe romper la identidad estable.

| id de catálogo | Asunto real | Módulo | Composer | Disparador productivo | Canal |
|---|---|---|---|---|---|
| `security.invitation` | `Invitación a FLIT — Activa tu cuenta` | Seguridad · Usuarios | `Auth/InvitationEmailTemplate.cs:11` | `CreateInvitationHandler.cs:63` · `ResendInvitationHandler.cs:50` | Siempre FLIT SMTP |
| `security.forgot-password` | `Recuperación de contraseña — FLIT` | Seguridad · Auth | `Auth/ForgotPassword/ForgotPasswordEmailTemplate.cs:11` | `ForgotPasswordHandler.cs:48` | Siempre FLIT SMTP |
| `security.admin-reset-password` | `Tu contraseña fue restablecida — FLIT` | Seguridad · Auth | `Auth/AdminResetPassword/AdminResetPasswordEmailTemplate.cs:17` | `AdminResetPasswordHandler.cs:76` | Siempre FLIT SMTP |
| `security.welcome-registration` | `¡Gracias por registrarte! — FLIT` | Seguridad · Auth | `Auth/WelcomeRegistrationEmailTemplate.cs:9` | **ninguno** | Siempre FLIT SMTP |
| `analytics.scheduled-report` | `[FLIT] {schedule.Name} — {periodo}` | Analítica · Informes | `Analytics/Scheduling/SchedulerEmailComposer.cs` | `AnalyticsSchedulerProcessor.cs:230` | Según canal del tenant |
| `analytics.alert` | `[FLIT] Alerta: {rule.Name}` | Analítica · Alertas | `SchedulerEmailComposer.cs` | `AnalyticsSchedulerProcessor.cs:397` | Según canal del tenant |
| `tramites.aprobado` | `[FLIT] Notificación radicación del trámite — {placa} — APROBADO` | Trámites | `Notifications/Tramites/TramiteCambioEstadoEmailComposer.cs:25,61-79` | **ninguno** | FLIT / Renting (dos variantes) |
| `tramites.rechazado` | `[FLIT] Notificación radicación del trámite — {placa} — RECHAZADO` | Trámites | `TramiteCambioEstadoEmailComposer.cs:26` | **ninguno** | FLIT / Renting |

### 2.1 Puntos de envío de producción

`new EmailMessage(` aparece en **7 sitios** de `src`: los 6 disparadores productivos de la tabla
(invitación y reenvío comparten plantilla) más `NotificationTestSendAdminService.cs:233`, que es el
banco de pruebas. No hay más caminos de salida.

### 2.2 Golden files

| Módulo | Cobertura |
|---|---|
| Seguridad | `tests/Flit.Modules.Security.Application.Tests/Auth/SecurityEmailGoldenTests.cs` |
| Analítica | `tests/Flit.Infrastructure.Tests/Scheduling/AnalyticsEmailGoldenTests.cs` |
| Compartido | `tests/Shared/EmailGolden.cs` |
| **Trámites** | **No se halló** un `*GoldenTests.cs` dedicado a `TramiteCambioEstadoEmailComposer` |

---

## 3. Correo enviado por un tercero — Kyverum Verify

**FLIT no compone ni controla este correo.** Llama a `IKyverumVerifyClient.StartVerificationAsync` y
**Kyverum notifica al sujeto** usando `subjects[].email` (`KyverumVerifyClient.cs:51-52`). Por eso no
hay plantilla en el repo, ni control sobre asunto, contenido o remitente.

No tiene entrada en el catálogo backend (grep de `kyverum` sobre `Notifications/` = 0 coincidencias).
En el banco de pruebas es una **fila sintética inyectada en el frontend**
(`NotificacionesBankPanel.tsx:31-35`), informativa, sin acciones y con el motivo a la vista.

Los 9 disparadores hacia Kyverum se documentaron en la versión anterior de este inventario y
**no se re-verificaron línea a línea en esta pasada** — reverificar antes de citarlos como
definitivos:

| # | Módulo | Disparador | Código |
|---|---|---|---|
| 1 | Trámites · Prevalidación | Prevalidación standalone (solo persona natural) | `IniciarPrevalidacionCommand.cs:169` |
| 2 | Trámites · Identidad | Botón del wizard | `KyverumVerifyCommand.cs:133` |
| 3 | Trámites · Identidad | **Automático.** El wizard guarda un actor → `ensureIdentity` | `TramiteWizard.tsx` |
| 4 | Trámites · Identidad | **Automático.** PJ sin RL utilizable ni cobertura de baúl (error descartado a propósito) | `ActorsCommand.cs:369` |
| 5 | Trámites · Prevalidación | Cambiar el correo del sujeto dispara reenvío | `EditarPrevalidacionCommand.cs:96` |
| 6 | Trámites · Prevalidación | Reenvío manual — tope 3, cooldown 5 min | `ReenviarPrevalidacionHandler` |
| 7 | Admin · Representantes legales | Validar al RL de una compañía | `AdminLegalRepresentativeIdentityEndpoints.cs:31,43` |
| 8 | Admin · Mandatarios | OT valida a un firmante de mandato | `AdminMandateSignerIdentityEndpoints.cs:33,45` |
| 9 | Infra · Outbox | **Automático.** Reintento de envíos fallidos | `IdentityValidationSendRetryProcessor.cs` |

> La ruta admin (7, 8) va deliberadamente **sin guard de precedencia** —
> `ADR-0034-validacion-identidad-admin-desacoplada.md` (Aceptado)— porque ahí una validación en curso
> sí se puede reenviar.

---

## 4. Canales y banco de pruebas

### 4.1 Canales

`Flit.Admin.Domain/Companies/Settings/NotificationChannel.cs:8-15`

| Valor DB | Wire | Enum | Etiqueta UI |
|---|---|---|---|
| `flit_smtp` | `FLIT_SMTP` | `FlitSmtp = 0` | «Colas FLIT» (default) |
| `tenant_api` | `TENANT_API` | `TenantApi = 1` | «API Renting cliente» |

El adaptador Renting se registra condicionalmente (`AddRentingChannel`, gobernado por
`RENTING_API_ENABLED`). Autenticación por login + mTLS: `RentingLoginClient`,
`RentingClientCertificateLoader`, `RentingTokenCache`. Mapea 401/400/429/503 a causas cerradas y
sanea secretos en logs.

### 4.2 Banco de pruebas

Pantalla `/admin/plataforma/notificaciones` (submenú Plataforma) —
`frontend/components/admin/plataforma/NotificacionesBankPanel.tsx`, con
`NotificacionBuzonPruebasSection` para el buzón.

Lista **9 filas**: las 8 del catálogo + Kyverum (sintética).

| Endpoint | Qué hace |
|---|---|
| `GET /api/v1/admin/plataforma/notificaciones/plantillas` | Lista íntegra del catálogo |
| `GET .../plantillas/{templateId}/muestra` | Render de muestra, `?channel=` opcional (FLIT_SMTP por defecto) |
| `GET` / `PUT .../buzon-pruebas` | Consulta y fija el buzón de pruebas (una sola fila, a nivel plataforma) |
| `GET .../canales` | Canales disponibles |
| `POST .../buzon-pruebas/envios` | Envío de prueba, con límite de frecuencia (429) |

Todo bajo **`SuperAdminPolicy` en backend**, no solo guardia de cliente
(`AdminPlataformaNotificacionesPlantillasEndpoints.cs:51-67`).

**Dos barreras que conviene no romper:**

1. El render de muestra rechaza con **400** cualquier `tramiteId` o `usuarioId` *antes* de resolver
   el catálogo (`...PlantillasEndpoints.cs:86-99`). Cierra el canal lateral hacia datos reales.
2. Pedir envío de prueba con `TENANT_API` sobre una plantilla de cuenta responde **400
   `plantilla_sin_enrutamiento_por_canal`**: en producción esas no se enrutan por canal, y permitir
   el envío haría creer lo contrario — que es justo el Bug #11311.

Outcomes cerrados del envío: `Sent`, `TransportFailed`, `TemplateNotFound`, `InvalidChannel`,
`MailboxNotConfigured`, `ChannelNotConfigured`, `TemplateChannelMismatch`, `RateLimited`,
`RenderFailed`.

---

## 5. Hallazgos

### 5.1 Tres plantillas del catálogo no las dispara ningún flujo de negocio

`security.welcome-registration`, `tramites.aprobado` y `tramites.rechazado` tienen composer, muestra,
fila en el banco y (las de Security) golden file — pero **solo son alcanzables desde el banco de
pruebas**. Sus propios comentarios lo declaran: *«sin disparador productivo aún»*
(`TramiteCambioEstadoEmailComposer.cs:8`) y *«el handler productivo se conecta en una fase
posterior»* (`NotificationTrigger.cs:22-26`).

Consecuencia práctica: **ningún actor del trámite** (comprador, vendedor, radicador, OT) recibe todavía
aviso por correo sobre su trámite. La infraestructura está lista; falta conectarla.

### 5.2 El banco de pruebas no deja rastro en la bitácora

`admin.notification_delivery_logs.tenant_id` es `NOT NULL` por decisión deliberada de la DDL 64
(«sin tenant la fila es irrastreable»). El banco envía siempre sin tenant, así que sus envíos no
entran en la bitácora. Se cubre con traza de log, no relajando el esquema.

### 5.3 Sin adjuntos

`EmailMessage` solo admite `HtmlBody`. Por eso el informe programado de analítica viaja como resumen
de KPIs, con un pie fijo que aclara que el archivo Excel/PDF no está disponible por este canal.

### 5.4 Nota de configuración

`appsettings.Development.json` contiene `DefaultSenderPassword` en claro para una cuenta corporativa
real. El archivo **no está versionado**, así que no hay fuga en el historial — pero sigue siendo una
credencial en claro en el disco de cada desarrollador. Ver también el hallazgo de memoria sobre
`docker-compose.yml`.

---

## 6. Autenticación por código (OTP) — no existe

Pregunta recurrente, respondida con verificación explícita el 2026-08-12.

**No hay mecanismo OTP ni plantilla de correo que transporte un código.** No es «no lo encontré»:

- Los 92 hits del regex amplio (`OTP|TOTP|HOTP|2FA|MFA|two-factor|código de verificación|
  passwordless|magic link|authenticator|challenge|…`) se revisaron uno a uno: **todos falsos
  positivos**, casi siempre por las siglas **OT** de *transit office* (`OtProfile`, `OtPrenda`) o por
  `PortalCommand`/`Payload`. Los 2 hits en ADR y docs también (`UpdateOtProfileRequest` en
  `ADR-0024`).
- `otp|verification_code|two_factor|mfa_secret` sobre **todo el DDL SQL** → **0 resultados**. No hay
  tabla ni columna de código, intentos, expiración ni factor.
- El frontend no tiene ningún componente de input de código de N dígitos. El login es
  `type="password"` (`Login.tsx:199-213`).
- Ninguna de las 8 plantillas transporta un código.
- Kyverum hace validación **biométrica** contra un `verification_id`; no envía un código para teclear.

Los tres flujos de credencial existentes son de otra naturaleza — un token en enlace se consume
haciendo clic, un OTP se teclea:

| Flujo | Qué viaja en el correo |
|---|---|
| Invitación / reenvío | **Token en enlace** de activación (se regenera y anula el anterior) |
| Recuperación de contraseña | **Token en enlace**, TTL `TokenLifetimeMinutes` = 30 |
| Reset administrativo | **Contraseña temporal** alfanumérica en el cuerpo |

Lo más cercano es la contraseña temporal del reset administrativo, pero **no es un segundo factor**:
sustituye la credencial en lugar de complementarla y no expira como código.

Construir OTP exigiría: generador de código con expiración e intentos, persistencia nueva, endpoints
de emisión y verificación, una novena plantilla de catálogo y un componente frontend de input de N
dígitos. Toca el borde de autenticación — **es un Feature, no un ajuste**.

---

## 7. Resumen numérico

| Métrica | Valor |
|---|---|
| Plantillas propias en el catálogo | **8** |
| …con disparador productivo | **5** (6 disparadores: invitación y reenvío comparten plantilla) |
| …solo alcanzables desde el banco de pruebas | **3** |
| Plantillas de terceros (Kyverum) | **1** (sin control sobre el contenido) |
| Plantillas almacenadas en base de datos | **0** |
| Disparadores totales | **15** (6 propios + 9 hacia Kyverum, estos últimos sin re-verificar) |
| Disparadores automáticos sin UI | **5** (2 de analítica, 3 de identidad) |
| Correos que respetan `notification_channel` | **4** (2 de analítica + 2 de trámites, estas sin disparador) |
| Correos de negocio del trámite en producción | **0** |
| Mecanismos de autenticación por código (OTP) | **0** |
