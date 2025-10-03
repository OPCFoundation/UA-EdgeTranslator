#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 4840
EXPOSE 5000
EXPOSE 5001
EXPOSE 19520
EXPOSE 19521

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["UAEdgeTranslator.csproj", "."]
RUN dotnet restore "./UAEdgeTranslator.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "UAEdgeTranslator.csproj" -c Release -o /app/build

FROM build AS publish
ARG TARGET_FRAMEWORK=net9.0
RUN dotnet publish "UAEdgeTranslator.csproj" -c Release -o /app/publish -f $TARGET_FRAMEWORK /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UAEdgeTranslator.dll"]