FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY whatsapp-bff.csproj .
RUN dotnet restore whatsapp-bff.csproj

COPY . .
RUN dotnet publish whatsapp-bff.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "whatsapp-bff.dll"]
