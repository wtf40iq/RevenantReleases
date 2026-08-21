# ===== Сборка =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY RevenantAuthServer.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

# ===== Рантайм =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Папка для SQLite-базы
RUN mkdir -p /app/data

ENV ASPNETCORE_ENVIRONMENT=Production
# Порт задаёт Render через переменную PORT (читается в Program.cs)
CMD ["dotnet", "RevenantAuthServer.dll"]
