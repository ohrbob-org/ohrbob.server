FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Ohrbob.Web/Ohrbob.Web.csproj", "Ohrbob.Web/"]
COPY ["Ohrbob.Server.Shared/Ohrbob.Server.Shared.csproj", "Ohrbob.Server.Shared/"]
RUN dotnet restore "Ohrbob.Web/Ohrbob.Web.csproj"
COPY . .
WORKDIR "/src/Ohrbob.Web"
RUN dotnet build "Ohrbob.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Ohrbob.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ARG GIT_COMMIT=dev-local
ARG RELEASE_TAG=beta
ENV AppVersion__Version=${GIT_COMMIT}
ENV AppVersion__Tag=${RELEASE_TAG}
COPY changelog.md .

# Create the DataProtection-Keys directory and ensure the non-root app user owns it.
# This prevents UnauthorizedAccessException when mounting the Docker volume.
RUN mkdir -p /app/DataProtection-Keys && chown -R $APP_UID /app/DataProtection-Keys

USER $APP_UID
ENTRYPOINT ["dotnet", "Ohrbob.Web.dll"]
