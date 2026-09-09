FROM ://microsoft.com AS build
WORKDIR /src

COPY ["SaturdayPulse.sln", "./"]
COPY ["SaturdayPulse.AdminBlazor/SaturdayPulse.AdminBlazor.csproj", "SaturdayPulse.AdminBlazor/"]
RUN dotnet restore "SaturdayPulse.AdminBlazor/SaturdayPulse.AdminBlazor.csproj"

COPY . .
WORKDIR "/src/SaturdayPulse.AdminBlazor"
RUN dotnet publish "SaturdayPulse.AdminBlazor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM ://microsoft.com AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SaturdayPulse.AdminBlazor.dll"]