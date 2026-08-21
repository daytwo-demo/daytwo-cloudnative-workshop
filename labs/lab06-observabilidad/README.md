# Lab 06: Observabilidad

**Día:** jueves · **Branch de referencia:** `lab06-start` → `lab06-solution`

## Objetivo

Verificar los logs estructurados y el endpoint `/metrics` que TaskFlow ya
expone, conectar ese endpoint al stack de Prometheus del clúster con un
`ServiceMonitor`, visualizar las métricas resultantes en la consola de
OpenShift, y ver el tracing distribuido de OpenTelemetry a nivel
conceptual/demo.

## Prerequisito (ya resuelto por el instructor)

El monitoreo de proyectos de usuario (*user workload monitoring*) debe
estar habilitado a nivel de clúster para que OpenShift scrapee
`ServiceMonitor`s fuera de los namespaces de la plataforma. Esto ya está
habilitado en el clúster del workshop (paso de post-instalación a cargo
del instructor), no hace falta tocarlo, pero es importante entender que
sin este flag, un `ServiceMonitor` en tu namespace simplemente no se
scrapea.

## Pasos

> **Antes de empezar:** confirma que ya hiciste `oc login` como tu usuario
> (Paso 0 de `labs/README.md`) en esta terminal, `oc whoami` tiene que
> devolver tu usuario, no un `system:serviceaccount:...`. La sesión no
> persiste entre reinicios del workspace.

1. **Confirmar los logs estructurados:**
   ```bash
   oc logs deploy/taskflow-api --tail=20 | grep '^{' | jq .
   ```
   Cada línea de Serilog es un objeto JSON (`CompactJsonFormatter`): esto
   es lo que un backend de logging (Loki, Elastic, Splunk) espera para
   indexar por campo en vez de por texto libre. El `grep '^{'` descarta
   las líneas del exporter de consola de OpenTelemetry (`Otel__Exporter:
   console`, ver paso 6): comparten el mismo stdout del contenedor pero no
   son JSON, y romperían `jq` si se les pasa tal cual.

2. **Confirmar `/metrics`.** La imagen de runtime (`aspnet:10.0`) no trae
   `curl` ni `wget` instalado: probarlo desde afuera, vía la Route:
   ```bash
   curl -sk "https://$(oc get route taskflow-api -o jsonpath='{.spec.host}')/metrics" | head -30
   ```
   Buscar métricas con prefijo `http_request_duration_seconds` (expuestas
   automáticamente por `UseHttpMetrics()`).

3. **Completar `manifests/servicemonitor.yaml`** (tiene `# TODO`):
   selector por el label del `Service` de `taskflow-api`, puerto `http`,
   path `/metrics`.

4. **Aplicar y confirmar que Prometheus lo está scrapeando:**
   ```bash
   oc apply -f labs/lab06-observabilidad/manifests/servicemonitor.yaml
   ```
   En la consola: **Observe → Targets** (si tienes acceso de administrador
   de proyecto) o **Observe → Metrics** directamente.

5. **Generar tráfico y graficar en la consola** (**Observe → Metrics**,
   pestaña del namespace):
   ```bash
   for i in $(seq 1 50); do
     curl -s "https://$(oc get route taskflow-api -o jsonpath='{.spec.host}')/api/tasks" > /dev/null
   done
   ```
   Correr una query PromQL simple, ej.
   `rate(http_request_duration_seconds_count[5m])`, y ver el tráfico
   generado reflejado en el gráfico.

6. **Tracing distribuido (demo conceptual).** TaskFlow ya trae
   instrumentación de OpenTelemetry cableada
   (`AddAspNetCoreInstrumentation`/`AddHttpClientInstrumentation`) con el
   exporter elegido por `Otel__Exporter` en el `ConfigMap`. Con el valor
   por defecto (`console`), cada request genera un span que se imprime en
   el log del pod:
   ```bash
   oc logs deploy/taskflow-api | grep -A5 "Activity.TraceId"
   ```
   Esto es **a nivel demo**: no se despliega un backend de tracing
   (Jaeger/Tempo) como parte de este workshop. El punto es entender que la
   instrumentación ya existe en el código: conectarla a un backend real
   es cambiar `Otel__Exporter` a `otlp` y apuntar `Otel__OtlpEndpoint` a un
   OpenTelemetry Collector, sin tocar una línea de C#.

## Criterios de "hecho"

- [ ] Los logs del pod son JSON válido, línea por línea.
- [ ] El `ServiceMonitor` existe y Prometheus lo scrapea (aparece en
      **Observe → Targets** o el namespace tiene datos en
      **Observe → Metrics**).
- [ ] Se generó tráfico y se vio reflejado en un gráfico de la consola.
- [ ] Se entiende qué cambiaría (`Otel__Exporter`) para pasar de tracing
      demo a tracing real contra un backend.

## Pistas

- Si no aparecen métricas, el problema más común es un mismatch entre el
  `port` del `ServiceMonitor` (nombre del puerto, no número) y el nombre
  definido en `service.yaml`.
- `oc get servicemonitor -o yaml` y confirmar que el `namespaceSelector`
  no está excluyendo tu propio namespace por accidente.
