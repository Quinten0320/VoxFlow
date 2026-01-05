FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AiCallAssistent.API.csproj", "./"]
RUN dotnet restore "AiCallAssistent.API.csproj"

COPY . .
RUN dotnet publish "AiCallAssistent.API.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AiCallAssistent.API.dll"]
