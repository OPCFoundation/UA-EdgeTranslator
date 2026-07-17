FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["UA-WoTMapper.csproj", "./"]
RUN dotnet restore "UA-WoTMapper.csproj"
COPY . .
RUN dotnet publish "UA-WoTMapper.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WotOpcUaMapper.dll"]
