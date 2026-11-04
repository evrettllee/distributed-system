FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Shared/Shared.csproj Shared/
COPY Server/Server.csproj Server/
RUN dotnet restore Server/Server.csproj

COPY Shared/ Shared/
COPY Server/ Server/
RUN dotnet publish Server/Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV Server__Port=8080
ENV TZ=Europe/Minsk

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Server.dll"]