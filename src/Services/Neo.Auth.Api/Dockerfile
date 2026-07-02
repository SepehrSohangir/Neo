FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Services/Neo.Auth.Api/Neo.Auth.Api.csproj", "src/Services/Neo.Auth.Api/"]
COPY ["src/BuildingBlocks/Neo.Contracts/Neo.Contracts.csproj", "src/BuildingBlocks/Neo.Contracts/"]
RUN dotnet restore "src/Services/Neo.Auth.Api/Neo.Auth.Api.csproj"

COPY . .
RUN dotnet publish "src/Services/Neo.Auth.Api/Neo.Auth.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Neo.Auth.Api.dll"]
