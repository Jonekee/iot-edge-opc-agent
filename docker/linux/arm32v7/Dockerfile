ARG runtime_base_tag=2.1-runtime-bionic-arm32v7
ARG build_base_tag=2.1-sdk-bionic-arm32v7

FROM microsoft/dotnet:${build_base_tag} AS build
WORKDIR /app

# copy csproj and restore as distinct layers
COPY opcagent/*.csproj ./opcagent/
WORKDIR /app/opcagent
RUN dotnet restore

# copy and publish app
WORKDIR /app
COPY opcagent/. ./opcagent/
WORKDIR /app/opcagent
RUN dotnet publish -c Release -o out

# start it up
FROM microsoft/dotnet:${runtime_base_tag} AS runtime
WORKDIR /app
COPY --from=build /app/opcagent/out ./
WORKDIR /appdata
ENTRYPOINT ["dotnet", "/app/opcagent.dll"]