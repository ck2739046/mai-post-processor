if exist "bin\Release\net48\Better_JacketAsMovie.dll" (
    del "bin\Release\net48\Better_JacketAsMovie.dll"
)
if exist "Better_JacketAsMovie.dll" (
    del "Better_JacketAsMovie.dll"
)

dotnet build -c Release

if exist "bin\Release\net48\Better_JacketAsMovie.dll" (
    copy "bin\Release\net48\Better_JacketAsMovie.dll" "."
) else (
    pause
)

REM 复制dll到游戏
if exist "Better_JacketAsMovie.dll" (
    copy "Better_JacketAsMovie.dll" "C:\maimai\155\Package\Mods"
    exit
) else (
    echo.
    powershell write-host "Error: Better_JacketAsMovie.dll not found" -ForegroundColor Red
    pause
)