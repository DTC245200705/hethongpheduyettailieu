FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /workspace

COPY src/Shared.Contracts/Shared.Contracts.csproj src/Shared.Contracts/
COPY src/Document.Api/Document.Api.csproj src/Document.Api/
RUN dotnet restore src/Document.Api/Document.Api.csproj

COPY src/Shared.Contracts/ src/Shared.Contracts/
COPY src/Document.Api/ src/Document.Api/
RUN dotnet publish src/Document.Api/Document.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

EXPOSE 5001
ENV ASPNETCORE_URLS=http://+:5001
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Document.Api.dll"]
