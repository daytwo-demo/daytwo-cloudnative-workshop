var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hola desde un contenedor en OpenShift!");

app.Run();
