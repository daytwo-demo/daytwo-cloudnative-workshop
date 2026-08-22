var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => $"Hola, corro dentro de un contenedor .NET con hostname {Environment.MachineName}.");

app.Run();
