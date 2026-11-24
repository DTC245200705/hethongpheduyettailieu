FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /workspace

COPY src/Shared.Contracts/Shared.Contracts.csproj src/Shared.Contracts/
COPY src/Notification.Service/Notification.Service.csproj src/Notification.Service/
RUN dotnet restore src/Notification.Service/Notification.Service.csproj

COPY src/Shared.Contracts/ src/Shared.Contracts/
COPY src/Notification.Service/ src/Notification.Service/
RUN dotnet publish src/Notification.Service/Notification.Service.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

EXPOSE 5002
ENV ASPNETCORE_URLS=http://+:5002
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Notification.Service.dll"]
