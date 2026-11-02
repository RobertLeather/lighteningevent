FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LighteningListener.csproj .
RUN dotnet restore LighteningListener.csproj
COPY . .
RUN dotnet publish LighteningListener.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "LighteningListener.dll"]
