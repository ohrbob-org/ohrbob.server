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
# This covers fresh volumes; existing volumes keep their old ownership, so the
# entrypoint repairs that at startup before dropping privileges.
RUN mkdir -p /app/DataProtection-Keys && chown -R $APP_UID /app/DataProtection-Keys

COPY Ohrbob.Web/docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

# Start as root so the entrypoint can fix volume ownership, then it execs the
# app as the non-root app user via setpriv.
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh", "dotnet", "Ohrbob.Web.dll"]
