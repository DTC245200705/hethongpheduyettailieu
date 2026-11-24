FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /workspace

COPY src/Shared.Contracts/Shared.Contracts.csproj src/Shared.Contracts/
COPY src/Approval.WebClient/Approval.WebClient.csproj src/Approval.WebClient/
RUN dotnet restore src/Approval.WebClient/Approval.WebClient.csproj

COPY src/Shared.Contracts/ src/Shared.Contracts/
COPY src/Approval.WebClient/ src/Approval.WebClient/
RUN dotnet publish src/Approval.WebClient/Approval.WebClient.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Approval.WebClient.dll"]
