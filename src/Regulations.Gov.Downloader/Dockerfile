FROM microsoft/dotnet:2.0-runtime as base
ARG version
ENTRYPOINT [ "dotnet", "Regulations.Gov.Downloader.dll" ]

FROM microsoft/dotnet:2.0-sdk as build
ARG version
WORKDIR /build
COPY ./src/Regulations.Gov.Downloader/Regulations.Gov.Downloader.csproj ./src/Regulations.Gov.Downloader/
COPY ./src/Google/Google.csproj ./src/Google/
RUN dotnet new sln && \
    dotnet sln add ./src/Regulations.Gov.Downloader/Regulations.Gov.Downloader.csproj && \
    dotnet sln add ./src/Google/Google.csproj && \
    dotnet restore
COPY ./src/Regulations.Gov.Downloader/ ./src/Regulations.Gov.Downloader/
COPY ./src/Google/ ./src/Google/
RUN dotnet publish -c Release -o /app /p:Version=${version}

FROM base as final
WORKDIR /app
COPY --from=build /app .