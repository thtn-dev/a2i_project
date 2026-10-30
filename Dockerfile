# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Whole src/ is copied so a new module needs no Dockerfile change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Host/Host.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Built-in non-root user of the official .NET images.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Exec form: the process is PID 1 and receives SIGTERM for graceful shutdown.
# `docker run <image> migrate` runs the migrations and exits.
ENTRYPOINT ["dotnet", "Host.dll"]
