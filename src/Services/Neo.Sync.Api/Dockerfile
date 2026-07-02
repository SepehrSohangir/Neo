FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Services/Neo.Sync.Api/Neo.Sync.Api.csproj", "src/Services/Neo.Sync.Api/"]
COPY ["src/BuildingBlocks/Neo.Contracts/Neo.Contracts.csproj", "src/BuildingBlocks/Neo.Contracts/"]
COPY ["src/BuildingBlocks/Neo.Eventing/Neo.Eventing.csproj", "src/BuildingBlocks/Neo.Eventing/"]
RUN dotnet restore "src/Services/Neo.Sync.Api/Neo.Sync.Api.csproj"

COPY . .
RUN dotnet publish "src/Services/Neo.Sync.Api/Neo.Sync.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Neo.Sync.Api.dll"]
