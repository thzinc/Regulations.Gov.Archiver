FROM microsoft/dotnet:2.0-runtime as base
ARG version
ENTRYPOINT [ "dotnet", "Regulations.Gov.Archiver.dll" ]

FROM microsoft/dotnet:2.0-sdk as build
ARG version
WORKDIR /build
COPY ./src/Regulations.Gov.Archiver/Regulations.Gov.Archiver.csproj ./
RUN dotnet restore
COPY ./src/Regulations.Gov.Archiver ./
RUN dotnet publish -c Release -o /app /p:Version=${version}

FROM base as final
WORKDIR /app
COPY --from=build /app .