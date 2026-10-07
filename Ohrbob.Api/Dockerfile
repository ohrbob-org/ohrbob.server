FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Ohrbob.Api/Ohrbob.Api.csproj", "Ohrbob.Api/"]
COPY ["Ohrbob.Server.Shared/Ohrbob.Server.Shared.csproj", "Ohrbob.Server.Shared/"]
RUN dotnet restore "Ohrbob.Api/Ohrbob.Api.csproj"
COPY . .
WORKDIR "/src/Ohrbob.Api"
RUN dotnet build "Ohrbob.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Ohrbob.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ARG GIT_COMMIT=dev-local
ARG RELEASE_TAG=beta
ENV ApiServerConfiguration__Version=${GIT_COMMIT}
ENV ApiServerConfiguration__Tag=${RELEASE_TAG}
ENTRYPOINT ["dotnet", "Ohrbob.Api.dll"]
