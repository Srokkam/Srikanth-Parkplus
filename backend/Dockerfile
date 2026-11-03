# The API and the DbMaintenance tool, built from one context (backend/). Choose with --target api | dbmaintenance.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so the layer is cached until a dependency changes.
COPY nuget.config global.json ./
COPY IplFranchise.Domain/IplFranchise.Domain.csproj IplFranchise.Domain/
COPY IplFranchise.Application/IplFranchise.Application.csproj IplFranchise.Application/
COPY IplFranchise.Infrastructure/IplFranchise.Infrastructure.csproj IplFranchise.Infrastructure/
COPY IplFranchise.Api/IplFranchise.Api.csproj IplFranchise.Api/
COPY IplFranchise.DbMaintenance/IplFranchise.DbMaintenance.csproj IplFranchise.DbMaintenance/
RUN dotnet restore IplFranchise.Api/IplFranchise.Api.csproj \
 && dotnet restore IplFranchise.DbMaintenance/IplFranchise.DbMaintenance.csproj

COPY IplFranchise.Domain/ IplFranchise.Domain/
COPY IplFranchise.Application/ IplFranchise.Application/
COPY IplFranchise.Infrastructure/ IplFranchise.Infrastructure/
COPY IplFranchise.Api/ IplFranchise.Api/
COPY IplFranchise.DbMaintenance/ IplFranchise.DbMaintenance/
RUN dotnet publish IplFranchise.Api/IplFranchise.Api.csproj -c Release -o /out/api --no-restore \
 && dotnet publish IplFranchise.DbMaintenance/IplFranchise.DbMaintenance.csproj -c Release -o /out/dbmaintenance --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "IplFranchise.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS dbmaintenance
WORKDIR /app
COPY --from=build /out/dbmaintenance .
USER $APP_UID
ENTRYPOINT ["dotnet", "IplFranchise.DbMaintenance.dll"]
