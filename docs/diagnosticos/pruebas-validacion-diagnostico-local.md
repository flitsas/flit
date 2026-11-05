# Diagnóstico Vehicular — casos de prueba para validar a mano en la pila local

> Generado: 2026-10-26

Rama: `feature/AB-12881-diagnostico-d0-d2`. Cubre las olas D0, D1, D2, D3, D4 y D5, y la ola F10 (RUNT por Intempo, HUs #13873–#13878).

Este documento **no se publica en Azure DevOps**. Los TCs no se crearon como Tasks: es un set para la validación manual local.

Los títulos siguen el formato FLIT `QA_TC{##}_{MODULO}_{ALCANCE} - {ESCENARIO}`, con el módulo `DIAGNOSTICO` y un consecutivo global de 00 a 99.

Fuentes usadas:
- los JSON de HUs, `notas-ola.md` y `decisiones-usuario.md` de cada ola en `.claude/state/`;
- `contracts/openapi/diagnostico.v1.yaml`;
- `services/core-diagnostico/README.md`;
- el código de `frontend-diagnostico/`, de donde salen todos los textos de pantalla citados;
- los adaptadores simulados de `services/core-diagnostico/.../Consultas/Fuentes/`.

---

## 1. Cómo usarlo y precondiciones

### 1.1 Cómo usarlo

- Los casos están en orden de recorrido. Sigue el orden numérico, con dos excepciones:
  - **TC05, TC06 y TC10 van al final del modo simulado**, después de TC94, porque necesitan lotes ya creados.
  - **TC95 a TC97 piden reiniciar la pila en modo `real`**: corre TC97, paso 1, antes de reiniciar.
- Cada caso trae **Origen** (HU y escenario o AC Gherkin), **Tipo** (Happy Path, Borde o Error), **Prioridad**, **Precondición**, **Datos**, **Pasos**, **Resultado esperado** y la casilla de resultado.
- Los casos **(condicional)** necesitan algo que la pila no trae por defecto, como un usuario con otro rol o la bandera del impuesto. Si no lo puedes preparar, marca «Falla» en blanco y escribe **«Bloqueado: …»** en Observaciones.
- Algunos casos verifican la API desde la consola del navegador (F12 → Consola), con la sesión ya iniciada en `http://127.0.0.1:4024`. Usan la ayuda **H1** de §1.6.
- **Siempre `http://127.0.0.1`, nunca `localhost`.** El login OIDC del hub está registrado con `127.0.0.1`.
- **Si repites el set el mismo día**, cambia el dígito del medio de las placas, por ejemplo de `TST141` a `TST241`. El último carácter es el que decide el escenario. Si no lo cambias, la reutilización del mismo día devuelve el dato anterior y altera los casos que esperan una consulta nueva.
- Los defectos van a la tabla de §5. Usa solo datos inventados.

### 1.2 Entorno local, ya levantado

| Componente | URL o puerto | Nota |
|---|---|---|
| hub (login de la suite) | http://127.0.0.1:4022 | La tarjeta Diagnóstico puede decir «Próximamente» porque P5 está pendiente. Entra directo por la URL del producto. |
| frontend-diagnostico | http://127.0.0.1:4024 | La app que se prueba. |
| gateway | 4002 | |
| core-api | 4003 (gRPC 8082) | Login OIDC, catálogos y tipos de trámite. |
| core-consultas | 4026 (gRPC 8084) | Todo en `mock`. `INTEMPO_API_BASE_URL` vacía. |
| core-notificaciones | 4027 | Los correos solo van al log. |
| core-diagnostico | 4032 (gRPC 8088) | `Diagnostico__Ambiente=LOCAL` y `Diagnostico__Identidad__SimularSuscripcionActiva=true`. |
| RabbitMQ | 5672 (consola http://127.0.0.1:15672) | vhost `flit` |
| PostgreSQL | 127.0.0.1:5432, base `flit_local` | Schemas `diagnostico`, `consultas`, `identity` y `platform`. |
| Trámites (frontend) | http://127.0.0.1:3000 | **El script no lo levanta.** Solo hace falta en TC00, para encender el producto. |

Comandos:

```bash
bash scripts/dev/levantar-diagnostico-local.sh simulado --sin-build   # modo de casi todo el set
bash scripts/dev/levantar-diagnostico-local.sh real --sin-build       # solo para TC95–TC97
bash scripts/dev/levantar-diagnostico-local.sh parar
```

- Los logs quedan en `C:\Users\USUARIO\.flit\logs\` (en Git Bash, `~/.flit/logs/`): `core-diagnostico.log`, `core-consultas.log`, `hub.log`, etc.
- **Cada arranque trunca el log de su servicio.**
- El script no levanta Identidad para core-diagnostico. Eso tiene dos efectos en local, que son comportamiento esperado y no defecto:
  - la suscripción se trata como activa para toda compañía;
  - el nombre del creador sale «No disponible» en el listado y en los reportes (#13845 AC4).

**Modo `simulado`.** El diagnóstico no llama a core-consultas. Todas las fuentes responden con datos inventados, de forma determinista según el último carácter:

| Último carácter de la placa o del VIN | RUNT vehículo | SIMIT vehículo (por placa) | Avalúo (por placa) |
|---|---|---|---|
| 1 | Persona natural: propietario CC 9999900001, sin locatario | Verde, sin comparendos | $ 52.000.000 (Fasecolda, Mercado Libre y base gravable) |
| 2 | Propietario NIT 999990002 | Rojo, 2 comparendos (SIM-0001 y SIM-0002) | Solo Fasecolda, $ 48.000.000 |
| 3 | Leasing: propietario NIT 999990003 y locatario CC 9999900013 | Amarillo, 1 comparendo «ACUERDO DE PAGO» | $ 52.000.000 |
| 4 | Leasing: propietario NIT 999990004 y locatario NIT 999990014 | Verde | **Sin valor comercial** |
| 5 | Servicio PUBLICO: propietario NIT 999990005 | Verde | $ 52.000.000 |
| 6 | Organismo «SECRETARIA DE TRANSITO DE PRUEBA SIN DEPARTAMENTO»: propietario CC 9999900006 | Rojo, **60 comparendos** | $ 52.000.000 |
| 7 | **Error TIMEOUT** (transitorio, se reintenta) | No se ejecuta: bloqueada por el RUNT | No se ejecuta |
| 8 | **Error RESPUESTA_INVALIDA** (definitivo) | No se ejecuta | No se ejecuta |
| Otro (letra, 0, 9) | Persona natural | Verde | $ 52.000.000 |

Las fuentes por persona dependen del **último dígito del documento**:
- **RUNT persona:** 2 inscrito sin licencias; 3 licencia B1 SUSPENDIDA; 7 TIMEOUT; 8 RESPUESTA_INVALIDA; otro, B1 ACTIVA.
- **RUES (NIT):** 6 matrícula CANCELADA; 7 TIMEOUT; 8 RESPUESTA_INVALIDA; otro, ACTIVA renovada el 2026-03-31.
- **SIMIT persona:** la misma tabla que SIMIT vehículo, aplicada al documento.

Consecuencias que conviene saber:
- SIMIT vehículo y avalúo en ERROR propio **no se pueden provocar** en simulado: los finales 7 y 8 hacen fallar antes al RUNT. Los errores de persona sí se provocan, con un **locatario declarado** que termine en 7 u 8 sobre una placa que no traiga locatario (final 1, 2, 5, 6 o letra).
- El reproceso de una celda en error vuelve a fallar, porque la simulación es determinista. El reproceso exitoso solo lo cubren las pruebas automatizadas.

**Modo `real`.** El diagnóstico usa core-consultas por gRPC (`Diagnostico__Consultas__Address=http://127.0.0.1:8084`).
- El RUNT por Intempo no tiene URL. core-consultas responde `FailedPrecondition` y la celda RUNT queda en ERROR definitivo `NO_CONECTADO`, sin reintento.
- Las demás fuentes de core-consultas están en `mock`, pero quedan bloqueadas por el RUNT.
- **Fe de erratas de `estado-implementacion-diagnostico-2026-10-23.md` §4.** Ese documento dice que faltan los modos reales de las fuentes de persona. Es un error: el adaptador real del RUNT persona sí existe (`FuenteRuntPersonaReal`), igual que los de RUES, SIMIT y avalúo. En local no se puede observar porque el RUNT del vehículo queda no conectado y bloquea las demás celdas.

**Impuesto departamental bloqueado.** `Diagnostico:Fuentes:ImpuestoDisponible` no se define, así que vale `false`. La fuente no se ofrece y la API responde 422 `FUENTE_NO_DISPONIBLE`.

### 1.3 Usuarios

| Alias | Usuario y clave | Compañía | Rol | Uso |
|---|---|---|---|---|
| **ADMIN** | `admin@empresa.local` / `AdminPass1!` | Compañía cliente (NIT 9000000002) = **EMPRESA_A** | AdminCompany → «Administrador de Diagnóstico» (admin_diagnostico) | Casi todo el recorrido. |
| **SUPER** | `demo@flit.local` / `DemoPass1!` | Compañía FLIT (NIT 9000000001, `tenant_type=FLIT`) = **EMPRESA_FLIT** | SuperAdmin | **Backoffice FLIT**. El SuperAdmin cuenta como si tuviera todos los permisos y es de la compañía FLIT, así que obtiene la capacidad `backoffice`. También es la «otra compañía» en las pruebas de aislamiento. |
| **RADICADOR** | `radicador@empresa.local` / `RadicadorPass1!` | EMPRESA_A | «Radicador» (solo Trámites) | Rol de otro producto sin rol de Diagnóstico (TC07). |
| **USUARIO_D** (condicional) | El que crees en TC00-E | EMPRESA_A | «Usuario de Diagnóstico» | TC22 y TC91. |
| **BACKOFFICE_D** (condicional) | El que crees en TC00-E | EMPRESA_FLIT | «Backoffice FLIT de Diagnóstico» | TC12. El seeder (`DevelopmentAuthSeeder.cs`) **no siembra ningún usuario con este rol**. |

### 1.4 Datos de prueba: solo inventados

- Placas `TST###`, VIN `9ZZPRUEBA` más 8 dígitos y documentos `99999…`.
- Ninguna placa real ni URL de proveedor.
- Los archivos de carga se preparan fuera del repo, por ejemplo en `~/qa-diagnostico/`:

| Archivo | Contenido |
|---|---|
| **F1** `carga-mixta.csv` | Encabezado `Identificador_Vehiculo,Tipo_Documento_Locatario,Documento_Locatario` y estas filas:<br>`TST121,,`<br>`TST122,,`<br>`TST123,,`<br>`TST124,,`<br>`ABC-12X,,`<br>`TST121,,` |
| **F1x** `carga-mixta.xlsx` | Las mismas 6 filas, en la plantilla descargada en TC36. |
| **F2** `formato.xls` | Una copia de F1 renombrada a `.xls`. |
| **F3** `documento.pdf` | Cualquier PDF sin datos personales. |
| **F3c** `falso.csv` | Una copia de F3 renombrada a `.csv`. |
| **F4** `grande.csv` | `python -I -c "open('grande.csv','w').write('Identificador_Vehiculo\n' + 'TST131\n'*900000)"` (≈ 6,3 MB) |
| **F5** `sobre-limite.csv` | `python -I -c "print('Identificador_Vehiculo'); [print(f'TST{n}') for n in range(300,501)]" > sobre-limite.csv` (201 válidos) |
| **F5b** `en-limite.csv` | Igual que F5 con `range(300,500)` (200 válidos). |
| **F6** `sin-validos.csv` | Encabezado y las filas `ABC-1`, `12345` y `9ZZPRUEBAO0000001`, que es un VIN con la letra O. |
| **F7** `documentos.csv` | Encabezado y las filas `TST126,CC,99999ABCD` y `ABC-99,CC,9999900129`. |
| **F8** `motor.csv` | Encabezado y las filas `TST141` a `TST148`, más `9ZZPRUEBA00000301`, con las columnas de locatario vacías. |

### 1.5 Lotes que se reutilizan entre casos

| Lote | Se crea en | Contenido |
|---|---|---|
| **QA Carga 01** | TC26 → TC42 | 5 vehículos (TST121–TST125), alcance RUNT + SIMIT vehículo. |
| **QA Carga 02** | TC43, se crea y nunca se confirma | Queda en CREADO («Sin lanzar»). |
| **L-MOTOR** «QA Motor 8 escenarios» | TC48 | F8 con las 7 fuentes ofrecidas; estimado «No mostrar». |
| **L-ERROR** | TC67 | Express `TST171` con locatario declarado CC `9999900018`; estimado «Al finalizar el diagnóstico». |

Para sacar el id de un lote, ábrelo y copia la URL `/diagnosticos/{id}`.

### 1.6 Ayudas

**H1. Llamar a la API desde la consola del navegador**, en http://127.0.0.1:4024 con la sesión iniciada:

```js
const api = async (ruta, metodo = 'GET', cuerpo) => {
  const r = await fetch('/api/v1/diagnostico' + ruta, { method: metodo,
    headers: cuerpo ? { 'Content-Type': 'application/json' } : undefined,
    body: cuerpo ? JSON.stringify(cuerpo) : undefined });
  console.log(r.status, await r.text()); };
```

**H2. Celdas del lote abierto, con su id**, en la pestaña del tablero `/diagnosticos/{id}`:

```js
const lote = location.pathname.split('/')[2];
const g = await (await fetch(`/api/v1/diagnostico/diagnosticos/${lote}/grilla`)).json();
console.table(g.vehiculos.flatMap(v => v.celdas.map(c => ({ vehiculo: v.identificador, fuente: c.fuente, estado: c.estado, tipo_error: c.tipo_error, motivo_na: c.motivo_na, id: c.id }))));
```

**SQL de solo lectura**, con `psql "postgresql://postgres:postgres@127.0.0.1:5432/flit_local"`. Nunca uses INSERT, UPDATE ni DELETE:

```sql
-- S1 lecturas auditadas del backoffice
select usuario_id, empresa_consultada, ruta, fecha from diagnostico.lecturas_backoffice order by fecha desc limit 5;
-- S2 cambios del contrato
select campo, valor_anterior, valor_nuevo, usuario_id, fecha from diagnostico.cambios_contrato order by fecha desc limit 10;
-- S3 intentos de un vehículo (cambia la placa)
select v.identificador_norm, i.fuente, i.servicio, i.disparador, i.estado, i.tipo_error, i.desde_cache, i.usuario_id, i.inicio, i.fin
  from diagnostico.intentos_consulta i join diagnostico.vehiculos v on v.id = i.vehiculo_id
 where v.identificador_norm = 'TST147' order by i.inicio;
-- S4 historial de un lote (cambia el id)
select estado_anterior, estado_nuevo, disparador, usuario_id, en from diagnostico.historial_estado_diagnostico
 where diagnostico_id = '<id>' order by en;
-- S5 documentos cifrados: ningún «99999» en claro
select identificador_norm, locatario_doc_origen,
       position(convert_to('99999','UTF8') in coalesce(propietario_doc_cifrado,''::bytea)) = 0 as prop_cifrado,
       position(convert_to('99999','UTF8') in coalesce(locatario_doc_cifrado,''::bytea)) = 0 as loc_cifrado
  from diagnostico.vehiculos where identificador_norm like 'TST1%';
select rol, orden, doc_tipo, origen, position(convert_to('99999','UTF8') in doc_cifrado) = 0 as cifrado from diagnostico.personas_vehiculo order by creado_en desc limit 20;
-- S6 evidencias de un lote
select tipo, vehiculo_id, estado, vigente, left(huella,12) as huella, solicitado_por, generado_en
  from diagnostico.evidencias where diagnostico_id = '<id>' order by creado_en;
-- S7 consumo del RUNT por Intempo en core-consultas
select fuente, proveedor, resultado, desde_cache, ocurrido_en from consultas.consumo
 where fuente in ('runt_intempo_placa','runt_intempo_vin') order by ocurrido_en desc limit 10;
-- S8 compañías y producto Diagnóstico
select t.id, t.tax_id, p.enabled from identity.tenants t
  left join platform.tenant_products p on p.tenant_id = t.id and p.product_code = 'diagnostico'
 where t.tax_id in ('9000000001','9000000002');
```

---

### QA_TC00_DIAGNOSTICO_PREPARACION - Encender Diagnóstico y preparar el entorno

- **Origen:** #12967 (Productos de la suite) · #13726 (Esc. 7) · #13734
- **Tipo / prioridad:** Preparación · Alta
- **Precondición:** la pila está arriba en modo `simulado`. La compañía de ADMIN **no** tiene Diagnóstico encendido.
- **Datos:** SUPER; EMPRESA_A (NIT 9000000002); EMPRESA_FLIT (NIT 9000000001).
- **Pasos:**
  1. **A. Pila.** Ejecuta `curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:4032/health`. Repite con 4026, 4003 y 4002. Cada uno debe dar `200`. Si alguno falla, arranca con `bash scripts/dev/levantar-diagnostico-local.sh simulado --sin-build`.
  2. **B. Trámites.** En otra terminal, ejecuta `pnpm --dir frontend exec next dev --webpack -H 127.0.0.1 -p 3000`. Abre http://127.0.0.1:3000 e inicia sesión como SUPER.
  3. **C. Encender.** Ve a «Administración de compañías» (`/admin/companies`), elige la compañía de NIT 9000000002 y abre la pestaña **«Productos»**. En «Productos de la FLIT Suite», activa el interruptor **«Diagnóstico»**.
  4. Repite el paso 3 con la compañía FLIT (NIT 9000000001), para las pruebas entre compañías.
  5. **D. Identificadores.** Corre S8. Anota el `id` de EMPRESA_A y el de EMPRESA_FLIT. También salen en la URL `/admin/companies/{tenantId}`.
  6. **E. Opcional, para los casos condicionales.** En la pestaña «Usuarios» de EMPRESA_A, crea o elige un usuario y dale el rol «Usuario de Diagnóstico» (USUARIO_D). En la compañía FLIT, haz lo mismo con «Backoffice FLIT de Diagnóstico» (BACKOFFICE_D). Si la pantalla no permite asignar roles de Diagnóstico, deja TC12, TC22 y TC91 como Bloqueados.
  7. **F.** Prepara los archivos F1 a F8 de §1.4.
- **Resultado esperado:**
  - Los cuatro `health` dan 200.
  - En «Productos», Diagnóstico muestra el chip **«Encendido»** y el texto «Último cambio: …, por demo@flit.local.».
  - S8 devuelve `enabled = true` para las dos compañías.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

---

## 2. Matriz de cobertura

| Ola | Feature | HU | TCs manuales | Solo automatizada (§4) |
|---|---|---|---|---|
| D0 | #13715 Esqueleto | #13721 servicio | TC02, TC03 | Migraciones en schema propio, readiness sin BD y buf lint |
| D0 | #13715 | #13722 filtro por compañía | TC08, TC09 | Prueba de fuga por endpoint y entidades sin tenant |
| D0 | #13715 | #13724 manifiesto, roles y sesión | TC04, TC12 | Registro del manifiesto y superficie sin escritura |
| D0 | #13715 | #13726 guard de suscripción | TC05, TC06, TC07 | Identidad caída (fail-closed) |
| D0 | #13715 | #13728 backoffice FLIT | TC10, TC11, TC76 | IgnoreQueryFilters acotado |
| D0 | #13715 | #13729 loader en @flit/ui | TC01 | Regresión visual en Trámites |
| D0 | #13715 | #13732 frontend con login del hub | TC01, TC05 | — |
| D0 | #13715 | #13734 despliegue y puertos | TC00 (puertos) | Compose, CD y CI |
| D0 | #13715 | #13826 prueba intermitente (`TimeProvider`) | — | **Sí, solo automatizada** |
| D0 | #13716 Configuración dos niveles | #13736 tabla de configuración | TC13 | CHECK de la base |
| D0 | #13716 | #13741 API dos niveles | TC13, TC15, TC17–TC21, TC22, TC94 | Copia inmutable (Esc. 13) |
| D0 | #13716 | #13744 pantalla Contrato y Operación | TC15, TC16, TC17, TC18, TC22 | — |
| D1 | #13717 Parametrización | #13731 gRPC ListarTiposTramite | TC25 | Permisos del rpc y buf |
| D1 | #13717 | #13738 schema de cabecera | TC26, TC08 | Migración idempotente y CHECK |
| D1 | #13717 | #13742 catálogo de tipos | TC25 | Reintento y 503 con Retry-After |
| D1 | #13717 | #13747 estimado | TC34, TC46 | Resta de N/A previsibles |
| D1 | #13717 | #13750 cabecera | TC12, TC14, TC24–TC28 | Fuentes obligatorias (catálogo vacío hasta D6) |
| D1 | #13717 | #13752 formulario de parametrización | TC23, TC24, TC25, TC29 | — |
| D1 | #13718 Captura | #13735 schema de vehículos | TC35 | ux_vehiculo_por_lote y ck_locatario_origen |
| D1 | #13718 | #13740 validador | TC35 | Tabla compartida .NET |
| D1 | #13718 | #13745 fieldRules | TC35 | Tabla compartida en el front |
| D1 | #13718 | #13749 plantilla y análisis | TC36–TC40, TC43–TC47 | Rendimiento con 500 filas |
| D1 | #13718 | #13751 revalidar y confirmar | TC41–TC44, TC71 | El inválido nunca se encola |
| D1 | #13718 | #13753 express API | TC29, TC30, TC32, TC34 | — |
| D1 | #13718 | #13754 carga masiva UI | TC37, TC39, TC41–TC47 | — |
| D1 | #13718 | #13755 captura express UI | TC29–TC34 | — |
| D2 | #13719 Cola y RUNT | #13723 esquema de celdas | TC62 (indirecto) | **Sí: CHECK, índices y migración** |
| D2 | #13719 | #13725 RUNT simulado | TC49–TC56 | Arranque con la simulación fuera de LOCAL/DEV |
| D2 | #13719 | #13727 cola durable | TC42, TC48 | Lease vencido y doble procesador |
| D2 | #13719 | #13730 celda RUNT | TC49, TC51, TC55, TC57, TC65, TC66 | — |
| D2 | #13719 | #13733 reglas N/A | TC62, TC63, TC64, TC94 | Adaptador faltante → 422 |
| D2 | #13719 | #13737 intentos y backoff | TC55, TC56, TC59, TC10 | MaxReintentos fuera de rango |
| D2 | #13719 | #13739 reutilización del mismo día | TC60, TC61 | Corte de las 00:00 de Colombia |
| D2 | #13719 | #13746 reproceso | TC74, TC75, TC22 | Reproceso exitoso que libera las bloqueadas |
| D2 | #13720 Grilla | #13743 grilla incremental | TC08, TC48, TC58, TC73, TC91 | — |
| D2 | #13720 | #13748 tablero | TC48, TC58, TC72, TC73, TC74, TC22 | — |
| D3 | #13788 Personas, SIMIT y avalúo | #13790 propietarios y locatarios | TC49, TC60, TC66, TC71 | Tres propietarios y FK compuesta |
| D3 | #13788 | #13791 adaptadores real, simulado y no conectado | TC95 | Arranque en QA con la simulación encendida |
| D3 | #13788 | #13792 RUNT persona y RUES | TC50, TC67, TC71 | Reproceso solo de la persona que falló |
| D3 | #13788 | #13793 SIMIT vehículo | TC50, TC61, TC68 | TIMEOUT y SIN_PLACA |
| D3 | #13788 | #13794 SIMIT por persona | TC51, TC54, TC62, TC67, TC71 | — |
| D3 | #13788 | #13795 avalúo | TC69, TC70, TC75 | Avalúo en ERROR |
| D3 | #13788 | #13796 estimado por persona | TC34, TC73 | — |
| D3 | #13788 | #13797 rótulo «al menos» | TC34 | — |
| D3 | #13789 Detalle y manual | #13798 detalle enmascarado | TC09, TC65, TC70, TC80 | — |
| D3 | #13789 | #13799 lotes y grilla del backoffice | TC10, TC11, TC76, TC80 | — |
| D3 | #13789 | #13800 llenado manual | TC10, TC77, TC78, TC79, TC81 | ?empresa=B sobre una celda de A |
| D3 | #13789 | #13801 detalle en el tablero | TC65, TC68, TC70, TC80 | Tres secciones por persona |
| D3 | #13789 | #13802 pantalla de backoffice | TC10, TC76–TC79 | — |
| D4 | #13840 Evidencias | #13846 huella y file-manager | TC85 | FK compuesta y doble vigente |
| D4 | #13840 | #13847 contenido del reporte | TC82 | Arquitectura: ninguna propiedad Origen |
| D4 | #13840 | #13848 PDF | TC09, TC82, TC83, TC84, TC85 | — |
| D4 | #13840 | #13849 Excel | TC09, TC86 | — |
| D4 | #13840 | #13850 ZIP | TC09, TC84, TC87 | 409 SIN_VEHICULOS_TERMINADOS |
| D4 | #13840 | #13851 descargas UI | TC82, TC83, TC84, TC87 | Sin la capacidad descargarReportes |
| D4 | #13841 Listado | #13845 listado API | TC88–TC91 | Corte de fecha a medianoche en Colombia |
| D4 | #13841 | #13852 listado UI | TC88, TC89, TC90 | — |
| D5 | #13859 Impuesto | #13860 contrato gRPC | — | **Sí, solo automatizada** |
| D5 | #13859 | #13861 réplica y N/A | TC54, TC63, TC64 | Sincronización de la réplica |
| D5 | #13859 | #13862 adaptador del impuesto | TC63, TC64 (condicionales) | **Sí, la consulta real o falsa** |
| D5 | #13859 | #13863 disponibilidad | TC92, TC93, TC94 | Bandera en true con réplica vacía |
| D5 | #13859 | #13864 detalle, PDF y Excel | TC64 (condicional) | — |
| D5 | #13859 | #13865 alcance y configuración | TC92, TC93 | — |
| D5 | #13859 | #13866 celda y detalle | TC63, TC64 (condicionales) | — |
| F10 | #13872 RUNT por Intempo | #13873 contrato gRPC | — | **Sí, solo automatizada** |
| F10 | #13872 | #13874 intérprete | — | **Sí, solo automatizada** |
| F10 | #13872 | #13875 cliente HTTP | TC97 | Status, timeout y red |
| F10 | #13872 | #13876 servicio gRPC, consumo y humo | TC95, TC96 | E2E con handler falso y humo (§4) |
| F10 | #13872 | #13877 mapeo a VehiculoRunt | TC49–TC56 (vía simulación), TC98, TC99 | Códigos de error |
| F10 | #13872 | #13878 fuente real en el diagnóstico | TC95, TC97, TC99 | Servidor falso con ENCONTRADO |

**HUs sin TC manual, cubiertas solo por las pruebas automatizadas:** #13723, #13826, #13860, #13873 y #13874. La #13862 solo tiene TCs condicionales.

**TCs por módulo:**

| Módulo | TCs |
|---|---|
| ACCESO | 01–12 (12) |
| CONFIGURACION | 13–22 (10) |
| PARAMETRIZACION | 23–28 (6) |
| EXPRESS | 29–35 (7) |
| CARGA | 36–47 (12) |
| MOTOR | 48–59 (12) |
| REUTILIZACION | 60–61 (2) |
| NOAPLICA | 62–64 (3) |
| PERSONAS, SIMIT, AVALUO y SEGURIDAD | 65–71 (7) |
| TABLERO y REPROCESO | 72–75 (4) |
| BACKOFFICE | 76–81 (6) |
| REPORTES | 82–87 (6) |
| LISTADO | 88–91 (4) |
| IMPUESTO | 92–94 (3) |
| RUNTREAL (F10) | 95–99 (5) |
| **Total** | **99 + TC00** |

---

## 3. Casos de prueba

### 3.1 Sesión, acceso, aislamiento y roles

#### QA_TC01_DIAGNOSTICO_ACCESO - Sin sesión redirige al login del hub y vuelve con la sesión
- **Origen:** #13732 (Esc. 1, 2, 3 y 5) · #13729 (Esc. 1)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** TC00. Ventana de incógnito sin sesión.
- **Datos:** ADMIN.
- **Pasos:**
  1. Abre http://127.0.0.1:4024.
  2. Observa la URL de destino.
  3. Inicia sesión en el formulario del hub con ADMIN.
  4. Espera a volver a Diagnóstico.
- **Resultado esperado:**
  - En el paso 2 la URL es la del hub (4022) y contiene `client_id=diagnostico` y `code_challenge_method=S256`.
  - Al volver aparece el loader «Abriendo Diagnóstico…» y luego la tarjeta **«Diagnóstico vehicular»** con el nombre de la compañía. No hay selector de compañía.
  - Ninguna pantalla de 4024 tiene campos de usuario o contraseña.
  - Se ven el bloque «Diagnóstico Express» y el listado.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC02_DIAGNOSTICO_ACCESO - Salud del servicio responde 200
- **Origen:** #13721 (Esc. 2)
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** la pila está arriba.
- **Datos:** —
- **Pasos:**
  1. Ejecuta `curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:4032/health`.
  2. Ejecuta lo mismo con `http://127.0.0.1:4032/health/ready`.
- **Resultado esperado:** los dos responden `200`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC03_DIAGNOSTICO_ACCESO - API sin token responde 401
- **Origen:** #13721 (Esc. 4)
- **Tipo / prioridad:** Error · Media
- **Precondición:** la pila está arriba.
- **Datos:** —
- **Pasos:**
  1. Ejecuta `curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:4032/api/v1/diagnostico/sesion`, sin cabecera Authorization.
- **Resultado esperado:** `401`. No se devuelve ningún dato de sesión.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC04_DIAGNOSTICO_ACCESO - Sesión de una sola compañía aunque se pida otra
- **Origen:** #13724 (Esc. 4)
- **Tipo / prioridad:** Borde · Alta
- **Precondición:** sesión de ADMIN en 4024.
- **Datos:** el id de EMPRESA_FLIT.
- **Pasos:**
  1. Abre `http://127.0.0.1:4024/api/v1/diagnostico/sesion?empresa=<id EMPRESA_FLIT>`.
- **Resultado esperado:**
  - El JSON trae `empresa` con el id de **EMPRESA_A**, no el de FLIT.
  - Incluye el rol de admin de Diagnóstico, sus permisos y sus capacidades, con `backoffice: false`.
  - No aparece ningún dato de la compañía FLIT.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC05_DIAGNOSTICO_ACCESO - Compañía con Diagnóstico apagado ve la página 403
- **Origen:** #13726 (Esc. 1) · #13732 (Esc. 4)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** se ejecuta al final del modo simulado. Trámites en 3000 con sesión de SUPER.
- **Datos:** ADMIN y EMPRESA_A.
- **Pasos:**
  1. En Trámites, en la pestaña «Productos» de EMPRESA_A, apaga «Diagnóstico».
  2. Confirma con **«Apagar Diagnóstico»** en el aviso «¿Apagar Diagnóstico para esta compañía?».
  3. En una ventana de incógnito nueva, abre http://127.0.0.1:4024 e inicia sesión con ADMIN.
- **Resultado esperado:**
  - Aparece la página **«No tienes acceso»** con «Diagnóstico no está habilitado para tu empresa.», o con «…o su suscripción está suspendida.», y el enlace «Ir a mis productos».
  - No se ve la pantalla base.
  - En local, una sesión que ya estaba abierta puede seguir funcionando porque el guard del backend está simulado como activo. Eso no es defecto.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC06_DIAGNOSTICO_ACCESO - Volver a encender Diagnóstico conserva los diagnósticos
- **Origen:** #13726 (Esc. 6)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** TC05. EMPRESA_A ya tenía lotes creados.
- **Datos:** ADMIN.
- **Pasos:**
  1. En «Productos», vuelve a encender «Diagnóstico» para EMPRESA_A.
  2. En incógnito, inicia sesión con ADMIN en 4024.
  3. Revisa el listado del inicio.
- **Resultado esperado:** entra a la pantalla base y el listado muestra los mismos lotes de antes (QA Carga 01, L-MOTOR, L-ERROR…), con sus estados.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC07_DIAGNOSTICO_ACCESO - Usuario con rol de Trámites sin rol de Diagnóstico ve la página 403
- **Origen:** #13726 (Esc. 4)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** TC00. Ventana de incógnito.
- **Datos:** RADICADOR.
- **Pasos:**
  1. Abre http://127.0.0.1:4024 e inicia sesión con RADICADOR.
- **Resultado esperado:**
  - Aparece **«No tienes acceso»** con «No tienes un rol en Diagnóstico. Pídele acceso a tu administrador.», si lo niega el hub.
  - O bien con «No tienes permisos en Diagnóstico. Pídele acceso a tu administrador.», si lo niega la API.
  - Cualquiera de las dos cuenta como Pasa. Nunca se ve la pantalla base.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC08_DIAGNOSTICO_ACCESO - Lote de otra compañía no se encuentra en el tablero
- **Origen:** #13722 (Esc. 1 y 2) · #13743 (AC6) · #13738 (Esc. 3)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** TC00 (Diagnóstico encendido en FLIT).
- **Datos:** SUPER y ADMIN; placa `TST191`.
- **Pasos:**
  1. Con SUPER en 4024, en «Diagnóstico Express», escribe `TST191`, pulsa «Diagnosticar», luego «Continuar» y, si aparece, «Lanzar diagnóstico».
  2. Copia la URL del tablero (`/diagnosticos/{id}`).
  3. En otra ventana de incógnito, con ADMIN, abre esa URL.
  4. Busca `TST191` en el listado de ADMIN.
- **Resultado esperado:**
  - ADMIN ve **«No encontramos este diagnóstico»** con «Puede que no exista o que no tengas acceso a él.» y «Volver al inicio».
  - El lote no aparece en el listado de ADMIN.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC09_DIAGNOSTICO_ACCESO - Detalle y descargas de otra compañía responden 404
- **Origen:** #13798 (AC5) · #13848 (AC7) · #13849 (AC6) · #13850 (AC6)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** TC08. Sesión de ADMIN.
- **Datos:** `<idB>` es el lote de SUPER del TC08 y `<vidB>` su vehículo, que se saca con H2 desde la sesión de SUPER.
- **Pasos (H1, con la sesión de ADMIN):**
  1. `api('/diagnosticos/<idB>/grilla')`
  2. `api('/diagnosticos/<idB>/excel')`
  3. `api('/diagnosticos/<idB>/vehiculos/<vidB>/pdf')`
  4. `api('/diagnosticos/<idB>/vehiculos/<vidB>/consultas/RUNT')`
  5. `api('/diagnosticos/<idB>/evidencias','POST',{tipo:'ZIP'})`
- **Resultado esperado:** los cinco responden `404` y el cuerpo no trae ningún dato del lote de FLIT: ni la placa, ni el nombre, ni las celdas.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC10_DIAGNOSTICO_ACCESO - Admin de compañía sin acceso al Backoffice
- **Origen:** #13802 (AC4) · #13799 (AC5) · #13800 (AC3) · #13737 (AC5) · #13728 (Esc. 2)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** se ejecuta al final del modo simulado. Existe L-ERROR. Sesión de ADMIN.
- **Datos:** el id de EMPRESA_A; `<cid>` es la celda SIMIT locatario de L-ERROR (H2).
- **Pasos:**
  1. Revisa el dock de la suite.
  2. Abre http://127.0.0.1:4024/backoffice/diagnosticos.
  3. Con H1: `api('/backoffice/diagnosticos?empresa=<id EMPRESA_A>')`.
  4. Con H1: `api('/backoffice/consultas/<cid>/intentos')`.
  5. Con H1: `api('/backoffice/consultas/<cid>/manual?empresa=<id EMPRESA_A>')`.
- **Resultado esperado:**
  - El dock no tiene la entrada «Backoffice».
  - La ruta lleva a **«No tienes acceso»** con «No tienes permiso para abrir esta página.».
  - Los tres llamados a la API responden `403` y la celda no cambia.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC11_DIAGNOSTICO_ACCESO - Backoffice sin compañía indicada responde 400
- **Origen:** #13728 (Esc. 4) · #13799 (AC3)
- **Tipo / prioridad:** Error · Media
- **Precondición:** sesión de SUPER en 4024.
- **Datos:** —
- **Pasos:**
  1. Abre el dock y elige «Backoffice».
  2. Sin escribir la compañía, observa la pantalla.
  3. Con H1: `api('/backoffice/diagnosticos')`.
  4. Corre S1.
- **Resultado esperado:**
  - La pantalla «Backoffice de diagnósticos» muestra **«Elige una compañía»** con «Indica la compañía cuyos lotes de diagnóstico quieres revisar.».
  - La API responde `400`.
  - S1 no registra ninguna lectura nueva por esa llamada.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC12_DIAGNOSTICO_ACCESO - Usuario de Backoffice FLIT no crea diagnósticos (condicional)
- **Origen:** #13750 (Esc. 9) · #13724 (Esc. 3)
- **Tipo / prioridad:** Error · Media
- **Precondición:** BACKOFFICE_D creado en TC00-E. Si no existe, el caso queda Bloqueado.
- **Datos:** BACKOFFICE_D.
- **Pasos:**
  1. Inicia sesión con BACKOFFICE_D en 4024 y revisa el inicio.
  2. Con H1: `api('/diagnosticos','POST',{nombre:'QA bo',modo:'SIMPLE',alcance:{por_vehiculo:['RUNT'],por_propietario:[],por_locatario:[]}})`.
- **Resultado esperado:**
  - El inicio no muestra el bloque «Diagnóstico Express».
  - El dock sí muestra «Backoffice».
  - El POST responde `403` y no se crea ningún lote.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.2 Configuración en dos niveles

#### QA_TC13_DIAGNOSTICO_CONFIGURACION - Compañía sin configuración muestra los valores por defecto
- **Origen:** #13741 (Esc. 1 y 2) · #13736 (Esc. 1)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** EMPRESA_A nunca guardó su configuración. Sesión de ADMIN.
- **Datos:** —
- **Pasos:**
  1. En el dock, abre «Configuración» y luego la pestaña «Contrato».
  2. Abre la pestaña «Operación».
  3. Con H1: `api('/configuracion')`.
- **Resultado esperado:**
  - «Contrato» muestra «Lo contratado por tu compañía», con Fuentes habilitadas = «RUNT vehículo» y «Límite de vehículos por lote» = 500.
  - «Operación» muestra la visibilidad «Todos los diagnósticos de la compañía», las fuentes por defecto «RUNT vehículo» y el estimado «No mostrar».
  - El JSON no trae `redaccion_ia_suspendida`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC14_DIAGNOSTICO_CONFIGURACION - Fuente no contratada se rechaza al crear el diagnóstico
- **Origen:** #13750 (Esc. 3)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** TC13. Se ejecuta antes de TC15, mientras el contrato solo tiene RUNT. Sesión de ADMIN.
- **Datos:** —
- **Pasos:**
  1. Con H1: `api('/diagnosticos','POST',{nombre:'QA no contratada',modo:'SIMPLE',alcance:{por_vehiculo:['RUNT','SIMIT_VEHICULO'],por_propietario:[],por_locatario:[]}})`.
- **Resultado esperado:** responde `422` y no se crea el lote, que no aparece en el listado.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC15_DIAGNOSTICO_CONFIGURACION - Backoffice FLIT edita el contrato y queda auditado
- **Origen:** #13741 (Esc. 8) · #13744 (Esc. 2)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** sesión de SUPER en 4024.
- **Datos:** el id de EMPRESA_A. Fuentes: RUNT vehículo, RUNT propietario, RUNT locatario, SIMIT vehículo, SIMIT propietario, SIMIT locatario y Avalúo comercial. Límite 200.
- **Pasos:**
  1. Abre «Configuración» → «Contrato».
  2. En «Contrato de una empresa (backoffice FLIT)», escribe el id en «Identificador de la empresa» y pulsa «Cargar contrato».
  3. En «Fuentes habilitadas», marca las 7 fuentes de los datos.
  4. En «Límite de vehículos por lote», escribe 200.
  5. Pulsa «Guardar contrato».
  6. Corre S2.
- **Resultado esperado:**
  - Aparece **«Contrato guardado.»**.
  - S2 muestra filas con el usuario de SUPER: `limite_vehiculos_lote` con valor anterior 500 y nuevo 200, y el cambio de `fuentes_habilitadas`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC16_DIAGNOSTICO_CONFIGURACION - Admin ve el contrato en solo lectura y sin campos ajenos
- **Origen:** #13744 (Esc. 1 y 5)
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** TC15. Sesión de ADMIN.
- **Datos:** —
- **Pasos:**
  1. Abre «Configuración» → «Contrato».
  2. Abre la pestaña «Operación».
- **Resultado esperado:**
  - «Contrato» muestra «Solo lectura: el contrato lo administra el backoffice FLIT.», las 7 fuentes y el límite 200, sin casillas ni campos editables.
  - No aparece el bloque «Contrato de una empresa (backoffice FLIT)».
  - Ninguna pestaña tiene campos de usuarios, roles, suscripción, retención ni purga.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC17_DIAGNOSTICO_CONFIGURACION - Límite de vehículos no positivo se rechaza
- **Origen:** #13741 (Esc. 9)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** sesión de SUPER con el contrato de EMPRESA_A cargado.
- **Datos:** límite 0.
- **Pasos:**
  1. En «Límite de vehículos por lote», escribe 0 y pulsa «Guardar contrato».
  2. Con H1: `api('/backoffice/empresas/<id EMPRESA_A>/configuracion/contrato','PUT',{limite_vehiculos_lote:0})`.
  3. Corre S2.
- **Resultado esperado:**
  - La pantalla muestra «El límite debe ser un número entero mayor que cero.» junto al campo.
  - La API responde `422`.
  - S2 no registra ningún cambio nuevo y el límite sigue en 200.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC18_DIAGNOSTICO_CONFIGURACION - Admin guarda la operación de la compañía
- **Origen:** #13741 (Esc. 3) · #13744 (Esc. 3)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** TC15. Sesión de ADMIN.
- **Datos:** fuentes por defecto = RUNT vehículo y SIMIT vehículo; estimado = «Antes de lanzar el diagnóstico».
- **Pasos:**
  1. Abre «Configuración» → «Operación».
  2. En «Fuentes por defecto», revisa que solo se ofrezcan las fuentes habilitadas. Marca RUNT vehículo y SIMIT vehículo.
  3. En «Estimado de consultas», elige «Antes de lanzar el diagnóstico».
  4. Pulsa «Guardar operación».
  5. Recarga la página.
- **Resultado esperado:**
  - Aparece **«Configuración guardada.»**.
  - Tras recargar se conservan los valores.
  - «Impuesto departamental» no aparece entre las fuentes por defecto.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC19_DIAGNOSTICO_CONFIGURACION - Valores inválidos en la operación responden 422
- **Origen:** #13741 (Esc. 4 y 6)
- **Tipo / prioridad:** Error · Media
- **Precondición:** TC18. Sesión de ADMIN.
- **Datos:** —
- **Pasos:**
  1. Con H1: `api('/configuracion','PUT',{estimado_consultas:'SIEMPRE'})`.
  2. Con H1: `api('/configuracion','PUT',{fuentes_por_defecto:['RUNT','IMPUESTO_DEPARTAMENTAL']})`.
  3. Con H1: `api('/configuracion')`.
- **Resultado esperado:**
  - El paso 1 responde `422` con `VALOR_FUERA_DE_CONJUNTO`.
  - El paso 2 responde `422` con `POR_DEFECTO_FUERA_DE_HABILITADAS`.
  - El paso 3 muestra la configuración del TC18, sin cambios.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC20_DIAGNOSTICO_CONFIGURACION - Campo contractual en la edición operativa responde 403
- **Origen:** #13741 (Esc. 5)
- **Tipo / prioridad:** Error · Media
- **Precondición:** TC18. Sesión de ADMIN.
- **Datos:** —
- **Pasos:**
  1. Con H1: `api('/configuracion','PUT',{limite_vehiculos_lote:10, estimado_consultas:'NO_MOSTRAR'})`.
  2. Con H1: `api('/configuracion')`.
- **Resultado esperado:**
  - El paso 1 responde `403` con `CAMPO_NO_EDITABLE`.
  - En el paso 2 el estimado sigue en `ANTES_DE_LANZAR` y el límite en 200: no se guardó ningún campo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC21_DIAGNOSTICO_CONFIGURACION - Reducir el contrato recorta las fuentes por defecto
- **Origen:** #13741 (Esc. 7) · #13744
- **Tipo / prioridad:** Borde · Media
- **Precondición:** ADMIN agregó «Avalúo comercial» a las fuentes por defecto y guardó.
- **Datos:** SUPER y ADMIN.
- **Pasos:**
  1. Con SUPER, carga el contrato de EMPRESA_A, desmarca «Avalúo comercial» y pulsa «Guardar contrato».
  2. Con ADMIN, abre «Configuración» → «Operación».
  3. Con SUPER, vuelve a marcar «Avalúo comercial» y guarda, para restaurar el contrato.
- **Resultado esperado:**
  - En el paso 1 aparece «Contrato guardado. Se quitaron de las fuentes por defecto: Avalúo comercial.».
  - En el paso 2 aparece el aviso **«El contrato cambió»** y Avalúo ya no está entre las fuentes por defecto.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC22_DIAGNOSTICO_CONFIGURACION - Usuario de Diagnóstico ve la operación en solo lectura y sin Reprocesar (condicional)
- **Origen:** #13744 (Esc. 4) · #13741 (Esc. 11) · #13748 (AC4) · #13746 (AC2)
- **Tipo / prioridad:** Error · Media
- **Precondición:** USUARIO_D creado. Existe un lote de EMPRESA_A con una celda roja (L-MOTOR) y la visibilidad está en «Todos los diagnósticos de la compañía».
- **Datos:** USUARIO_D; `<cid>` es la celda RUNT de `TST147` en L-MOTOR.
- **Pasos:**
  1. Abre «Configuración» → «Operación».
  2. Con H1: `api('/configuracion','PUT',{estimado_consultas:'NO_MOSTRAR'})`.
  3. Abre el tablero de L-MOTOR y mira la celda roja de RUNT de TST147.
  4. Con H1: `api('/consultas/<cid>/reprocesar','POST')`.
- **Resultado esperado:**
  - Aparece el aviso **«Solo lectura»** con «No tienes permiso para editar la operación. Pídele el cambio al administrador de tu compañía.». No se puede guardar.
  - El PUT responde `403`.
  - Ninguna celda roja tiene el botón «Reprocesar».
  - El POST responde `403` y la celda sigue en Error.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.3 Parametrización (alcance, modo y estimado)

> En la interfaz no hay ningún botón ni enlace hacia `/diagnosticos/nuevo`: se entra escribiendo la URL. Ver la observación de §5.

#### QA_TC23_DIAGNOSTICO_PARAMETRIZACION - Formulario muestra los grupos, el RUNT bloqueado y solo lo contratado
- **Origen:** #13752 (Esc. 2 y 3)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** TC15 y TC18. Sesión de ADMIN.
- **Datos:** —
- **Pasos:**
  1. Abre http://127.0.0.1:4024/diagnosticos/nuevo.
  2. Revisa la sección «Alcance».
  3. Pasa el cursor sobre «RUNT vehículo».
- **Resultado esperado:**
  - Se ven «Nuevo diagnóstico», «Información general» y «Alcance» con el texto «Elige qué fuentes consultar por cada vehículo. Solo ves las que tu compañía tiene contratadas.».
  - Aparecen los grupos «Por Vehículo», «Por Propietario» y «Por Locatario».
  - «RUNT vehículo» está marcado y bloqueado, con el tooltip «El RUNT vehículo siempre se consulta: es la base del diagnóstico.».
  - Las fuentes de persona llevan la insignia «Dependiente».
  - Vienen premarcadas RUNT vehículo y SIMIT vehículo.
  - «Impuesto departamental» no aparece.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC24_DIAGNOSTICO_PARAMETRIZACION - Nombre vacío o de 151 caracteres no se envía
- **Origen:** #13752 (Esc. 1) · #13750 (Esc. 1)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** formulario de `/diagnosticos/nuevo` abierto. Pestaña Network de las DevTools abierta.
- **Datos:** un nombre de 151 caracteres, por ejemplo 151 letras «a».
- **Pasos:**
  1. Deja vacío «Nombre del diagnóstico» y pulsa «Crear diagnóstico».
  2. Escribe el nombre de 151 caracteres y pulsa «Crear diagnóstico».
- **Resultado esperado:**
  - El paso 1 muestra «Escribe el nombre del diagnóstico.».
  - El paso 2 muestra «El nombre admite hasta 150 caracteres.».
  - En ninguno sale un POST `/diagnosticos`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC25_DIAGNOSTICO_PARAMETRIZACION - Con Análisis IA exige elegir el tipo de trámite
- **Origen:** #13752 (Esc. 4) · #13742 (Esc. 1 y 2) · #13731 (Esc. 1) · #13750 (Esc. 5)
- **Tipo / prioridad:** Error · Media
- **Precondición:** formulario abierto, con nombre válido.
- **Datos:** —
- **Pasos:**
  1. En «Modo», elige «Con Análisis IA».
  2. Abre el combo «Tipo de trámite».
  3. Sin elegir ningún tipo, pulsa «Crear diagnóstico».
  4. Con H1: `api('/tipos-tramite')`.
- **Resultado esperado:**
  - El combo muestra «Elige un tipo de trámite» y solo los tipos de la etapa: traspaso, traspaso unilateral y transferencia de dominio, con los nombres que publique Trámites en local.
  - Al guardar aparece «Elige el tipo de trámite.».
  - El GET responde `200` y la lista `fuentesObligatorias` de cada tipo viene vacía.
  - Si el catálogo no responde, aparece «No se pudieron cargar los tipos de trámite» con «Reintentar». Anótalo como observación.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC26_DIAGNOSTICO_PARAMETRIZACION - Diagnóstico Simple se crea en estado Creado
- **Origen:** #13750 (Esc. 7) · #13738 (Esc. 1)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** formulario abierto.
- **Datos:** nombre `QA Carga 01`; modo Simple; alcance por defecto (RUNT vehículo y SIMIT vehículo).
- **Pasos:**
  1. Escribe el nombre, deja el modo «Simple» y pulsa «Crear diagnóstico».
  2. Vuelve al inicio y busca el lote en el listado.
- **Resultado esperado:**
  - Aparece «Diagnóstico creado. Puedes ajustar la cabecera mientras siga en estado Creado.» y el botón **«Cargar vehículos»**.
  - En el listado, el lote lleva el chip «Sin lanzar».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC27_DIAGNOSTICO_PARAMETRIZACION - Alcance sin RUNT o con fuente en grupo ajeno responde 422
- **Origen:** #13750 (Esc. 2 y 4)
- **Tipo / prioridad:** Error · Media
- **Precondición:** sesión de ADMIN.
- **Datos:** —
- **Pasos:**
  1. Con H1: `api('/diagnosticos','POST',{nombre:'QA sin runt',modo:'SIMPLE',alcance:{por_vehiculo:['SIMIT_VEHICULO'],por_propietario:[],por_locatario:[]}})`.
  2. Con H1: `api('/diagnosticos','POST',{nombre:'QA grupo ajeno',modo:'SIMPLE',alcance:{por_vehiculo:['RUNT','RUNT_LOCATARIO'],por_propietario:[],por_locatario:[]}})`.
- **Resultado esperado:** los dos responden `422` y ninguno aparece en el listado.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC28_DIAGNOSTICO_PARAMETRIZACION - Editar un diagnóstico ya lanzado responde 409
- **Origen:** #13750 (Esc. 8)
- **Tipo / prioridad:** Error · Media
- **Precondición:** QA Carga 01 en «En proceso» (TC42).
- **Datos:** `<id>` de QA Carga 01.
- **Pasos:**
  1. Con H1: `api('/diagnosticos/<id>','PUT',{nombre:'QA editado',modo:'SIMPLE',alcance:{por_vehiculo:['RUNT'],por_propietario:[],por_locatario:[]}})`.
- **Resultado esperado:** responde `409` y el nombre del lote no cambia en el tablero.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.4 Captura express

#### QA_TC29_DIAGNOSTICO_EXPRESS - Express de punta a punta con nombre autogenerado
- **Origen:** #13755 (Esc. 1 y 2) · #13753 (Esc. 1 y 2) · #13752 (Esc. 6)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** TC18 (estimado «Antes de lanzar»). Sesión de ADMIN en el inicio.
- **Datos:** escribe `tst 111`.
- **Pasos:**
  1. En «Diagnóstico Express», escribe `tst 111` en «Placa o VIN».
  2. Pulsa «Diagnosticar».
  3. Revisa el nombre del panel.
  4. Pulsa «Continuar».
  5. En «Estimado de consultas», pulsa «Lanzar diagnóstico».
- **Resultado esperado:**
  - El campo muestra `TST111` mientras escribes.
  - El panel trae el nombre editable **«Express TST111 26/10/2026»**, con la fecha del día en hora Colombia.
  - Aparece «Creando el diagnóstico…» y luego navega a `/diagnosticos/{id}` con el tablero de un solo vehículo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC30_DIAGNOSTICO_EXPRESS - Identificador inválido no crea el diagnóstico
- **Origen:** #13755 (Esc. 3) · #13753 (Esc. 3)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** sesión de ADMIN en el inicio.
- **Datos:** `ABC-1`
- **Pasos:**
  1. Escribe `ABC-1` en «Placa o VIN». El campo muestra `ABC1`.
  2. Pulsa «Diagnosticar».
- **Resultado esperado:** debajo del campo aparece «No es una placa ni un VIN válido. Revisa el identificador.», no se abre el panel y no se crea ningún lote.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC31_DIAGNOSTICO_EXPRESS - Nombre vacío impide continuar
- **Origen:** #13755 (Esc. 4)
- **Tipo / prioridad:** Error · Media
- **Precondición:** panel express abierto con `TST112`.
- **Datos:** —
- **Pasos:**
  1. Borra «Nombre del diagnóstico».
  2. Pulsa «Continuar».
- **Resultado esperado:** aparece «Escribe el nombre del diagnóstico.», el foco vuelve al campo y no se lanza nada.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC32_DIAGNOSTICO_EXPRESS - Documento del locatario es opcional y se enmascara
- **Origen:** #13755 (Esc. 5) · #13753 (Esc. 5)
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** panel express abierto con `TST112`.
- **Datos:** CC `9999900016`.
- **Pasos:**
  1. Revisa la sección «Locatario».
  2. En «Tipo de documento del locatario», elige CC.
  3. En «Número de documento del locatario (opcional)», escribe `9999900016` y sal del campo.
- **Resultado esperado:**
  - La sección dice «Declarado por el cliente» y «Opcional. Se guarda cifrado y en pantalla solo se ven los 4 últimos caracteres.».
  - Al salir del campo se ve **`****0016`**.
  - Sin documento también se puede continuar.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC33_DIAGNOSTICO_EXPRESS - Documento del locatario sin tipo se rechaza
- **Origen:** #13755 (Esc. 5, negativo) · #13753
- **Tipo / prioridad:** Error · Baja
- **Precondición:** panel express abierto con `TST112`.
- **Datos:** documento `9999900016` con el tipo «Sin documento».
- **Pasos:**
  1. Deja «Sin documento» en el tipo y escribe el número.
  2. Pulsa «Continuar».
- **Resultado esperado:** aparece «Elige el tipo de documento del locatario.» y no se lanza nada.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC34_DIAGNOSTICO_EXPRESS - Estimado antes de lanzar con «al menos» en personas
- **Origen:** #13755 (Esc. 6) · #13753 (Esc. 4) · #13797 (AC1 y AC2) · #13796 (AC1) · #13747
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** TC18. Panel express abierto con `TST113`.
- **Datos:** alcance RUNT vehículo, SIMIT vehículo, RUNT propietario y SIMIT propietario.
- **Pasos:**
  1. Marca las fuentes del alcance y pulsa «Continuar».
  2. Revisa «Estimado de consultas».
  3. Pulsa «Anterior».
  4. Revisa el listado.
- **Resultado esperado:**
  - El estimado dice «Consultas previstas por fuente para TST113.».
  - RUNT vehículo y SIMIT vehículo muestran `1` sin «al menos».
  - RUNT propietario y SIMIT propietario muestran **«al menos 1»**.
  - «Anterior» vuelve a la edición.
  - No se creó ningún lote `Express TST113`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC35_DIAGNOSTICO_EXPRESS - Tipos de identificador detectados y rechazados
- **Origen:** #13740 (Esc. 1 a 3) · #13745 (Esc. 1) · #13735 (Esc. 3)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** sesión de ADMIN en el inicio. No hace falta lanzar nada.
- **Datos:** `ABC12D`, `R12345`, `MC029554`, `9ZZPRUEBA00000301`, `ABCDE1234` y `9ZZPRUEBAO0000001`.
- **Pasos:**
  1. Por cada valor, escríbelo en «Placa o VIN» y pulsa «Diagnosticar».
  2. Cierra el panel editando el campo antes de pasar al siguiente.
- **Resultado esperado:**
  - Con los cuatro primeros se abre el panel: moto, remolque, maquinaria y VIN.
  - `ABCDE1234` (9 caracteres) y el VIN con la letra O muestran un error en el campo y no abren el panel.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.5 Carga masiva (.xlsx y .csv) y confirmación del lote

#### QA_TC36_DIAGNOSTICO_CARGA - Plantilla .xlsx con las tres columnas
- **Origen:** #13749 (Esc. 1)
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** TC26. En el inicio, haz clic en «QA Carga 01» (chip «Sin lanzar»), que abre `/diagnosticos/{id}/carga`.
- **Datos:** —
- **Pasos:**
  1. En «Cargar vehículos», pulsa «Descargar plantilla».
  2. Abre el archivo en Excel.
- **Resultado esperado:** se descarga un `.xlsx` cuya primera fila tiene `Identificador_Vehiculo`, `Tipo_Documento_Locatario` y `Documento_Locatario`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC37_DIAGNOSTICO_CARGA - Archivo .xlsx con válidos, erróneos y duplicados
- **Origen:** #13749 (Esc. 5 y 6) · #13754 (Esc. 3)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** TC36. Pantalla de carga de QA Carga 01.
- **Datos:** F1x.
- **Pasos:**
  1. Haz clic en «Archivo de vehículos (.xlsx o .csv)», que dice «Máximo 5 MB», y elige F1x.
  2. Espera a que termine «Analizando el archivo…».
- **Resultado esperado:**
  - Aparece **«Resumen de validación»** con «6 filas leídas · límite por lote: 200 vehículos».
  - Los conteos son «Válidos» 4, «Erróneos» 1 y «Duplicados» 1.
  - Aparecen «Corregir ahora» y «Continuar solo con los válidos».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC38_DIAGNOSTICO_CARGA - El mismo contenido en .csv da el mismo resumen
- **Origen:** #13749 (Esc. 2)
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** TC37.
- **Datos:** F1.
- **Pasos:**
  1. Pulsa «Subir otro archivo» y elige F1.
- **Resultado esperado:** el resumen es idéntico al del TC37: 6 filas, 4 válidos, 1 erróneo y 1 duplicado.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC39_DIAGNOSTICO_CARGA - Formatos .xls, .pdf y falso .csv se rechazan
- **Origen:** #13749 (Esc. 3 y 4) · #13754 (Esc. 1 y 2)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** pantalla de carga de QA Carga 01.
- **Datos:** F2, F3 y F3c.
- **Pasos:**
  1. Sube F2 (`.xls`).
  2. Sube F3 (`.pdf`).
  3. Sube F3c (un PDF renombrado a `.csv`).
- **Resultado esperado:**
  - **F2:** la API responde 422 y la pantalla muestra «El formato .xls no se admite: guarde el archivo como .xlsx y vuelva a subirlo».
  - **F3:** la pantalla muestra «Formato no admitido: sube el archivo en .xlsx o .csv (puedes partir de la plantilla).» sin llamar a la API.
  - **F3c:** la API responde 415 con «Formato de archivo no admitido: suba la plantilla en .xlsx o un .csv en UTF-8 separado por coma o punto y coma.».
  - En ningún caso se llega al resumen.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC40_DIAGNOSTICO_CARGA - Archivo mayor a 5 MB se rechaza
- **Origen:** #13749 (tope `Diagnostico:Carga:TamanoMaximoBytes`) · README de core-diagnostico
- **Tipo / prioridad:** Borde · Alta
- **Precondición:** pantalla de carga de QA Carga 01.
- **Datos:** F4 (≈ 6,3 MB).
- **Pasos:**
  1. Sube F4.
- **Resultado esperado:** la pantalla muestra, sin resumen, «El archivo pesa más de 5120 KB: divídalo en varios archivos y vuelva a subirlos.», que viene de un 413 de la API. Si el gateway o el proxy cortan antes con otro mensaje, anótalo en Observaciones.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC41_DIAGNOSTICO_CARGA - Corregir ahora revalida la fila al vuelo
- **Origen:** #13754 (Esc. 4) · #13751 (Esc. 1)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** resumen de F1 a la vista (TC38).
- **Datos:** cambia `ABC-12X` por `TST125`.
- **Pasos:**
  1. Pulsa «Corregir ahora».
  2. En «Filas con errores», cambia el identificador de la fila errónea por `TST125` y deja de escribir.
- **Resultado esperado:**
  - El estado de la fila pasa por «Revalidando…» y queda en **«Válida»**.
  - El resumen queda con «Válidos» 5 y «Erróneos» 0.
  - No se crea ningún vehículo, porque la revalidación no persiste.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC42_DIAGNOSTICO_CARGA - Continuar solo con los válidos lanza el lote
- **Origen:** #13754 (Esc. 5) · #13751 (Esc. 2 y 7) · #13727 (AC1)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** TC41.
- **Datos:** QA Carga 01.
- **Pasos:**
  1. Pulsa «Continuar solo con los válidos».
  2. Observa el tablero.
  3. Corre S4 con el id del lote.
- **Resultado esperado:**
  - Navega a `/diagnosticos/{id}` con 5 filas (TST121 a TST125).
  - El estado del lote es «En proceso».
  - S4 muestra `CREADO → EN_PROCESO`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC43_DIAGNOSTICO_CARGA - Archivo sin válidos no deja continuar
- **Origen:** #13749 (Esc. 7) · #13754 (Esc. 6) · #13751 (Esc. 4)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** en `/diagnosticos/nuevo`, crea «QA Carga 02» (Simple, alcance por defecto) y pulsa «Cargar vehículos».
- **Datos:** F6.
- **Pasos:**
  1. Sube F6.
- **Resultado esperado:**
  - «Válidos» 0.
  - Aparece el motivo «El archivo no tiene ningún registro válido: corrija los errores o suba otro archivo para continuar.».
  - «Continuar solo con los válidos» queda **deshabilitado**.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC44_DIAGNOSTICO_CARGA - Archivo sobre el límite del lote no deja continuar
- **Origen:** #13749 (Esc. 8) · #13754 (Esc. 6) · #13751 (Esc. 5)
- **Tipo / prioridad:** Borde · Alta
- **Precondición:** carga de QA Carga 02. Límite en 200 (TC15).
- **Datos:** F5 (201 válidos) y F5b (200 válidos).
- **Pasos:**
  1. Sube F5.
  2. Pulsa «Subir otro archivo» y sube F5b. No confirmes.
- **Resultado esperado:**
  - **F5:** «El archivo tiene 201 vehículos válidos y el límite por lote es 200: divida el archivo o retire vehículos para continuar.» y el botón deshabilitado.
  - **F5b:** sin motivo de bloqueo y el botón habilitado.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC45_DIAGNOSTICO_CARGA - Documento del locatario con letras deja la fila errónea
- **Origen:** #13749 (Esc. 9) · #13754 (Esc. 8)
- **Tipo / prioridad:** Error · Media
- **Precondición:** carga de QA Carga 02.
- **Datos:** F7.
- **Pasos:**
  1. Sube F7.
  2. Pulsa «Corregir ahora».
- **Resultado esperado:**
  - Las dos filas quedan erróneas: `TST126` por el documento `99999ABCD` y `ABC-99` por el identificador.
  - En la fila de `ABC-99`, el documento se ve **`****0129`** con el rótulo «Declarado por el cliente», nunca completo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC46_DIAGNOSTICO_CARGA - Estimado aparece en el resumen con «Antes de lanzar»
- **Origen:** #13749 (Esc. 10) · #13754 (Esc. 7) · #13747 (Esc. 1)
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** estimado en «Antes de lanzar el diagnóstico» (TC18). Carga de QA Carga 02.
- **Datos:** F1.
- **Pasos:**
  1. Sube F1.
- **Resultado esperado:** el resumen trae **«Estimado de consultas»** con «Consultas previstas por fuente según el último análisis del archivo.», y RUNT vehículo y SIMIT vehículo con `4` cada una.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC47_DIAGNOSTICO_CARGA - Sin estimado en el resumen con «No mostrar»
- **Origen:** #13749 (Esc. 10) · #13754 (Esc. 7)
- **Tipo / prioridad:** Borde · Baja
- **Precondición:** con ADMIN, en «Configuración» → «Operación», cambia el estimado a «No mostrar» y pulsa «Guardar operación».
- **Datos:** F1.
- **Pasos:**
  1. En la carga de QA Carga 02, sube F1.
- **Resultado esperado:** el resumen **no** trae «Estimado de consultas». Deja el estimado en «No mostrar» para L-MOTOR.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.6 Motor y cola: los 8 escenarios del RUNT (también F10 por la ruta simulada)

> Los fixtures simulados pasan por el mismo `MapperIntempo` que la fuente real (#13877, AC2).

#### QA_TC48_DIAGNOSTICO_MOTOR - Lote de ocho escenarios queda En proceso con celdas Pendiente
- **Origen:** #13727 (AC1) · #13743 (AC2) · #13748 (AC1)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** TC15 y TC47. Sesión de ADMIN.
- **Datos:** nombre «QA Motor 8 escenarios»; modo Simple; las 7 fuentes ofrecidas; F8.
- **Pasos:**
  1. En `/diagnosticos/nuevo`, escribe el nombre, marca todas las fuentes ofrecidas y pulsa «Crear diagnóstico».
  2. Pulsa «Cargar vehículos» y sube F8.
  3. Pulsa «Continuar solo con los válidos».
  4. Observa el tablero en los primeros segundos.
- **Resultado esperado:**
  - La tabla «Semáforo de consultas del lote» tiene 9 filas y las columnas agrupadas «Por vehículo», «Por propietario» y «Por locatario», más «Reporte».
  - Al inicio las celdas están en «Pendiente» o «En curso», en gris.
  - El estado del lote es «En proceso», con la barra «Vehículos terminados».
  - Se ve «Se actualiza sola cada 3 segundos.».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC49_DIAGNOSTICO_MOTOR - Escenario 1 persona natural
- **Origen:** #13725 · #13730 (AC1) · #13877 (AC2) · #13792 · #13790
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST141`.
- **Pasos:**
  1. Haz clic en el semáforo «RUNT vehículo» de TST141.
  2. Abre el detalle de «RUNT propietario».
  3. Abre el detalle de «SIMIT vehículo».
- **Resultado esperado:**
  - **RUNT:** «Completada», con Placa TST141, Servicio PARTICULAR, «Organismo de tránsito» SECRETARIA DE TRANSITO DE PRUEBA, «Propietarios» con «Documento CC · Propiedad …» y «Locatarios» = «Sin locatarios».
  - **RUNT propietario:** «Consultado en el RUNT», documento `****0001`, «Inscrito en el RUNT», licencia B1 ACTIVA.
  - **SIMIT vehículo:** «Semáforo SIMIT: Verde» y «Sin comparendos registrados.».
  - La fila lleva la marca «Terminado».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC50_DIAGNOSTICO_MOTOR - Escenario 2 propietario NIT consulta el RUES
- **Origen:** #13792 (AC1 y AC5) · #13793 (AC1) · #13794 (AC2)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST142`.
- **Pasos:**
  1. Abre el detalle de «RUNT propietario» de TST142.
  2. Abre el detalle de «SIMIT propietario».
- **Resultado esperado:**
  - **RUNT propietario:** «Consultado en el RUES (persona jurídica)», documento NIT `****0002`, «Matrícula mercantil» = «Encontrada» y «Estado de la matrícula» = ACTIVA. No aparece ninguna razón social ni nombre.
  - **SIMIT propietario:** semáforo Rojo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC51_DIAGNOSTICO_MOTOR - Escenario 3 leasing con locatario cédula
- **Origen:** #13730 (AC3) · #13790 (AC2) · #13794 (AC1)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST143`.
- **Pasos:**
  1. Abre el detalle de «RUNT vehículo».
  2. Abre el detalle de «RUNT locatario».
  3. Abre los detalles de «SIMIT locatario» y «SIMIT vehículo».
- **Resultado esperado:**
  - **RUNT vehículo:** «Locatarios» = «1 locatario».
  - **RUNT locatario:** sección «Locatario», documento CC `****0013` **sin** «Declarado por el cliente», «Consultado en el RUNT» y licencia B1 SUSPENDIDA.
  - **SIMIT locatario y SIMIT vehículo:** semáforo Amarillo, con el comparendo «ACUERDO DE PAGO» en SIMIT vehículo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC52_DIAGNOSTICO_MOTOR - Escenario 4 leasing con locatario NIT
- **Origen:** #13725 (AC1 y AC4) · #13792
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST144`.
- **Pasos:**
  1. Abre el detalle de «RUNT locatario» de TST144.
  2. Abre el detalle de «SIMIT locatario».
- **Resultado esperado:**
  - **RUNT locatario:** «Consultado en el RUES (persona jurídica)» con NIT `****0014`.
  - **SIMIT locatario:** Verde.
  - Las celdas en «Completada».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC53_DIAGNOSTICO_MOTOR - Escenario 5 servicio público
- **Origen:** #13725 · #13733
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST145`.
- **Pasos:**
  1. Abre el detalle de «RUNT vehículo» de TST145.
  2. Revisa el resto de la fila.
- **Resultado esperado:**
  - «Servicio» = PUBLICO.
  - Las fuentes del alcance se consultan normal, porque el impuesto no está en el alcance (ver TC63).
  - El propietario NIT sale por el RUES.
  - La fila queda «Terminado».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC54_DIAGNOSTICO_MOTOR - Escenario 6 organismo sin departamento y SIMIT con 60 comparendos
- **Origen:** #13725 · #13861 (AC5) · #13794 (AC2)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST146`.
- **Pasos:**
  1. Abre el detalle de «RUNT vehículo».
  2. Abre el detalle de «SIMIT vehículo».
  3. Pulsa «Ver los 60 comparendos».
- **Resultado esperado:**
  - **RUNT:** «Completada», con el organismo «SECRETARIA DE TRANSITO DE PRUEBA SIN DEPARTAMENTO». El vehículo no queda en error por eso.
  - **SIMIT vehículo:** Rojo, con «Mostrando N de 60» y el botón, que al pulsarlo despliega todos y cambia a «Ver menos».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC55_DIAGNOSTICO_MOTOR - Escenario 7 Timeout se reintenta y queda en error
- **Origen:** #13737 (AC2) · #13730 (AC5) · #13877 (AC2)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** L-MOTOR. Se observa justo después de lanzarlo.
- **Datos:** `TST147`.
- **Pasos:**
  1. Observa la celda RUNT de TST147 durante unos 15 segundos.
  2. Abre su detalle.
  3. Abre el detalle de otra celda de TST147, por ejemplo «SIMIT vehículo».
  4. Corre S3 con `TST147`.
- **Resultado esperado:**
  - **RUNT:** sigue en gris unos 5 segundos (reintento con espera) y termina en **«Error»** con «No se pudo consultar». El detalle dice «La consulta terminó en error» y «La fuente no respondió a tiempo.».
  - **Las demás celdas:** «Error» con «Espera al RUNT del vehículo». Su detalle dice «Depende del RUNT del vehículo, que también falló. Se resuelve al reprocesar el RUNT.».
  - **S3:** 2 intentos de RUNT con TIMEOUT (MaxReintentos = 1) y ningún intento en las demás fuentes.
  - **TST148**, la fila siguiente, se procesa normal (#13730 AC5).
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC56_DIAGNOSTICO_MOTOR - Escenario 8 respuesta inválida queda en error sin reintento
- **Origen:** #13737 (AC3) · #13877 (AC2)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST148`.
- **Pasos:**
  1. Abre el detalle de «RUNT vehículo» de TST148.
  2. Corre S3 con `TST148`.
- **Resultado esperado:**
  - «Error», con «La fuente respondió con datos que no se pudieron leer.».
  - S3 muestra **un solo** intento con RESPUESTA_INVALIDA.
  - Las demás celdas quedan en «Espera al RUNT del vehículo».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC57_DIAGNOSTICO_MOTOR - VIN consulta el RUNT y guarda la placa de la fuente
- **Origen:** #13730 (AC2) · #13740
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** L-MOTOR procesado.
- **Datos:** `9ZZPRUEBA00000301`.
- **Pasos:**
  1. Ubica la fila del VIN y abre el detalle de «RUNT vehículo».
- **Resultado esperado:**
  - RUNT «Completada», con la **Placa TST001** devuelta por la fuente y la persona natural.
  - SIMIT vehículo se consultó con esa placa: Verde.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC58_DIAGNOSTICO_MOTOR - Lote sin errores pasa solo a Completado
- **Origen:** #13743 (AC3) · #13748 (AC3)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** sesión de ADMIN.
- **Datos:** express `TST151` con las 7 fuentes y sin locatario.
- **Pasos:**
  1. Lanza el express con TST151, marcando todas las fuentes del panel.
  2. Espera sin tocar nada.
  3. Corre S4 con el id del lote.
- **Resultado esperado:**
  - Las celdas quedan en «Completada» o «No aplica» (las de locatario).
  - La fila queda «Terminado» y el estado del lote pasa solo a **«Completado»**.
  - Deja de verse «Se actualiza sola cada 3 segundos.».
  - S4 muestra `EN_PROCESO → COMPLETADO` con disparador AUTO.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC59_DIAGNOSTICO_MOTOR - Intentos registrados con su disparador
- **Origen:** #13737 (AC1)
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** TC58.
- **Datos:** `TST151`.
- **Pasos:**
  1. Corre S3 con `TST151`.
- **Resultado esperado:**
  - Hay un intento por cada fuente consultada, con el servicio de la simulación, `disparador = AUTO`, estado de éxito, inicio y fin con milisegundos, y `desde_cache = false`.
  - No hay intentos de RUNT locatario ni de SIMIT locatario.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.7 Reutilización del mismo día

#### QA_TC60_DIAGNOSTICO_REUTILIZACION - Misma placa el mismo día reutiliza el dato
- **Origen:** #13739 (AC1) · #13748 (AC6) · #13743 (AC5) · #13790 (AC3)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-MOTOR procesado hoy, con TST141 consultado. Sesión de ADMIN.
- **Datos:** express `TST141` con las mismas fuentes que L-MOTOR.
- **Pasos:**
  1. Lanza el express con TST141.
  2. Observa las celdas «Completada».
  3. Abre el detalle de «RUNT propietario».
  4. Corre S3 con `TST141`.
- **Resultado esperado:**
  - Las celdas muestran **«Dato del …»**, con la fecha y hora de la consulta original.
  - El detalle dice «Reutilizado de una consulta del mismo día» y conserva el documento `****0001`.
  - No hay ningún indicador de origen ni de dato manual.
  - S3 tiene intentos nuevos con `desde_cache = true`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC61_DIAGNOSTICO_REUTILIZACION - Otra compañía no reutiliza el dato
- **Origen:** #13739 (AC3) · #13793 (AC5)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** TC60. Diagnóstico encendido en FLIT.
- **Datos:** SUPER; express `TST141`.
- **Pasos:**
  1. Con SUPER, lanza un express con TST141.
  2. Revisa la celda RUNT.
  3. Corre S3 con `TST141` y fíjate en las filas más recientes.
- **Resultado esperado:**
  - La celda RUNT queda «Completada» **sin** la nota «Dato del …».
  - El intento de la compañía FLIT tiene `desde_cache = false`. Consultó por su cuenta.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.8 N/A por locatario, servicio público y departamento

#### QA_TC62_DIAGNOSTICO_NOAPLICA - Locatario sin documento queda en No aplica sin consumo
- **Origen:** #13733 (AC3) · #13794 (AC4) · #13723
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST141`.
- **Pasos:**
  1. Mira las celdas «RUNT locatario» y «SIMIT locatario» de TST141.
  2. Abre su detalle.
  3. Corre S3 con `TST141`.
- **Resultado esperado:**
  - Las dos celdas en «No aplica» (gris claro, borde punteado), con el motivo «Sin documento del locatario».
  - El detalle dice «El vehículo está sin el documento del locatario: no hay a quién consultar.».
  - S3 no tiene intentos de esas dos fuentes.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC63_DIAGNOSTICO_NOAPLICA - Impuesto de vehículo de servicio público en No aplica (condicional)
- **Origen:** #13733 (AC1) · #13861 (AC6) · #13866 (AC2)
- **Tipo / prioridad:** Borde · Baja
- **Precondición:**
  - **No es posible con la pila actual:** la bandera del impuesto está en false.
  - Para ejecutarlo, para core-diagnostico, agrega `Diagnostico__Fuentes__ImpuestoDisponible=true` a su línea en el script local y relanza con `--sin-build`.
  - Con SUPER, habilita «Impuesto departamental» en el contrato de EMPRESA_A. La capacidad simulada declara los departamentos 05 y 25.
  - Al terminar, revierte la bandera.
- **Datos:** express `TST205` con el impuesto en el alcance.
- **Pasos:**
  1. Lanza el express.
  2. Mira la celda «Impuesto departamental» y abre su detalle.
  3. Corre S3 con `TST205`.
- **Resultado esperado:**
  - Chip **«No aplica»** con «Servicio público».
  - El detalle dice «El vehículo es de servicio público: esta consulta no aplica.».
  - No hay botón «Reprocesar».
  - S3 no tiene intentos del impuesto.
  - Si la pila no se puede preparar, anota «Bloqueado».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC64_DIAGNOSTICO_NOAPLICA - Impuesto de departamento no soportado en No aplica (condicional)
- **Origen:** #13733 (AC2) · #13861 (AC4 y AC5) · #13866 (AC3) · #13864 (AC3)
- **Tipo / prioridad:** Borde · Baja
- **Precondición:** la misma del TC63.
- **Datos:** express `TST206` con el impuesto en el alcance.
- **Pasos:**
  1. Lanza el express.
  2. Abre el detalle de «Impuesto departamental».
  3. Descarga el PDF del vehículo con el botón «PDF».
- **Resultado esperado:**
  - Chip «No aplica» con «Departamento no soportado».
  - El detalle dice «El departamento del vehículo todavía no está soportado.», sin error ni «Reprocesar».
  - El PDF muestra ese mismo texto en la sección del impuesto.
  - Supuesto: el organismo de los fixtures no está en la réplica local de organismos, así que **todos** los particulares quedarían con este motivo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.9 Celdas de persona, SIMIT, avalúo y protección de documentos

#### QA_TC65_DIAGNOSTICO_PERSONAS - Locatario declarado se conserva y se ve enmascarado
- **Origen:** #13730 (AC4) · #13798 (AC2) · #13801 (AC2)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** sesión de ADMIN.
- **Datos:** express `TST161` con las 7 fuentes y el locatario CC `9999900016`.
- **Pasos:**
  1. Lanza el express con el locatario declarado.
  2. Abre el detalle de «RUNT locatario».
  3. Abre el detalle de «SIMIT locatario».
- **Resultado esperado:**
  - **RUNT locatario:** «Completada», sección «Locatario» con «Documento CC», **`****0016`** y el rótulo **«Declarado por el cliente»**. Nunca se ve el número completo.
  - **SIMIT locatario:** Rojo, con muchos comparendos.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC66_DIAGNOSTICO_PERSONAS - Locatario del RUNT reemplaza al declarado
- **Origen:** #13730 (AC3) · #13790 (AC2)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** sesión de ADMIN.
- **Datos:** express `TST163` con el locatario CC `9999900099`.
- **Pasos:**
  1. Lanza el express.
  2. Abre el detalle de «RUNT locatario».
- **Resultado esperado:** el documento mostrado es **`****0013`**, el del proveedor, **sin** «Declarado por el cliente». No aparece `****0099`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC67_DIAGNOSTICO_PERSONAS - Persona con error definitivo deja la celda en error sin bloquear el vehículo
- **Origen:** #13792 (AC2 y AC4) · #13794 (AC3)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** con ADMIN, en «Operación», pon el estimado en «Al finalizar el diagnóstico» y guarda.
- **Datos:** **L-ERROR**, un express `TST171` con las 7 fuentes y el locatario CC `9999900018`.
- **Pasos:**
  1. Lanza el express.
  2. Espera a que no queden celdas grises.
  3. Abre el detalle de «RUNT locatario».
- **Resultado esperado:**
  - «RUNT locatario» y «SIMIT locatario» quedan en **«Error»**, con «No se pudo consultar» y el botón «Reprocesar» para ADMIN.
  - El detalle dice «La consulta terminó en error» y un texto de error de fuente.
  - RUNT vehículo, RUNT propietario, SIMIT vehículo, SIMIT propietario y Avalúo quedan «Completada»: el vehículo no se bloquea.
  - El lote sigue «En proceso» y aparece el botón «Cerrar panel».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC68_DIAGNOSTICO_SIMIT - SIMIT vehículo muestra los comparendos con campos mínimos
- **Origen:** #13793 (AC1 y AC2) · #13801 (AC1)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST142`.
- **Pasos:**
  1. Abre el detalle de «SIMIT vehículo» de TST142.
- **Resultado esperado:**
  - «Semáforo SIMIT: Rojo».
  - La tabla tiene las columnas Número, Fecha, Valor, Estado y Organismo, con SIM-0001 (10/01/2026, $ 604.100, PENDIENTE) y SIM-0002 ($ 302.050).
  - Aparece la fecha del dato.
  - No hay ningún dato del infractor: ni nombre ni documento.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC69_DIAGNOSTICO_AVALUO - Avalúo con valor sugerido y valores por fuente
- **Origen:** #13795 (AC1)
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** L-MOTOR procesado.
- **Datos:** `TST141` y `TST142`.
- **Pasos:**
  1. Abre el detalle de «Avalúo comercial» de TST141.
  2. Abre el de TST142.
- **Resultado esperado:**
  - **TST141:**
    - «Valor sugerido» $ 52.000.000, con «Fuente principal: Fasecolda».
    - La tabla «Fuentes del avalúo» tiene Fasecolda «Con valor» $ 52.000.000, Mercado Libre $ 50.500.000 con 9 muestras y Base gravable $ 45.000.000.
  - **TST142:** solo Fasecolda $ 48.000.000. Las otras dos fuentes, «Sin datos».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC70_DIAGNOSTICO_AVALUO - Avalúo sin valor comercial completa y no se reprocesa
- **Origen:** #13795 (AC2 y AC3) · #13798 (AC3) · #13801 (AC4)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** L-MOTOR procesado. Sesión de ADMIN.
- **Datos:** `TST144`.
- **Pasos:**
  1. Mira la celda «Avalúo comercial» de TST144.
  2. Abre su detalle.
- **Resultado esperado:**
  - «Completada», sin botón «Reprocesar».
  - El detalle dice **«Sin valor comercial»** y «Ninguna fuente reportó un valor para este vehículo.».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC71_DIAGNOSTICO_SEGURIDAD - Documentos cifrados en la base y ausentes de los logs
- **Origen:** #13751 (Esc. 6) · #13730 (AC3) · #13790 (AC1) · #13792 (AC7) · #13794 (AC5)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** TC65, TC66 y TC67.
- **Datos:** —
- **Pasos:**
  1. Corre las dos consultas de S5.
  2. Ejecuta `grep -nE "99999[0-9]{4,5}" ~/.flit/logs/core-diagnostico.log`.
- **Resultado esperado:**
  - **S5:**
    - `prop_cifrado`, `loc_cifrado` y `cifrado` son `true` en todas las filas.
    - `locatario_doc_origen` vale `DECLARADO_CLIENTE` en TST161 y `PROVEEDOR` en TST163.
    - No hay columnas de nombre.
  - **grep:** no devuelve nada.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.10 Tablero con semáforos, sondeo, cierre y reproceso

#### QA_TC72_DIAGNOSTICO_TABLERO - Sondeo cada 3 segundos que se detiene sin pendientes
- **Origen:** #13748 (AC2) · #13743 (AC1)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** DevTools → Network, con el filtro `grilla`.
- **Datos:** express `TST159` con las 7 fuentes.
- **Pasos:**
  1. Lanza el express y deja abierto el tablero.
  2. Observa las peticiones `GET …/grilla?desde=N`.
  3. Espera al estado «Completado».
- **Resultado esperado:**
  - Mientras hay celdas grises sale una petición cada ~3 s, con `desde` creciente, y la grilla se repinta sin recargar la página.
  - Al quedar «Completado» dejan de salir peticiones y desaparece «Se actualiza sola cada 3 segundos.».
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC73_DIAGNOSTICO_TABLERO - Cerrar panel deja el lote Incompleto con estimado frente a consumido
- **Origen:** #13748 (AC7) · #13743 (AC4 y AC8) · #13796 (AC2)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-ERROR terminado con celdas en error (TC67). Estimado «Al finalizar el diagnóstico».
- **Datos:** L-ERROR.
- **Pasos:**
  1. En el tablero de L-ERROR, pulsa «Cerrar panel».
  2. Revisa el estado y la sección del estimado.
  3. Corre S4 con su id.
- **Resultado esperado:**
  - El estado pasa a **«Incompleto»**.
  - Aparece **«Estimado frente a consumido»** con «Consultas previstas al lanzar el lote y las que se consumieron por fuente (las reutilizadas no cuentan).», con las fuentes de persona rotuladas «al menos».
  - S4 registra `EN_PROCESO → INCOMPLETO` con disparador `CIERRE_PANEL` y el usuario de ADMIN.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC74_DIAGNOSTICO_REPROCESO - Admin reprocesa una celda en error con auditoría
- **Origen:** #13746 (AC1 y AC6) · #13748 (AC5)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-MOTOR sin celdas grises. Si sigue «En proceso», pulsa «Cerrar panel» para dejarlo «Incompleto». Sesión de ADMIN.
- **Datos:** `TST147`.
- **Pasos:**
  1. Pulsa «Reprocesar» en la celda RUNT de TST147. Su nombre accesible es «Reprocesar RUNT vehículo de TST147».
  2. Observa la celda y el estado global.
  3. Espera unos 15 s.
  4. Corre S3 con `TST147` y S4 con el id de L-MOTOR.
- **Resultado esperado:**
  - El botón dice «Reprocesando…».
  - La celda vuelve a gris, el estado pasa a **«En proceso»** y reaparece «Se actualiza sola cada 3 segundos.».
  - Como la simulación es determinista, la celda vuelve a «Error» (TIMEOUT). Esto es esperado.
  - S3 registra intentos nuevos con `disparador = REPROCESO` y el `usuario_id` de ADMIN.
  - S4 registra `INCOMPLETO → EN_PROCESO` con disparador `REPROCESO` y el usuario.
  - Las celdas de otros vehículos no cambian.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC75_DIAGNOSTICO_REPROCESO - Celdas bloqueadas o completadas no se reprocesan
- **Origen:** #13746 (AC3 y AC4) · #13795 (AC3)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** L-MOTOR procesado. Sesión de ADMIN.
- **Datos:** con H2, `<cidSimit148>` es la celda SIMIT_VEHICULO de TST148, `<cidRunt141>` la RUNT de TST141 y `<cidAval144>` la AVALUO de TST144.
- **Pasos:**
  1. Mira las celdas «Espera al RUNT del vehículo» de TST148.
  2. Con H1: `api('/consultas/<cidSimit148>/reprocesar','POST')`.
  3. Con H1: `api('/consultas/<cidRunt141>/reprocesar','POST')`.
  4. Con H1: `api('/consultas/<cidAval144>/reprocesar','POST')`.
- **Resultado esperado:**
  - Las celdas bloqueadas **no** tienen el botón «Reprocesar».
  - Los tres POST responden `409`; los pasos 3 y 4, con `CELDA_NO_EN_ERROR`.
  - S3 no registra ningún intento nuevo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.11 Backoffice FLIT y llenado manual

#### QA_TC76_DIAGNOSTICO_BACKOFFICE - Lotes con errores de una compañía con lectura auditada
- **Origen:** #13802 (AC1) · #13799 (AC1) · #13728 (Esc. 3)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** existen L-MOTOR y L-ERROR, con errores. Sesión de SUPER.
- **Datos:** el id de EMPRESA_A.
- **Pasos:**
  1. En el dock, abre «Backoffice».
  2. En «Identificador de la compañía», escribe el id y pulsa «Ver lotes».
  3. Marca «Solo lotes con errores».
  4. Corre S1.
- **Resultado esperado:**
  - La URL lleva `con_errores=true`.
  - La tabla «Lotes de la compañía» tiene las columnas Lote, Estado, Creado, Vehículos y Celdas en error, y solo muestra lotes con el chip «N con error», entre ellos L-MOTOR y L-ERROR.
  - S1 registra la lectura con el usuario de SUPER, `empresa_consultada` = EMPRESA_A y la fecha.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC77_DIAGNOSTICO_BACKOFFICE - Llenar a mano las celdas en error de una persona
- **Origen:** #13800 (AC1 y AC2) · #13802 (AC2) · #13799 (AC2)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** TC73 (L-ERROR «Incompleto»). Sesión de SUPER en la lista del TC76.
- **Datos:**
  - SIMIT locatario: «Semáforo» Verde, sin comparendos.
  - RUNT locatario: «Inscrito en el RUNT» marcado, «Estado del conductor» ACTIVO.
- **Pasos:**
  1. Abre L-ERROR. La URL es `/backoffice/diagnosticos/{id}?empresa=…`.
  2. Haz clic en el semáforo de «SIMIT locatario» de TST171 y pulsa «Llenar a mano».
  3. En «Llenar a mano · SIMIT locatario · TST171», llena los datos y pulsa «Confirmar llenado».
  4. Repite los pasos 2 y 3 con «RUNT locatario».
  5. Corre S4 con el id y S3 con `TST171`.
- **Resultado esperado:**
  - Tras cada llenado aparece «Celda llenada», por ejemplo «Se llenó a mano SIMIT locatario de TST171.».
  - La celda queda «Completada» con la marca **«Manual»**.
  - El lote pasa a «Completado».
  - S4 registra `INCOMPLETO → EN_PROCESO` con disparador `MANUAL` y el usuario, y luego `EN_PROCESO → COMPLETADO`.
  - S3 registra intentos `MANUAL` con el usuario de SUPER y sin transacción del proveedor.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC78_DIAGNOSTICO_BACKOFFICE - Celdas del RUNT y bloqueadas no ofrecen Llenar a mano
- **Origen:** #13802 (AC3) · #13800 (AC4 y AC5)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** L-MOTOR abierto en el backoffice con SUPER.
- **Datos:** con H2 desde la sesión de ADMIN, `<cidRunt148>` es la celda RUNT de TST148 y `<cidSimit148>` la SIMIT_VEHICULO de TST148.
- **Pasos:**
  1. Abre el semáforo de «RUNT vehículo» de TST148.
  2. Abre el de una celda bloqueada de TST148.
  3. Con H1 (SUPER): `api('/backoffice/consultas/<cidRunt148>/manual?empresa=<id EMPRESA_A>')`.
  4. Con H1 (SUPER): `api('/backoffice/consultas/<cidSimit148>/manual?empresa=<id EMPRESA_A>')`.
- **Resultado esperado:**
  - En ningún diálogo aparece el botón «Llenar a mano».
  - El paso 3 trae `admite_manual: false` con el motivo `FUENTE_NO_ADMITE_MANUAL`.
  - El paso 4 trae `admite_manual: false` con el motivo de celda bloqueada por el RUNT. Un PUT respondería 422 y 409.
  - Las celdas no cambian.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC79_DIAGNOSTICO_BACKOFFICE - Valor inválido del formulario se marca junto al campo
- **Origen:** #13802 (AC5) · #13800 (AC6)
- **Tipo / prioridad:** Error · Media
- **Precondición:** con ADMIN, lanza el express `TST172` con las 7 fuentes y el locatario CC `9999900028`. Sus celdas de locatario quedan en Error.
- **Datos:** «Valor del comparendo 1 (pesos)» = `abc`.
- **Pasos:**
  1. Con SUPER, abre ese lote en el backoffice.
  2. En «SIMIT locatario» de TST172, pulsa «Llenar a mano».
  3. Pulsa «Agregar comparendo», escribe `abc` en el valor y pulsa «Confirmar llenado».
- **Resultado esperado:**
  - Aparece un mensaje de error **junto al campo** del valor.
  - No se guarda nada y la celda sigue en «Error» en la grilla.
  - Supuesto: si el formulario impide escribir letras, prueba con un valor negativo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC80_DIAGNOSTICO_BACKOFFICE - La compañía ve el dato manual sin marca y el historial como AUTO
- **Origen:** #13801 (AC5) · #13798 (AC4 y AC6) · #13799 (AC2)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** TC77.
- **Datos:** L-ERROR.
- **Pasos:**
  1. Con ADMIN, abre el tablero de L-ERROR.
  2. Abre el detalle de «SIMIT locatario».
  3. Con H1 (ADMIN): `api('/diagnosticos/<id>/historial')`.
  4. Con H1 (SUPER): `api('/backoffice/diagnosticos/<id>/historial?empresa=<id EMPRESA_A>')`.
- **Resultado esperado:**
  - La celda se ve «Completada» **sin** «Manual» ni ningún icono, texto o estilo distinto al de una consulta por API.
  - **Paso 3:** las transiciones del llenado salen con `disparador: AUTO` y `usuario_id: null`.
  - **Paso 4:** las mismas transiciones salen con `MANUAL` y el usuario.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC81_DIAGNOSTICO_BACKOFFICE - Dato llenado a mano no se reutiliza en otro lote
- **Origen:** #13800 (AC8)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** TC77, hecho hoy.
- **Datos:** ADMIN; express `TST171` con las 7 fuentes y el locatario CC `9999900018`.
- **Pasos:**
  1. Lanza el express.
  2. Mira «SIMIT locatario» y «RUNT locatario».
  3. Corre S3 con `TST171`.
- **Resultado esperado:**
  - Las dos celdas de locatario se consultan de nuevo y quedan en «Error». No copian el dato manual.
  - El RUNT vehículo sí puede mostrar «Dato del …», porque viene de la API.
  - S3 muestra intentos nuevos de las fuentes de locatario con `desde_cache = false`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.12 Reportes: PDF, Excel y ZIP

#### QA_TC82_DIAGNOSTICO_REPORTES - PDF del vehículo con encabezado y documentos enmascarados
- **Origen:** #13848 (AC1 y AC6) · #13847 (AC1 y AC4) · #13851 (AC2)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-MOTOR, con TST141 «Terminado». Sesión de ADMIN.
- **Datos:** `TST141`.
- **Pasos:**
  1. En la columna «Reporte» de TST141, pulsa «PDF». Su nombre accesible es «Descargar PDF de TST141».
  2. Abre el archivo.
- **Resultado esperado:**
  - Se descarga un `application/pdf` con el membrete FLIT y el encabezado completo:
    - el ID del diagnóstico y «QA Motor 8 escenarios»;
    - el creador, que en local es «No disponible» porque no hay Identidad;
    - la empresa y la fecha y hora de creación en hora Colombia;
    - **«Diagnóstico Simple»** y la placa TST141.
  - Las secciones van en el orden de la grilla.
  - El propietario sale `****0001`, nunca el número completo, y no aparece ningún nombre.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC83_DIAGNOSTICO_REPORTES - PDF deshabilitado mientras el vehículo sigue en curso
- **Origen:** #13851 (AC1) · #13848 (AC3)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** sesión de ADMIN.
- **Datos:** express `TST157`. El RUNT reintenta unos 5 s.
- **Pasos:**
  1. Lanza el express.
  2. Mientras la celda RUNT siga gris, pasa el cursor o el foco sobre «PDF».
- **Resultado esperado:** el botón «PDF» está deshabilitado y tiene la ayuda **«Disponible cuando terminen las consultas de este vehículo»**.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC84_DIAGNOSTICO_REPORTES - PDF de un lote Incompleto muestra «consulta no disponible»
- **Origen:** #13848 (AC8) · #13851 (AC6) · #13850 (AC7)
- **Tipo / prioridad:** Borde · Alta
- **Precondición:** L-MOTOR «Incompleto». Si un reproceso lo dejó «En proceso», pulsa «Cerrar panel».
- **Datos:** `TST148`.
- **Pasos:**
  1. Pasa el cursor sobre «PDF» de TST148.
  2. Descárgalo y ábrelo.
- **Resultado esperado:**
  - El botón está **habilitado** con la ayuda «Las consultas no disponibles aparecerán así en el PDF».
  - Las secciones en error dicen **«consulta no disponible»**, sin TIMEOUT, RESPUESTA_INVALIDA, causa técnica ni origen.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC85_DIAGNOSTICO_REPORTES - PDF sin cambios se reutiliza y cambia tras un reproceso
- **Origen:** #13846 (AC1 y AC2) · #13848 (AC5)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** TC84.
- **Datos:** L-MOTOR y `TST148`.
- **Pasos:**
  1. Descarga dos veces el PDF de TST148.
  2. Corre S6.
  3. Reprocesa el RUNT de TST148, espera el «Error», pulsa «Cerrar panel» y vuelve a descargar el PDF.
  4. Corre S6 de nuevo.
- **Resultado esperado:**
  - Tras el paso 1 hay **una sola** fila PDF vigente para ese vehículo, con su `solicitado_por`. La segunda descarga no regenera.
  - Tras el paso 3 la fila anterior queda `vigente = false`, se conserva, y hay una nueva con `vigente = true` y otra huella.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC86_DIAGNOSTICO_REPORTES - Excel del lote con columnas del alcance y hoja Consultas
- **Origen:** #13849 (AC1, AC3 y AC4)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-ERROR «Completado» tras TC77, con el estimado «Al finalizar el diagnóstico».
- **Datos:** L-ERROR.
- **Pasos:**
  1. En «Descargas del lote», pulsa «Descargar Excel».
  2. Abre el archivo.
- **Resultado esperado:**
  - La hoja **«Vehículos»** tiene 1 fila (TST171) con las columnas fijas y las de las 7 fuentes del alcance. No hay columnas del impuesto.
  - La hoja **«Consultas»** tiene las consultas estimadas y consumidas por fuente.
  - Los documentos salen como `****0018`.
  - Ninguna columna dice origen ni usuario manual.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC87_DIAGNOSTICO_REPORTES - ZIP del lote en segundo plano con índice
- **Origen:** #13850 (AC1 y AC2) · #13851 (AC3)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** L-MOTOR «Incompleto».
- **Datos:** L-MOTOR.
- **Pasos:**
  1. Pulsa «Descargar PDF (.zip)».
  2. Espera el estado y pulsa «Descargar el ZIP».
  3. Descomprime el archivo.
  4. Vuelve a pulsar «Descargar PDF (.zip)» y corre S6.
- **Resultado esperado:**
  - Aparece «Preparando 9 PDF…» y luego **«El ZIP está listo: 9 PDF.»**.
  - El ZIP trae 9 PDF y un índice que no deja ningún vehículo por fuera.
  - En el paso 4 lo entrega sin generar otro: S6 muestra una sola fila ZIP vigente.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.13 Listado con filtros

#### QA_TC88_DIAGNOSTICO_LISTADO - Listado del inicio con columnas, orden y creador
- **Origen:** #13852 (AC1 y AC4) · #13845 (AC1 y AC4)
- **Tipo / prioridad:** Happy Path · Alta
- **Precondición:** EMPRESA_A ya tiene varios lotes. Sesión de ADMIN.
- **Datos:** —
- **Pasos:**
  1. Abre el inicio y revisa la tabla «Diagnósticos de la compañía».
  2. Haz clic en la fila de L-MOTOR.
- **Resultado esperado:**
  - Las columnas son ID, Nombre, Creado, Tipo, Trámite, Estado y Creador.
  - Los lotes van del más reciente al más antiguo, con «Diagnóstico Simple» en Tipo.
  - El Creador dice **«No disponible»**: en local no hay Identidad y es comportamiento esperado.
  - No aparece ningún lote de FLIT.
  - El clic abre su tablero.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC89_DIAGNOSTICO_LISTADO - Filtros en la URL y lote Sin lanzar
- **Origen:** #13852 (AC2 y AC3) · #13845 (AC7)
- **Tipo / prioridad:** Happy Path · Media
- **Precondición:** existe QA Carga 02 en CREADO.
- **Datos:** Estado «Completado»; Nombre «Express».
- **Pasos:**
  1. Sin filtros, busca QA Carga 02.
  2. Haz clic en su fila.
  3. Vuelve al inicio, elige el estado «Completado» y escribe «Express» en «Nombre».
- **Resultado esperado:**
  - QA Carga 02 lleva el chip **«Sin lanzar»** y su clic abre `/diagnosticos/{id}/carga`.
  - Con el filtro, la URL refleja el estado y el nombre, la tabla vuelve a la página 1 y solo muestra lotes Completado con «Express» en el nombre.
  - QA Carga 02 no aparece con el filtro de estado.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC90_DIAGNOSTICO_LISTADO - Filtros sin resultados y rango de fechas inválido
- **Origen:** #13852 (AC5) · #13845 (AC5)
- **Tipo / prioridad:** Error · Media
- **Precondición:** sesión de ADMIN en el inicio.
- **Datos:**
  - Nombre `zzz-sin-coincidencias`.
  - Desde 22/10/2026 y Hasta 01/10/2026.
- **Pasos:**
  1. Filtra por el nombre.
  2. Pulsa «Limpiar filtros».
  3. Pon el rango de fechas.
- **Resultado esperado:**
  - **Paso 1:** «No hay diagnósticos con estos filtros» con «Limpiar filtros». No sale el vacío de «Todavía no hay diagnósticos».
  - **Paso 3:** «Revisa los filtros» y «La fecha final no puede ser anterior a la inicial.», porque la API responde 400 `RANGO_INVALIDO`.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC91_DIAGNOSTICO_LISTADO - Visibilidad Solo los propios en el listado (condicional)
- **Origen:** #13845 (AC3) · #13743 (AC7)
- **Tipo / prioridad:** Borde · Media
- **Precondición:** USUARIO_D creado. Con ADMIN, pon la visibilidad en «Solo los propios» y guarda.
- **Datos:** USUARIO_D lanza un express `TST192`.
- **Pasos:**
  1. Con USUARIO_D, lanza TST192 y revisa el listado.
  2. Con USUARIO_D, abre la URL del tablero de L-MOTOR.
  3. Con ADMIN, revisa el listado.
  4. Vuelve a poner la visibilidad en «Todos los diagnósticos de la compañía».
- **Resultado esperado:**
  - USUARIO_D solo ve su lote.
  - L-MOTOR le muestra «No encontramos este diagnóstico».
  - ADMIN ve todos, incluido TST192.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.14 Impuesto departamental bloqueado

#### QA_TC92_DIAGNOSTICO_IMPUESTO - Impuesto no se ofrece en la parametrización ni en la operación
- **Origen:** #13865 (AC2 y AC4) · #13863 (AC4)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** la bandera en false, que es lo normal. Sesión de ADMIN.
- **Datos:** —
- **Pasos:**
  1. Abre `/diagnosticos/nuevo` y revisa «Por Vehículo».
  2. Abre «Configuración» → «Operación» → «Fuentes por defecto».
  3. Con H1: `api('/configuracion')`.
- **Resultado esperado:**
  - «Impuesto departamental» no aparece en ninguno de los dos sitios.
  - En el JSON, `fuentes_disponibles` no incluye `IMPUESTO_DEPARTAMENTAL` y sí las demás, en el orden del enum.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC93_DIAGNOSTICO_IMPUESTO - Impuesto bloqueado en el contrato con su ayuda
- **Origen:** #13865 (AC3) · #13863 (AC2)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** sesión de SUPER con el contrato de EMPRESA_A cargado.
- **Datos:** —
- **Pasos:**
  1. En «Fuentes habilitadas», intenta marcar «Impuesto departamental».
  2. Pasa el cursor o el foco sobre ella.
- **Resultado esperado:** la casilla está bloqueada y la ayuda dice **«Disponible cuando la consulta automatizada esté en producción»**. No queda marcada.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC94_DIAGNOSTICO_IMPUESTO - Pedir el impuesto por API responde 422 FUENTE_NO_DISPONIBLE
- **Origen:** #13733 (AC6) · #13863 (AC2 y AC5) · #13741 (Esc. 12)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** sesiones de SUPER y de ADMIN.
- **Datos:** —
- **Pasos:**
  1. Con H1 (SUPER): `api('/backoffice/empresas/<id EMPRESA_A>/configuracion/contrato','PUT',{fuentes_habilitadas:['RUNT','IMPUESTO_DEPARTAMENTAL']})`.
  2. Con H1 (ADMIN): `api('/diagnosticos','POST',{nombre:'QA impuesto',modo:'SIMPLE',alcance:{por_vehiculo:['RUNT','IMPUESTO_DEPARTAMENTAL'],por_propietario:[],por_locatario:[]}})`.
  3. Corre S2.
- **Resultado esperado:**
  - Los dos responden `422` con `FUENTE_NO_DISPONIBLE`; el segundo, con `campo = alcance`.
  - El contrato no cambia y S2 no registra nada.
  - No se crea ningún lote.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

### 3.15 F10: RUNT por Intempo en modo real y brechas conocidas

> Antes de TC95, corre el paso 1 de TC97 sobre los logs del modo simulado: el reinicio los trunca. Después ejecuta `bash scripts/dev/levantar-diagnostico-local.sh parar` y `bash scripts/dev/levantar-diagnostico-local.sh real --sin-build`, y vuelve a iniciar sesión.

#### QA_TC95_DIAGNOSTICO_RUNTREAL - Modo real: celda RUNT no conectada sin reintento
- **Origen:** #13878 (AC2 y AC4) · #13876 (AC4) · #13791 (AC2)
- **Tipo / prioridad:** Borde · Alta
- **Precondición:** pila en modo `real`, con `INTEMPO_API_BASE_URL` vacía. Sesión de ADMIN.
- **Datos:** express `TST181` (RUNT y SIMIT vehículo) y express `9ZZPRUEBA00000181`. Ninguno se consultó hoy.
- **Pasos:**
  1. Lanza el express con TST181.
  2. Abre el detalle de «RUNT vehículo».
  3. Espera 20 s y corre S3 con `TST181`.
  4. Repite los pasos 1 y 2 con el VIN.
- **Resultado esperado:**
  - El RUNT queda en **«Error»** casi de inmediato, sin la espera de un reintento.
  - El detalle dice **«Esta fuente no está conectada en este ambiente.»**.
  - SIMIT vehículo queda con «Espera al RUNT del vehículo».
  - S3 tiene **un solo** intento de RUNT con `tipo_error = NO_CONECTADO`, aunque pasen 20 s.
  - Con el VIN se ve lo mismo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC96_DIAGNOSTICO_RUNTREAL - Modo real: no se registra consumo sin conexión
- **Origen:** #13876 (AC4)
- **Tipo / prioridad:** Borde · Alta
- **Precondición:** modo `real`. Corre S7 y anota el número de filas **antes** de TC95.
- **Datos:** —
- **Pasos:**
  1. Después de TC95, corre S7 otra vez.
  2. Ejecuta `select count(*) from consultas.consumo where fuente in ('runt_intempo_placa','runt_intempo_vin') and ocurrido_en > now() - interval '1 hour';`.
- **Resultado esperado:** el número de filas no cambió: `count = 0`, o el mismo de antes. core-consultas respondió `FailedPrecondition` sin consumo ni llamada a Intempo.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC97_DIAGNOSTICO_RUNTREAL - Logs sin placa, VIN ni documentos
- **Origen:** #13875 (AC5) · #13878 (AC1) · F10-C6 · #13792 (AC7)
- **Tipo / prioridad:** Error · Alta
- **Precondición:** el paso 1 se hace en modo simulado, antes de reiniciar. El paso 2, después de TC95.
- **Datos:** —
- **Pasos:**
  1. En modo simulado: `grep -nE "TST[0-9]{3}|9ZZPRUEBA|99999[0-9]{4,5}" ~/.flit/logs/core-diagnostico.log ~/.flit/logs/core-consultas.log`.
  2. En modo real, tras TC95, repite el mismo `grep`.
- **Resultado esperado:** los dos `grep` no devuelven ninguna línea. Si aparece algo, anota la línea **sin copiar el valor** en la tabla de defectos.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC98_DIAGNOSTICO_RUNTREAL - Vehículo no registrado muestra «La fuente rechazó la consulta.» (comportamiento esperado por decisión)
- **Origen:** #13877 (AC3) · decisión **G3=a**
- **Tipo / prioridad:** Borde · Baja
- **Precondición:** **no se puede reproducir en local**: haría falta una URL real de Intempo, y está prohibido configurarla. Se verifica en el código y en las pruebas.
- **Datos:** —
- **Pasos:**
  1. En `frontend-diagnostico/lib/tablero/detalle.ts`, busca `ESTADO_NO_OK` en `TIPOS_ERROR`.
  2. Con la pila parada, ejecuta `dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --filter "FullyQualifiedName~MapperIntempo"`.
- **Resultado esperado:**
  - El vehículo no registrado mapea a `ESTADO_NO_OK` definitivo, sin reintento.
  - El tablero muestra **«La fuente rechazó la consulta.»**, no el mensaje fijo «El RUNT no registra el vehículo consultado.».
  - **Comportamiento esperado por decisión (G3=a, sin cambio de frontend).** No se radica como defecto.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

#### QA_TC99_DIAGNOSTICO_RUNTREAL - Error AUTENTICACION sin texto propio en el detalle (brecha conocida)
- **Origen:** #13877 (AC3) · #13878 (`Unauthenticated` y `PermissionDenied` → `AUTENTICACION`)
- **Tipo / prioridad:** Error · Baja
- **Precondición:** no se puede inducir en local sin romper la autenticación entre servicios. Se verifica en el código.
- **Datos:** —
- **Pasos:**
  1. En `frontend-diagnostico/lib/tablero/detalle.ts`, revisa `TIPOS_ERROR`.
  2. Comprueba si existe la clave `AUTENTICACION`.
- **Resultado esperado:**
  - **Comportamiento actual:** no hay texto para `AUTENTICACION`, así que el detalle mostraría el genérico «La fuente respondió con un error.». Sí hay texto para `HTTP_5XX` y `ESTADO_NO_OK`.
  - **Brecha conocida.** Regístrala en §5 como mejora, con la severidad Baja.
- **Resultado:** ☐ Pasa / ☐ Falla — Observaciones: ____________________

---

## 4. Pruebas automatizadas de respaldo

> **Para la pila antes de correr las suites .NET:** `bash scripts/dev/levantar-diagnostico-local.sh parar`. La pila bloquea `bin/` del servicio en ejecución y además la máquina va corta de memoria. Las cifras esperadas salen de los logs de cierre de la ola F10 (`.claude/state/runt-intempo/logs/F10-test-*.txt`); no se corrieron para este documento.

**core-diagnostico. Esperado: 741 superadas, 0 con error y 0 omitidas (cerca de 8 min).**

```bash
cd /d/Cursor/FLIT/2.0/services/core-diagnostico
export ConnectionStrings__Core="Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres"
dotnet build tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj
dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build
# Por módulo (namespace Flit.Diagnostico.Tests.<Modulo>):
dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~Flit.Diagnostico.Tests.Consultas"
#   Aislamiento · Api · Carga · Catalogos · Configuracion · Consultas · Diagnosticos · Dominio
#   Persistencia · Plataforma · Reportes · Seguridad · Tablero · Trabajos
# F10 en el diagnóstico:
dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~MapperIntempo|FullyQualifiedName~FuenteRunt"
```

**core-consultas. Esperado: 165 superadas, 0 con error y 0 omitidas (cerca de 1 min).** Usa la misma variable `ConnectionStrings__Core`.

```bash
cd /d/Cursor/FLIT/2.0/services/core-consultas
dotnet test tests/Flit.Consultas.Tests/Flit.Consultas.Tests.csproj
# Solo F10:
dotnet test tests/Flit.Consultas.Tests/Flit.Consultas.Tests.csproj --filter "FullyQualifiedName~Intempo|FullyQualifiedName~PartidoNombre"
```

**frontend-diagnostico (Vitest).** Esperado: 497 pruebas en 41 archivos. Es la cifra del cierre de D5; F10 no tocó el frontend.

```bash
pnpm --dir /d/Cursor/FLIT/2.0/frontend-diagnostico test
```

**Prueba sin red del script de humo de Intempo (#13876).** Son 3 pruebas `unittest` que no llaman a Intempo:

```bash
cd /d/Cursor/FLIT/2.0 && python -I scripts/smoke/test_humo_intempo.py
```

**E2E de Playwright existentes, sin correr.** Están en `frontend-diagnostico/e2e/` y se lanzan desde la raíz con:

```
DIAGNOSTICO_URL=http://127.0.0.1:4024 <variables> pnpm --dir frontend exec playwright test ../frontend-diagnostico/e2e/<spec>
```

Las credenciales entran solo por variables de entorno y nunca se escriben en archivos.

| Spec | HU | Variables que pide | Nota |
|---|---|---|---|
| `entrada-desde-hub.spec.ts` | #13732 | `E2E_USUARIO`, `E2E_CLAVE`, `E2E_USUARIO_SUSPENDIDO`, `E2E_CLAVE_SUSPENDIDO` | Usuario de una compañía con el producto apagado. |
| `configuracion.spec.ts` | #13744 | `E2E_ADMIN`, `E2E_CLAVE_ADMIN`, `E2E_USUARIO`, `E2E_CLAVE`, `E2E_BACKOFFICE`, `E2E_CLAVE_BACKOFFICE`, `E2E_EMPRESA_CLIENTE` (uuid) | |
| `tablero.spec.ts` | #13748 | `E2E_USUARIO`, `E2E_CLAVE` (sin rol admin), `E2E_PLACA_EXPRESS` (por defecto TST101) | Simulación encendida. |
| `backoffice-llenado.spec.ts` | #13802 | `E2E_BACKOFFICE_USUARIO`, `E2E_BACKOFFICE_CLAVE`, `E2E_EMPRESA`, `E2E_ADMIN_USUARIO`, `E2E_ADMIN_CLAVE` | Necesita un lote con SIMIT vehículo en ERROR, que la simulación actual no produce (ver §1.2). |
| `evidencias.spec.ts` | #13851 | `E2E_USUARIO`, `E2E_CLAVE`, `E2E_PLACA_EXPRESS` | |
| `listado.spec.ts` | #13852 | `E2E_USUARIO`, `E2E_CLAVE`, `E2E_PLACA_EXPRESS` | |
| `impuesto.spec.ts` | #13866 | `E2E_USUARIO`, `E2E_CLAVE`, `E2E_PLACA_IMPUESTO` (por defecto TST201) | Necesita `Diagnostico__Fuentes__ImpuestoDisponible=true` solo en ese entorno. |

---

## 5. Registro de defectos hallados

Usa solo datos inventados: placas TST###, VIN 9ZZPRUEBA…, documentos 99999…. Nunca copies valores de logs, tokens ni URLs de proveedores.

| # | TC | HU | Severidad (Crítico/Alto/Medio/Bajo) | Resumen | Pasos y evidencia (sin datos reales) | Esperado vs. observado | Estado |
|---|---|---|---|---|---|---|---|
| O-1 | TC23 | #13752 | Bajo (observación) | No hay ningún enlace ni botón hacia `/diagnosticos/nuevo`. La carga masiva solo se alcanza escribiendo la URL o desde un lote «Sin lanzar». | Revisión de `app/(app)/**` y `components/**` | Debería existir una entrada visible a «Nuevo diagnóstico». | Por confirmar con el PO |
| B-1 | TC99 | #13877 / #13878 | Bajo (brecha conocida) | `detalle.ts` no tiene texto para `AUTENTICACION`. | `lib/tablero/detalle.ts` → `TIPOS_ERROR` | Se espera un texto propio; se observa el genérico «La fuente respondió con un error.». | Abierta |
| 1 | | | | | | | |
| 2 | | | | | | | |
| 3 | | | | | | | |
| 4 | | | | | | | |
| 5 | | | | | | | |

### Supuestos no verificados en el código

- **TC00-B y C:** el frontend de Trámites arranca en 3000 con su `.env` local apuntando a la pila, y el SuperAdmin entra a `/admin/companies`.
- **TC00-E:** la pestaña «Usuarios» de la compañía permite asignar roles de Diagnóstico.
- **TC05 y TC07:** el código exacto de la 403 lo decide el hub (`PRODUCT_NOT_ENABLED` o `PRODUCT_ROLE_REQUIRED`) o la API (`SIN_PERMISOS_DIAGNOSTICO`).
- **TC25:** el catálogo local de Trámites publica los tres tipos de la etapa.
- **TC40:** ni el gateway ni el proxy de Next cortan el cuerpo de más de 5 MB antes que la API.
- **TC64:** el organismo de los fixtures no está en la réplica local de organismos.
- **TC79:** el formulario manual acepta texto en el campo del valor.
- **H1:** el proxy `/api/v1/*` acepta un `fetch` desde la consola con la cookie de sesión.
