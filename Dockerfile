# ===========================
# BUILD
# ===========================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1) Copia só o csproj p/ cache de restore
COPY *.csproj ./
RUN dotnet restore

# 2) Copia o restante e publica
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ===========================
# RUNTIME
# ===========================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Kestrel deve escutar em todas as interfaces para aceitar requests do Prometheus/Docker
ENV PORT=8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish ./
EXPOSE 8080

# Render usa o comando padrão; não sobrescreva com argumentos.
CMD ["dotnet", "IdeorAI.Api.dll"]
