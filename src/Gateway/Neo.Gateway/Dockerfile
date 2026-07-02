FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Gateway/Neo.Gateway/Neo.Gateway.csproj", "src/Gateway/Neo.Gateway/"]
COPY ["src/BuildingBlocks/Neo.Contracts/Neo.Contracts.csproj", "src/BuildingBlocks/Neo.Contracts/"]
RUN dotnet restore "src/Gateway/Neo.Gateway/Neo.Gateway.csproj"

COPY . .
RUN dotnet publish "src/Gateway/Neo.Gateway/Neo.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Neo.Gateway.dll"]
