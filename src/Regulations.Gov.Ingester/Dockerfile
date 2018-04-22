FROM microsoft/dotnet:2.0-runtime as base
ARG version
ENTRYPOINT [ "dotnet", "Regulations.Gov.Ingester.dll" ]

FROM microsoft/dotnet:2.0-sdk as build
ARG version
WORKDIR /build
COPY ./src/Regulations.Gov.Ingester/Regulations.Gov.Ingester.csproj ./
RUN dotnet restore
COPY ./src/Regulations.Gov.Ingester ./
RUN dotnet publish -c Release -o /app /p:Version=${version}

FROM base as final
WORKDIR /app
COPY --from=build /app .