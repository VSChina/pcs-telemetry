@ECHO off & setlocal enableextensions enabledelayedexpansion

:: Note: use lowercase names for the Docker images
SET DOCKER_IMAGE="vschinaiot/pcs-telemetry"
:: "testing" is the latest dev build, usually matching the code in the "master" branch
SET DOCKER_TAG=%DOCKER_IMAGE%:testing

docker login -u %DOCKER_USERNAME% -p %DOCKER_PASSWORD%

docker push %DOCKER_TAG%

endlocal
