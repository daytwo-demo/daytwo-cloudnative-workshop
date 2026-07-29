using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Serilog.Formatting.Compact;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter()));

    // SOLUCIÓN (lab02): connection string construida desde configuración
    // (ConfigMap para host/puerto/nombre, Secret para usuario/password) en
    // vez de un valor hardcodeado.
    var connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = builder.Configuration["Db:Host"] ?? "localhost",
        Port = int.Parse(builder.Configuration["Db:Port"] ?? "5432"),
        Database = builder.Configuration["Db:Name"] ?? "taskflow",
        Username = builder.Configuration["Db:Username"] ?? "taskflow",
        Password = builder.Configuration["Db:Password"] ?? ""
    }.ConnectionString;

    builder.Services.AddDbContext<TaskFlowDbContext>(options =>
        options.UseNpgsql(connectionString));

    builder.Services.AddHealthChecks()
        // "ready" verifica conexión real a la base: si Postgres no responde,
        // el pod deja de recibir tráfico sin reiniciarse.
        .AddNpgSql(connectionString, name: "postgres", tags: ["ready"])
        // "live"/"startup" son checks de proceso: si el runtime .NET responde,
        // el proceso está vivo. No dependen de la base a propósito.
        .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "startup"]);

    var otelExporter = builder.Configuration["Otel:Exporter"] ?? "none";
    var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"] ?? "http://localhost:4317";

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(
            serviceName: "taskflow-api",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"))
        .WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation();
            tracing.AddHttpClientInstrumentation();
            ConfigureExporter(tracing, otelExporter, otlpEndpoint);
        })
        .WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation();
            metrics.AddRuntimeInstrumentation();
        });

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // SPA mínima (wwwroot/index.html) para probar /api/tasks desde el
    // browser sin instalar nada: same-origin, sin configurar CORS.
    app.UseDefaultFiles();
    app.UseStaticFiles();

    // Métricas HTTP automáticas (latencia, status code, etc.) + endpoint /metrics
    // en formato Prometheus. Cableado desde el día 1; lab06 agrega el
    // ServiceMonitor que hace que OpenShift lo scrapee.
    app.UseHttpMetrics();
    app.MapMetrics("/metrics");

    app.MapHealthChecks("/healthz", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live")
    });
    app.MapHealthChecks("/readyz", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });
    app.MapHealthChecks("/startupz", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("startup")
    });

    var tasks = app.MapGroup("/api/tasks");

    tasks.MapGet("", async (TaskFlowDbContext db) =>
        Results.Ok(await db.Tasks.OrderBy(t => t.CreatedAt).ToListAsync()));

    tasks.MapGet("/{id:guid}", async (Guid id, TaskFlowDbContext db) =>
        await db.Tasks.FindAsync(id) is { } task ? Results.Ok(task) : Results.NotFound());

    tasks.MapPost("", async (TaskItem input, TaskFlowDbContext db) =>
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = input.Title,
            Description = input.Description,
            IsDone = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return Results.Created($"/api/tasks/{task.Id}", task);
    });

    tasks.MapPut("/{id:guid}", async (Guid id, TaskItem input, TaskFlowDbContext db) =>
    {
        var task = await db.Tasks.FindAsync(id);
        if (task is null) return Results.NotFound();

        task.Title = input.Title;
        task.Description = input.Description;
        task.IsDone = input.IsDone;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(task);
    });

    tasks.MapDelete("/{id:guid}", async (Guid id, TaskFlowDbContext db) =>
    {
        var task = await db.Tasks.FindAsync(id);
        if (task is null) return Results.NotFound();

        db.Tasks.Remove(task);
        await db.SaveChangesAsync();
        return Results.NoContent();
    });

    // Sin migraciones EF Core a propósito: EnsureCreated() alcanza para el
    // alcance del workshop (el esquema no evoluciona lab a lab).
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<TaskFlowDbContext>();
        db.Database.EnsureCreated();
    }

    Log.Information("TaskFlow API iniciando");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "TaskFlow API terminó inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}

// Exporter configurable por appsettings/env (Otel:Exporter): "console",
// "otlp" o "none". En el lab06 se usa a nivel conceptual/demo, sin exigir
// un backend de tracing real corriendo en el clúster.
static void ConfigureExporter(TracerProviderBuilder tracing, string exporter, string otlpEndpoint)
{
    switch (exporter.ToLowerInvariant())
    {
        case "otlp":
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            break;
        case "console":
            tracing.AddConsoleExporter();
            break;
        default:
            break;
    }
}
