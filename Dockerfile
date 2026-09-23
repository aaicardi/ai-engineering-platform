# Stage 1: Base de ejecución ligera
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

# Stage 2: Compilación de la aplicación
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY . .
# En fases posteriores aquí se compilarán los servicios .NET / Web APIs

# Stage 3: Imagen final
FROM base AS final
WORKDIR /app
COPY --from=build /src .
ENTRYPOINT ["echo", "AI Engineering Platform - Container Ready"]
