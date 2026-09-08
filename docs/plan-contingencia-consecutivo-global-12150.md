# Plan de contingencia — Feature #12150, identificador consecutivo global

> Aplica al despliegue de las HUs **#12151, #12152, #12153 y #12154**, que van en un solo PR.
> Rama `feature/AB-12151-consecutivo-global-tramites`, sobre `develop` @ `3c62944d`.

---

## Por qué este despliegue necesita un plan propio

Casi cualquier despliegue se revierte volviendo el código atrás. **Este no**, y conviene tenerlo
claro antes de empezar:

> Si se revierte el código **sin** revertir la migración, el generador viejo vuelve a escribir
> `TRM-2026-…` y el camino de creación manda cadena vacía. Ambas violan
> `ck_procedure_instances_reference_numerico`, así que **deja de poder crearse ningún trámite**.

Verificado en laboratorio: un `UPDATE` con `'MIG-TR-8617'` y otro con `''` fallan los dos contra
el CHECK.

**Un respaldo de base de datos no basta por sí solo. Código y migración se revierten juntos.**

---

## 1. Antes de desplegar (en cada ambiente)

### 1.1 Respaldo

```bash
pg_dump -Fc -d <base> -f flit_<ambiente>_pre12150_$(date +%Y%m%d_%H%M).dump
```

### 1.2 Huella de los radicados — no basta con contar filas

```sql
SELECT md5(string_agg(id || ':' || reference_number, ',' ORDER BY id))
  FROM tramites.procedure_instances;
```

Guardar ese valor. **Es el punto más importante de este apartado**, y sale de un error cometido
durante el desarrollo: una base de trabajo quedó renumerada sin que nadie lo notara, porque se
verificó el *conteo* de filas — y un conteo no cambia con una renumeración. Solo comparar valores
lo detecta.

### 1.3 Punto de reversión

| Qué | Valor |
|---|---|
| Commit base (`develop` antes del feature) | `3c62944d` |
| Migración anterior a las nuestras | `20260907130000_HU12126_SeedMatriculaTraspasoDescriptions` |
| Migraciones que introduce el feature | `20260908120000_HU12151_ConsecutivoGlobalTramite`<br>`20260908130000_HU12153_OrdenNumericoRadicado` |

### 1.4 Avisar al equipo

La migración **renumera todos los trámites del ambiente**. Conviene que nadie tenga trabajo a
medias ni referencias apuntadas en papel durante la ventana.

---

## 2. Orden de despliegue

**DEV → QA → PDN**, validando entre cada uno.

En **producción la migración no renumera nada** (base vacía tras el borrón y cuenta nueva) y el
primer trámite recibirá el `1`. Es el ambiente de menor riesgo; va último por disciplina, no por
peligro.

---

## 3. Verificación posterior (en cada ambiente)

### 3.1 Integridad de los datos

```sql
SELECT count(*) FILTER (WHERE reference_number !~ '^[1-9][0-9]*$') AS invalidos,
       count(*) - count(DISTINCT reference_number)                 AS duplicados
  FROM tramites.procedure_instances;
-- esperado: 0 y 0
```

### 3.2 Objetos de esquema

```sql
SELECT (SELECT count(*) FROM pg_class      WHERE relname = 'procedure_instance_reference_seq') AS secuencia,
       (SELECT count(*) FROM pg_class      WHERE relname = 'uq_procedure_instances_reference') AS indice_global,
       (SELECT count(*) FROM pg_constraint WHERE conname = 'uq_procedure_instances_tenant_reference') AS constraint_vieja;
-- esperado: 1, 1, 0
```

### 3.3 La prueba de humo que de verdad importa

**Crear un trámite desde la aplicación.** Si `ValueGeneratedOnAdd` no viajara bien en el build
desplegado, EF mandaría cadena vacía y la creación fallaría — y **ese fallo no aparece en ningún
otro sitio**. La suite no puede cubrirlo porque no tiene Postgres.

Crear dos seguidos confirma además que los números son distintos y consecutivos.

---

## 4. Si hay que revertir

| Situación | Acción |
|---|---|
| **La migración falló** y no llegó a aplicarse | Nada que revertir en datos: la migración es transaccional, entra entera o no entra. Volver el código a `3c62944d`. |
| **Migración aplicada y el sistema falla** | Revertir **las dos cosas**:<br>`dotnet ef database update 20260907130000_HU12126_SeedMatriculaTraspasoDescriptions`<br>y el código a `3c62944d`. |
| **Se necesita el estado exacto previo** | Restaurar el dump del punto 1.1. |

### Lo que el `Down()` devuelve y lo que no

Las dos migraciones tienen `Down()` y **devuelven la estructura**: quitan la secuencia, el `CHECK`
y el índice global, y reponen la constraint única por tenant.

**No devuelven los radicados originales.** La renumeración es irreversible: el valor anterior no
se guarda en ninguna parte. Está dicho explícitamente en el `Down()` de la migración para que
nadie cuente con recuperarlos revirtiendo. El único camino de vuelta a los valores viejos es el
dump.

---

## 5. Riesgos aceptados y cosas que no están cubiertas

| Punto | Estado |
|---|---|
| **Migrador V1 → V2** | Los mappers están cubiertos con pruebas, pero **el `--dry-run` contra una base V1 real no se ejecutó** (requiere credenciales de V1). Si hay previsto migrar trámites de V1 poco después del despliegue, hacer ese ensayo antes. |
| **Documentos ya generados en dev y QA** | Conservan el nombre viejo (`consolidado_TRM-2026-000006.pdf`). Es esperado: se emitieron antes del cambio. Se corrigen regenerando, y cada generador es independiente — regenerar el consolidado maestro no toca el FUR. **En producción no aplica.** |
| **Seeds y números bajos (dev y QA)** | El seed analítico crea ~945 trámites antes de que nadie cree el primero real, así que el primer trámite manual no será el `1`. En producción no ocurre: los seeds están cerrados por entorno. |
| **Saltos en la secuencia** | Las secuencias de Postgres no son transaccionales: un alta revertida o un trámite borrado dejan un número vacío para siempre. Se verán `1, 2, 4`. **No es defecto.** |
| **Consecutivo global entre compañías** | Cada compañía ve el tramo que le toca, no una numeración propia desde 1; y el número revela el volumen agregado de la plataforma. Decidido con el PO. |
| **Buscador multi-campo** | Buscar `18` también trae trámites cuya placa contiene «18». Comportamiento previo, pero con radicados numéricos cortos el cruce será más frecuente. |

---

## 6. Evidencia del ensayo previo

Todo lo anterior se apoya en pruebas ejecutadas antes del despliegue, no en suposiciones:

- Cadena completa de migraciones aplicada **desde cero contra Postgres real**: 224 migraciones, `Done.`
- Renumeración verificada sobre copia de dev: `1..17` por antigüedad, 5 compañías, 0 duplicados.
- **Idempotencia**: reejecutar el script no reasigna números ya emitidos.
- Los tres seeds ejecutados dos veces sobre base migrada: 0 errores propios, 0 duplicación.
- Barrido en dev con la aplicación corriendo: **dos trámites creados por la interfaz recibieron el
  `18` y el `19`**, asignados por la base.
- Suite backend: 0 fallos nuevos. Vitest: 75 fallos heredados antes y después, medido con `git stash`.
