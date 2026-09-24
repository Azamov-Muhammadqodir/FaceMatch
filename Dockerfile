# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore in a separate layer so code changes do not re-download packages (the face models are ~250 MB).
COPY FaceMatch.slnx Directory.Build.props ./
COPY src/FaceMatch.Core/FaceMatch.Core.csproj src/FaceMatch.Core/
COPY src/FaceMatch.Infrastructure/FaceMatch.Infrastructure.csproj src/FaceMatch.Infrastructure/
COPY src/FaceMatch.Api/FaceMatch.Api.csproj src/FaceMatch.Api/
RUN dotnet restore src/FaceMatch.Api/FaceMatch.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/FaceMatch.Api/FaceMatch.Api.csproj -c Release -a $TARGETARCH --no-restore --self-contained false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "FaceMatch.Api.dll"]
