# ===========================
# BUILD
# ===========================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1) Copia só o csproj p/ cache de restore
COPY IdeorAI.Api/IdeorAI.Api.csproj IdeorAI.Api/
RUN dotnet restore IdeorAI.Api/IdeorAI.Api.csproj

# 2) Copia o restante e publica
COPY . .
RUN dotnet publish IdeorAI.Api/IdeorAI.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ===========================
# RUNTIME
# ===========================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Render define PORT dinamicamente. Ligue o Kestrel nisso.
ENV PORT=8080
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT}
ENV ASPNETCORE_ENVIRONMENT=Production
# (opcional) melhora cold start e desativa diagnóstico em prod
ENV DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish ./
EXPOSE 8080

# Render usa o comando padrão; não sobrescreva com argumentos.
CMD ["dotnet", "IdeorAI.Api.dll"]
