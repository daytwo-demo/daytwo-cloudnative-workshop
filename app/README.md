# TaskFlow: app de referencia del workshop

API REST en ASP.NET Core (.NET 10 LTS) para gestión de tareas. Es el hilo
conductor de todo el workshop: cada laboratorio le agrega una dimensión
Cloud Native.

## Estado de este branch (`main`)

Este es el estado **inicial**, deliberadamente no cloud-native, con el que
arrancan los participantes:

- Connection string de PostgreSQL **hardcodeada** en `appsettings.json` y en
  `Program.cs` (`# TODO (lab02)`).
- `Dockerfile` es un placeholder que no compila, el lab01 escribe uno real.
- Sin liveness/readiness/startup probes en ningún manifiesto de Kubernetes
  (los endpoints `/healthz`, `/readyz`, `/startupz` ya existen en el código;
  falta cablearlos en un Deployment, eso es el lab05).
- Sin manifiestos de Deployment/Service/Route todavía (lab01).
- Corre como root por defecto al no tener imagen productiva (lab03).

El estado completo, con las ocho capas aplicadas, vive en el branch
`solution`: ver `labs/README.md` para el esquema de branches/tags.

## Endpoints

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/` | SPA mínima (`wwwroot/index.html`) para probar la API desde el browser |
| GET | `/api/tasks` | Lista todas las tareas |
| GET | `/api/tasks/{id}` | Obtiene una tarea |
| POST | `/api/tasks` | Crea una tarea |
| PUT | `/api/tasks/{id}` | Actualiza una tarea |
| DELETE | `/api/tasks/{id}` | Elimina una tarea |
| GET | `/healthz` | Liveness: el proceso responde |
| GET | `/readyz` | Readiness: incluye chequeo de conexión a PostgreSQL |
| GET | `/startupz` | Startup |
| GET | `/metrics` | Métricas en formato Prometheus |

## Correr localmente

### Dentro del workspace de Dev Spaces (el caso del workshop)

El `devfile.yaml` ya trae un contenedor de PostgreSQL corriendo todo el
tiempo, en el mismo Pod que la terminal (mismo namespace de red), con las
mismas credenciales que espera `appsettings.json` por defecto
(`taskflow` / `taskflow` / `TaskFlow!2024`). No hace falta levantar nada:

```bash
dotnet run --project TaskFlow.Api
```

Para probarla desde afuera del workspace (tu propio browser, no la
terminal), Dev Spaces expone automáticamente el endpoint declarado en el
`devfile.yaml` como una Route pública:
```bash
oc get route --no-headers -o custom-columns=HOST:.spec.host | grep taskflow-api
```
Siempre con `https://`: la Route usa TLS (terminación edge), y `http://`
redirige. Si el browser no respeta el redirect (algunos cachean un
intento fallido anterior), probar en una ventana de incógnito.

### En tu propia máquina, fuera del workshop

Requiere .NET 10 SDK y un PostgreSQL accesible en `localhost:5432` con
usuario/base `taskflow` (antes del lab02: connection string hardcodeada
en `TaskFlow.Api/appsettings.json`; desde el lab02: variables de entorno
`Db__Host`/`Db__Port`/`Db__Name`/`Db__Username`/`Db__Password`). Ajustar
según cuál de las dos tenga tu copia de la app:

```bash
podman run -d --name taskflow-db \
  -e POSTGRESQL_DATABASE=taskflow \
  -e POSTGRESQL_USER=taskflow \
  -e POSTGRESQL_PASSWORD='TaskFlow!2024' \
  -p 5432:5432 quay.io/sclorg/postgresql-16-c9s

# Con -d el comando anterior vuelve enseguida, antes de que Postgres esté
# listo para aceptar conexiones. Sin un restartPolicy que reintente solo,
# dotnet run puede crashear en el primer intento si arranca demasiado
# rápido.
until podman exec taskflow-db pg_isready -U taskflow >/dev/null 2>&1; do sleep 1; done

dotnet run --project TaskFlow.Api
```

La app escucha en `http://localhost:8080` (definido en
`Properties/launchSettings.json`, el mismo puerto que se expone en
contenedor).

## Stack

- ASP.NET Core 10 (minimal APIs) + EF Core / Npgsql
- Serilog con salida JSON (`Serilog.Formatting.Compact`)
- `prometheus-net.AspNetCore` para `/metrics`
- OpenTelemetry (traces + metrics) con exporter configurable vía
  `Otel:Exporter` (`console` / `otlp` / `none`)
