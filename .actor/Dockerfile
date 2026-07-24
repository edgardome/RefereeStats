FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/RefereeStats.ApifyActor -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN mkdir -p /app/exports
COPY --from=build /app .
ENTRYPOINT ["dotnet", "RefereeStats.ApifyActor.dll"]
