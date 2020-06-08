FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY *.csproj .
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet clean
RUN dotnet publish -c Release -o out

# Build runtime image
FROM ubuntu:20.04
# Depts. Openssl for dotnet core. CA for cosmos to validate certs. lib fonts for skia sharp
RUN apt update && apt install -y openssl ca-certificates libfontconfig1 
WORKDIR /app
COPY --from=build-env /app/out/phetracker .
COPY --from=build-env /app/populationData.csv .
ENTRYPOINT ["./phetracker"]