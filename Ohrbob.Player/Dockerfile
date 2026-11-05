FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Ohrbob.Player/Ohrbob.Player.csproj", "Ohrbob.Player/"]
COPY ["Ohrbob.Server.Shared/Ohrbob.Server.Shared.csproj", "Ohrbob.Server.Shared/"]
RUN dotnet restore "Ohrbob.Player/Ohrbob.Player.csproj"
COPY . .
WORKDIR "/src/Ohrbob.Player"
RUN dotnet build "Ohrbob.Player.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Ohrbob.Player.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ARG GIT_COMMIT=dev-local
ARG RELEASE_TAG=beta
ENV AppVersion__Version=${GIT_COMMIT}
ENV AppVersion__Tag=${RELEASE_TAG}
USER $APP_UID
ENTRYPOINT ["dotnet", "Ohrbob.Player.dll"]
