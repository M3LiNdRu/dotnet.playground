# Use the official .NET 10 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY AsyncCallsApi/AsyncCallsApi.csproj AsyncCallsApi/
RUN dotnet restore AsyncCallsApi/AsyncCallsApi.csproj

# Copy the rest of the code
COPY AsyncCallsApi/ AsyncCallsApi/

# Build the application
WORKDIR /src/AsyncCallsApi
RUN dotnet build AsyncCallsApi.csproj -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish AsyncCallsApi.csproj -c Release -o /app/publish

# Use the official .NET 10 runtime image for running
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose port 5000
EXPOSE 5000

# Set environment variable to listen on all interfaces
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "AsyncCallsApi.dll"]
