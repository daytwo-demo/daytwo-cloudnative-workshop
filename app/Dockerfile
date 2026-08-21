# TODO (lab01, paso 1): Dockerfile de una sola etapa. Compila y corre
# con la misma imagen (el SDK completo), a propósito: no es la versión
# productiva, es para sentir el problema antes de resolverlo en el
# paso 2. Ver labs/lab01-contenerizar/README.md.
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
COPY TaskFlow.Api/*.csproj .
RUN dotnet restore
COPY TaskFlow.Api/. .
RUN dotnet publish -c Release -o /app/publish --no-restore
WORKDIR /app/publish

# TODO: puerto donde escucha Kestrel (por defecto en contenedores .NET 8+,
# sin necesidad de configurar ASPNETCORE_URLS).
EXPOSE TODO
# TODO: nombre del ensamblado publicado (TaskFlow.Api.csproj -> TaskFlow.Api.dll).
ENTRYPOINT ["dotnet", "TODO"]
