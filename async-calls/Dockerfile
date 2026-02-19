# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY AsyncCallsDemo/AsyncCallsDemo.csproj AsyncCallsDemo/
RUN dotnet restore AsyncCallsDemo/AsyncCallsDemo.csproj

# Copy source code and build
COPY AsyncCallsDemo/ AsyncCallsDemo/
WORKDIR /src/AsyncCallsDemo
RUN dotnet build AsyncCallsDemo.csproj -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish AsyncCallsDemo.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

# Copy published app
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "AsyncCallsDemo.dll"]
