FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Jellyfin.Plugin.Zelefin/Push/ApnsClient.cs Jellyfin.Plugin.Zelefin/Push/
COPY Jellyfin.Plugin.Zelefin/Api/PushMessage.cs Jellyfin.Plugin.Zelefin/Api/
COPY relay/ relay/
RUN dotnet publish relay/Zelefin.PushRelay.csproj --configuration Release --output /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Zelefin.PushRelay.dll"]
