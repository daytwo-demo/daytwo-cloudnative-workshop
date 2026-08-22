# Lab 01: Contenerizar TaskFlow

**Día:** lunes · **Branch de referencia:** `main` (`lab01-start`) → `lab01-solution`

## Objetivo

Escribir un Dockerfile básico para TaskFlow, construir la imagen,
publicarla en el registry interno de OpenShift, y desplegarla con
`Deployment` + `Service` + `Route`. Meta: la app corriendo en un pod,
accesible desde afuera del clúster.

## Contexto

TaskFlow (`app/TaskFlow.Api/`) todavía no tiene forma de correr en un
contenedor: su `Dockerfile` es solo un placeholder, y su connection string
a PostgreSQL está hardcodeada a `Host=localhost` en `appsettings.json`. Ese
`localhost` es del alumno hoy, y va a ser un problema en cuanto la app
corra en un Pod, no hay ningún PostgreSQL en `localhost` dentro de un
contenedor.

Para este lab, la solución **no** es arreglar la configuración (eso es el
lab02): es meter un contenedor de PostgreSQL como *sidecar* en el mismo
Pod, para que "localhost" siga siendo válido dentro del namespace de red
del Pod. Es una solución fea a propósito: el objetivo es que se sienta el
dolor de la config hardcodeada antes de resolverlo formalmente mañana.

## Conceptos

- **Qué hace `dotnet publish` (si nunca tocaste .NET)**: compila el
  código (`dotnet restore` baja las dependencias del `.csproj`,
  equivalente a un `npm install` o un `mvn install`) y arma una carpeta
  con el ensamblado (`TaskFlow.Api.dll`) más todo lo que necesita para
  correr.
- **Principios Cloud Native**: diseñar asumiendo que un pod puede morir en
  cualquier momento (la plataforma lo reemplaza, no lo repara), mantener
  el estado fuera del proceso, y declarar en manifiestos lo que la
  plataforma debe garantizar en vez de operarlo a mano.
- **Arquitectura basada en contenedores**: la unidad de despliegue es una
  imagen inmutable versionada, no un servidor que se actualiza en el
  lugar.
- **Responsabilidad de la plataforma vs. del desarrollador**: OpenShift
  garantiza que el contenedor declarado siga corriendo (scheduling, red,
  reinicios); el desarrollador garantiza que la app se comporte bien
  dentro de ese contrato (arranca sin depender de estado local, expone
  sus puertos, sabe reportar si está sana). El `Deployment` de este lab es
  la primera vez que se cruza esa línea explícitamente.
- **`Route` es una conveniencia de OpenShift**, no de Kubernetes: el
  objeto portable equivalente para exponer un `Service` hacia afuera del
  clúster es `Ingress`. Se usa `Route` acá porque es lo nativo del
  clúster del workshop.

## Pasos

> **Antes de empezar:** confirma que ya hiciste `oc login` como tu usuario
> (Paso 0 de `labs/README.md`) en esta terminal, `oc whoami` tiene que
> devolver tu usuario, no un `system:serviceaccount:...`. La sesión no
> persiste entre reinicios del workspace.

1. **Completar el Dockerfile de una sola etapa.** `app/Dockerfile` ya
   trae el `FROM`/`COPY`/`RUN` de compilación armado con la imagen del
   SDK completo (`dotnet publish` genera el ensamblado en
   `/app/publish`, ver "Conceptos" arriba si nunca tocaste .NET). Faltan
   dos valores marcados `TODO`:

   - `EXPOSE`: el puerto donde escucha Kestrel (por defecto en contenedores
     .NET 8+, sin necesidad de configurar `ASPNETCORE_URLS`).
   - `ENTRYPOINT`: el nombre del ensamblado publicado
     (`TaskFlow.Api.csproj` → `TaskFlow.Api.dll`).

   Construirla y correrla local, sin publicar nada todavía:
   ```bash
   podman build -t taskflow-api:una-etapa -f app/Dockerfile app
   podman images taskflow-api:una-etapa
   ```
   Anotar el tamaño. Probarla:
   ```bash
   podman run --rm -p 8080:8080 taskflow-api:una-etapa
   ```
   Esta vez sí va a fallar al conectar a Postgres (`EnsureCreated()` tira
   una excepción de conexión), a diferencia de cuando corriste
   `dotnet run --project TaskFlow.Api` directo en la terminal (Paso 0 de
   `labs/README.md`). La diferencia no es casualidad: `dotnet run`
   arranca un *proceso* dentro del mismo contenedor de la terminal, que
   comparte el namespace de red del Pod con el sidecar de Postgres del
   devfile, por eso `localhost:5432` resuelve. `podman run` en cambio
   crea un **contenedor nuevo, con su propio namespace de red aislado**:
   "localhost" ahí adentro es el loopback de ese contenedor recién
   creado, donde no hay ningún Postgres escuchando. Es la misma razón
   por la que el Deployment real necesita su propio sidecar de Postgres
   en el paso 4: cada Pod (o cada contenedor anidado) tiene que resolver
   "localhost" contra algo que esté corriendo ahí mismo. El problema de
   esta imagen no es ese, igual: es que arrastra el SDK de compilación
   completo (compiladores, herramientas de build) dentro de la imagen
   que se va a desplegar en producción, sin necesitarlo para correr.

