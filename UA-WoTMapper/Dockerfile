FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the repo-root MSBuild infrastructure that NuGet restore needs under
# central package management: Directory.Packages.props pins every version,
# Directory.Build.props enforces shared policy (TreatWarningsAsErrors etc.),
# and global.json pins the .NET SDK channel.
COPY ["Directory.Packages.props", "Directory.Build.props", "global.json", "./"]
COPY ["UA-WoTMapper/UA-WoTMapper.csproj", "UA-WoTMapper/"]
RUN dotnet restore "UA-WoTMapper/UA-WoTMapper.csproj"
COPY . .
WORKDIR "/src/UA-WoTMapper"
RUN dotnet publish "UA-WoTMapper.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WotOpcUaMapper.dll"]
