FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CarManualAssistant.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 5080
ENTRYPOINT ["dotnet", "CarManualAssistant.Api.dll"]