2. **Convertirlo a multi-stage y comparar.** Reescribir `app/Dockerfile`
   con **dos** bloques `FROM`:

   - Un stage `build` (`FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build`)
     con el mismo `COPY`/`RUN dotnet publish` de arriba.
   - Un stage final, nuevo, arrancando de
     `FROM mcr.microsoft.com/dotnet/aspnet:10.0` (la imagen de *runtime*,
     sin el SDK), que trae el resultado del otro stage con
     `COPY --from=build /app/publish .`, una sola instrucción que copia
     el ensamblado ya compilado sin arrastrar el compilador.

   Reconstruir bajo otro tag y comparar:
   ```bash
   podman build -t taskflow-api:multi-stage -f app/Dockerfile app
   podman images | grep taskflow-api
   ```
   La diferencia de tamaño entre ambas filas **es** la razón de ser del
   multi-stage build: nada de lo que el compilador necesitó (el SDK
   entero) queda en la imagen que corre en producción, solo lo que la
   app necesita para ejecutar.

   No hace falta todavía usuario non-root, rootfs de solo lectura, ni
   resource limits, eso es lab03.

3. **Construir la imagen final y publicarla en el registry interno de
   OpenShift.** El registry interno tiene una ruta pública
   (`default-route-openshift-image-registry.apps.workshop.bg.daytwodemo.com`)
   habilitada por el instructor: te autenticas con tu propio token de
   OpenShift, igual que harías con cualquier registry externo.

   ```bash
   REGISTRY=default-route-openshift-image-registry.apps.workshop.bg.daytwodemo.com
   NS=$(oc project -q)          # tu namespace actual (<tu-usuario>)
   USUARIO=$(oc whoami)         # tu usuario de OpenShift
   TOKEN=$(oc whoami -t)        # tu token de autenticación, hace de password

   podman login -u $USUARIO -p $TOKEN $REGISTRY

   podman build -t $REGISTRY/$NS/taskflow-api:lab01 \
     -f app/Dockerfile app
   podman push $REGISTRY/$NS/taskflow-api:lab01
   ```

   El `push` crea automáticamente un `ImageStream taskflow-api` con el tag
   `lab01` en tu namespace: verifícalo con `oc get is taskflow-api`. Es el
   mismo flujo que usarías para pushear a cualquier registry (Docker Hub,
   Quay), no hace falta nada específico de OpenShift más que el login con
   tu token en vez de una password.

4. **Completar `manifests/deployment.yaml`** (tiene `# TODO`): referencia
   la imagen que acabas de publicar, y agrega un contenedor sidecar de
   PostgreSQL usando `registry.redhat.io/rhel9/postgresql-16:latest` (ya
   preparado para correr con cualquier UID, como exige la SCC
   `restricted-v2` por defecto de OpenShift; se descarga directo del
   registry de Red Hat, no hace falta ningún `ImageStream` ni pull secret
   adicional). Las variables `POSTGRESQL_DATABASE` / `POSTGRESQL_USER` /
   `POSTGRESQL_PASSWORD` del sidecar deben coincidir con lo hardcodeado en
   `appsettings.json` (`taskflow` / `taskflow` / `TaskFlow!2024`).

5. **Completar `manifests/service.yaml` y `manifests/route.yaml`** (tienen
   `# TODO`): `Service` ClusterIP apuntando al puerto 8080 del Deployment,
   `Route` edge apuntando al `Service`.

6. **Desplegar y validar:**

   ```bash
   oc apply -f labs/lab01-contenerizar/manifests/
   oc get pods -w
   curl "https://$(oc get route taskflow-api -o jsonpath='{.spec.host}')/api/tasks"
   ```

## Criterios de "hecho"

- [ ] `oc get is taskflow-api` muestra la imagen publicada.
- [ ] El pod queda en `Running` (no `CrashLoopBackOff`): sin esto, algo
      falló conectando el sidecar de Postgres.
- [ ] `GET /api/tasks` a través de la Route devuelve `200` con `[]`.
- [ ] Puedes explicar por qué hizo falta el sidecar de Postgres en este lab
      puntual (pista: `Host=localhost` en la connection string).

## Pistas

- Si el pod queda en `CrashLoopBackOff`, revisar los logs del contenedor
  `taskflow-api` (`oc logs <pod> -c taskflow-api`): el error más común es
  que el sidecar de Postgres todavía no estaba listo cuando la app intentó
  `EnsureCreated()`. Un `restartPolicy` normal de Kubernetes ya reintenta
  esto solo.
- El puerto por defecto de Kestrel en contenedores .NET 8+ es `8080`, no
  hace falta setear `ASPNETCORE_URLS` a mano.
