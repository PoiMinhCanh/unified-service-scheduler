# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ServiceScheduler.Api/ src/ServiceScheduler.Api/
RUN dotnet restore src/ServiceScheduler.Api/ServiceScheduler.Api.csproj
RUN dotnet publish src/ServiceScheduler.Api/ServiceScheduler.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ServiceScheduler.Api.dll"]

FROM runtime AS setup
CMD ["--db-setup"]
