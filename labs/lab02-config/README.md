# Lab 02: Configuración externalizada y PostgreSQL propio

**Día:** martes · **Branch de referencia:** `lab02-start` → `lab02-solution`

## Objetivo

Eliminar la connection string hardcodeada de TaskFlow, externalizarla a
`ConfigMap` (datos no sensibles) + `Secret` (credenciales), desplegar
PostgreSQL como su propio `Deployment`/`Service` (ya no como sidecar), y
verificar que la API es realmente stateless.

## Contexto

En el lab01 el pod arrancaba gracias a un sidecar de Postgres: un parche
para que `Host=localhost` (hardcodeado) siguiera siendo válido. Hoy se
resuelve de raíz: Postgres pasa a ser un componente independiente,
alcanzable por su nombre de `Service` (DNS interno de OpenShift), y la app
deja de tener ningún valor de configuración hardcodeado.

Este lab aplica dos factores de [Twelve-Factor App](https://12factor.net/es/):
**III. Config** (la configuración vive en el entorno, nunca en el código
ni en la imagen) y **VI. Processes** (el proceso corre stateless;
cualquier estado persistente vive en un backing service, acá Postgres).
`ConfigMap`/`Secret` son la forma en que Kubernetes implementa "config en
el entorno"; separar Postgres del pod de la API es lo que hace a la app
realmente stateless, no solo la ausencia de un valor hardcodeado.

## Pasos

> **Antes de empezar:** confirma que ya hiciste `oc login` como tu usuario
> (Paso 0 de `labs/README.md`) en esta terminal, `oc whoami` tiene que
> devolver tu usuario, no un `system:serviceaccount:...`. La sesión no
> persiste entre reinicios del workspace.

1. **Refactorizar `Program.cs`.** Buscar el bloque marcado
   `// TODO (lab02)` en `app/TaskFlow.Api/Program.cs` y reemplazar
   `HardcodedConnectionString` por una connection string construida desde
   configuración:
   ```csharp
   var dbHost = builder.Configuration["Db:Host"] ?? "localhost";
   var dbPort = builder.Configuration["Db:Port"] ?? "5432";
   var dbName = builder.Configuration["Db:Name"] ?? "taskflow";
   var dbUser = builder.Configuration["Db:Username"] ?? "taskflow";
   var dbPassword = builder.Configuration["Db:Password"] ?? "";

   var connectionString = new Npgsql.NpgsqlConnectionStringBuilder
   {
       Host = dbHost,
       Port = int.Parse(dbPort),
       Database = dbName,
       Username = dbUser,
       Password = dbPassword
   }.ConnectionString;
   ```
   Usar `connectionString` en `AddDbContext` y en `AddNpgSql(...)` en vez de
   la constante. Quitar `ConnectionStrings:Default` de `appsettings.json`:
   ya no debe quedar ningún valor de conexión en el repo.

2. **Reconstruir y publicar la imagen con el `Program.cs` nuevo,
   con un tag distinto de `lab01`.** La imagen `taskflow-api:lab01` tiene
   compilada la connection string vieja; el `Deployment` de este lab tiene
   que apuntar a una imagen que ya lea `Db__Host` de la configuración.
   Publicarla bajo el tag `lab02` en vez de sobreescribir `lab01`: un
   `Deployment` que ya corrió con `taskflow-api:lab01` no vuelve a bajar la
   imagen solo porque el `ImageStream` tenga un dígest nuevo bajo ese mismo
   tag (el `imagePullPolicy` por defecto de un tag que no es `latest` es
   `IfNotPresent`).
   Reutiliza el login de `podman` que ya hiciste en el lab01, no hace
   falta repetirlo.
   ```bash
   REGISTRY=default-route-openshift-image-registry.apps.workshop.bg.daytwodemo.com
   NS=$(oc project -q)          # tu namespace actual (<tu-usuario>)

   podman build -t $REGISTRY/$NS/taskflow-api:lab02 \
     -f app/Dockerfile app
   podman push $REGISTRY/$NS/taskflow-api:lab02
   ```

3. **Crear el Secret con las credenciales.** No lo commitees, genéralo
   directo contra el clúster:

   ```bash
   oc create secret generic taskflow-db-credentials \
     --from-literal=username=taskflow \
     --from-literal=password="$(openssl rand -base64 24)"
   ```

4. **Completar `manifests/configmap.yaml`** (tiene `# TODO`): host (nombre
   del `Service` de Postgres, `taskflow-db`), puerto, nombre de base, y la
   configuración de OpenTelemetry (`Otel__Exporter`, hoy `console`).

5. **Completar `manifests/postgres-deployment.yaml` y
   `postgres-service.yaml`** (tienen `# TODO`): Deployment de PostgreSQL
   usando `registry.redhat.io/rhel9/postgresql-16:latest`, leyendo
   usuario/password del Secret del paso 3. Almacenamiento efímero
   (`emptyDir`) alcanza para el alcance de este workshop, no es el foco
   del lab.

6. **Completar `manifests/deployment.yaml`** (reemplaza al del lab01): la
   imagen publicada en el paso 2 (tag `lab02`, no `lab01`), sin sidecar,
   con `envFrom`/`env` apuntando al `ConfigMap` y al `Secret`.

7. **Aplicar todo y validar:**
   ```bash
   oc apply -f labs/lab02-config/manifests/
   oc rollout status deployment/taskflow-api
   ```

8. **Verificar que la API es stateless.** Crear una tarea vía `POST
   /api/tasks`, borrar el pod de la API (`oc delete pod -l
   app=taskflow-api`), esperar a que el Deployment lo recree, y confirmar
   con `GET /api/tasks` que la tarea sigue ahí: el estado vive en
   Postgres, no en el pod de la API.

## Criterios de "hecho"

- [ ] `appsettings.json` no tiene ningún `ConnectionStrings` ni password.
- [ ] `taskflow-db-credentials` existe como Secret, nunca como archivo en
      el repo.
- [ ] El pod de `taskflow-api` ya no tiene contenedor sidecar de Postgres.
- [ ] Borrar el pod de la API y dejar que se recree no pierde datos.
- [ ] `/readyz` devuelve `200` (el chequeo de conexión a la base pasa).

## Pistas

- Si el pod de `taskflow-api` crashea con `Failed to connect to
  127.0.0.1:5432` (`Connection refused`), la imagen desplegada sigue
  siendo la del lab01 (connection string vieja hardcodeada): repetir el
  paso 2 con el tag `lab02` y confirmar que `manifests/deployment.yaml`
  apunta a ese tag, no a `lab01`.
- Si `/readyz` falla con `Connection refused` pero el error menciona el
  nombre `taskflow-db` (no `127.0.0.1`), confirmar que el nombre del
  `Service` de Postgres en el `ConfigMap` coincide exactamente con
  `metadata.name` de `postgres-service.yaml`.
- La convención de doble guion bajo (`Db__Host`) es la forma en que el
  proveedor de variables de entorno de .NET mapea a secciones anidadas de
  configuración (`Db:Host`), no es un capricho de este lab.
