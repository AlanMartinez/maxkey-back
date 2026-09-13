# Multi-stage build (design section 12): restore/publish on the SDK image, run on
# the smaller ASP.NET runtime image as a non-root user, listening on port 8080.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props Maxkeys.sln ./
COPY src/Maxkeys.Domain/Maxkeys.Domain.csproj src/Maxkeys.Domain/
COPY src/Maxkeys.Application/Maxkeys.Application.csproj src/Maxkeys.Application/
COPY src/Maxkeys.Infrastructure/Maxkeys.Infrastructure.csproj src/Maxkeys.Infrastructure/
COPY src/Maxkeys.Api/Maxkeys.Api.csproj src/Maxkeys.Api/
RUN dotnet restore src/Maxkeys.Api/Maxkeys.Api.csproj

COPY src/ src/
RUN dotnet publish src/Maxkeys.Api/Maxkeys.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Maxkeys.Api.dll"]
