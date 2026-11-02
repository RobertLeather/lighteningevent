FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LightningGenerator.csproj .
RUN dotnet restore LightningGenerator.csproj
COPY . .
RUN dotnet publish LightningGenerator.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "LightningGenerator.dll"]
