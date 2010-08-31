@echo off
SETLOCAL

if "%1" == "" goto USAGE
if /i "%1" == "/?" goto USAGE
if /i "%1" == "-?" goto USAGE
if /i "%1" == "/h" goto USAGE
if /i "%1" == "-h" goto USAGE

SET LANG=%1
SET TEMPLATES_DIR=%~dp0templates\%LANG%

if not exist %TEMPLATES_DIR% (
    echo Template not found: %TEMPLATES_DIR%
    echo.
    goto END 
)

if "%2"=="" (
    SET APP_PATH=%CD%\%LANG%_App
) else (
    SET APP_PATH=%2
)
if NOT "%APP_PATH:~-1%" == "\" SET APP_PATH=%APP_PATH%\

SET OVERWRITE=y
if exist %APP_PATH% set /p OVERWRITE=%APP_PATH% already exists. Do you want to overwrite? (Y/N)

if /i "%OVERWRITE%"=="y" (
    echo.
) else (
    echo.
    echo Sample was not created. 
    goto END
)

xcopy /s /y /r /q %TEMPLATES_DIR%\*.* %APP_PATH%
if "%ERRORLEVEL%"=="0" (
    echo Your %LANG% Silverlight application was created in %APP_PATH%.
) else (
    echo Failed. Please try again.
)

goto END

:USAGE
echo This is a tool to create a Silverlight application for dynamic languages.
echo.
echo Usage:
echo     sl [ruby^|python^|jscript] ^<ApplicationPath^>
echo.


:END
ENDLOCAL
