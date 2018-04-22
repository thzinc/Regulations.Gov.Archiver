FROM microsoft/dotnet:2.0-runtime as base
ARG version
ENTRYPOINT [ "dotnet", "Regulations.Gov.Downloader.dll" ]

FROM microsoft/dotnet:2.0-sdk as build
ARG version
WORKDIR /build
COPY ./Regulations.Gov.Downloader.csproj ./
RUN dotnet restore
COPY ./ ./
RUN dotnet publish -c Release -o /app /p:Version=${version}

FROM base as final
WORKDIR /app
COPY --from=build /app .