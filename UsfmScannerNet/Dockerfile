FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["UsfmScannerNet/UsfmScannerNet.csproj", "UsfmScannerNet/"]
RUN dotnet restore "UsfmScannerNet/UsfmScannerNet.csproj"
COPY . .
WORKDIR "/src/UsfmScannerNet"
RUN dotnet build "./UsfmScannerNet.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Publish the application
RUN dotnet publish "./UsfmScannerNet.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
# Install CSnakes.Stage tool to get the Python redistributable (which includes libcsnakes_python.so)
RUN dotnet tool install --global CSnakes.Stage
ENV PATH="/root/.dotnet/tools:${PATH}"
# Setup Python redistributable - this downloads Python + libcsnakes_python.so
RUN setup-python --python 3.12 --venv /app/python-env --pip-requirements /src/UsfmScannerNet/requirements.txt --verbose

FROM base AS final
WORKDIR /app
ENV InContainer=true
ENV PythonHome=/app
# Copy the application
COPY --from=publish /app/publish .
# Copy Python virtual environment from build stage
COPY --from=publish /app/python-env /app/python-env
# Copy CSnakes redistributable to a location accessible to the app user
# CSnakes looks in ~/.config/CSnakes, so we copy to /home/app/.config/CSnakes
COPY --from=publish /root/.config/CSnakes /home/app/.config/CSnakes
# Set proper permissions  
RUN chown -R $APP_UID:$APP_UID /app /home/app/.config
USER $APP_UID
ENTRYPOINT ["dotnet", "UsfmScannerNet.dll"]
