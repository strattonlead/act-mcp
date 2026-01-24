# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["ACT/ACT.csproj", "ACT/"]
RUN dotnet restore "ACT/ACT.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/ACT"
RUN dotnet build "ACT.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "ACT.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final Stage
FROM createiflabs/aspnet:10.0-cran-r AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ACT.dll"]
