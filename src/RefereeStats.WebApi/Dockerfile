FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/RefereeStats.WebApi -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
# Crear el directorio para el caché donde montaremos el Volume de Railway
RUN mkdir -p /app/data/cache
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
EXPOSE 8080
ENTRYPOINT ["dotnet", "RefereeStats.WebApi.dll"]
