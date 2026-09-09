FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["SaturdayPulse.sln", "./"]
COPY ["SaturdayPulse.Api/SaturdayPulse.Api.csproj", "SaturdayPulse.Api/"]
RUN dotnet restore "SaturdayPulse.Api/SaturdayPulse.Api.csproj"

COPY . .
WORKDIR "/src/SaturdayPulse.Api"
RUN dotnet publish "SaturdayPulse.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080

RUN mkdir -p /app/data

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SaturdayPulse.Api.dll"]