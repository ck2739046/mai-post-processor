@echo off

set "current_dir=%~dp0"
set "target_exe=%current_dir%jacket_process\target\release\jacket_process.exe"

echo start build
echo.

REM 先删除旧的exe再构建
if exist "%target_exe%" (del "%target_exe%")
pushd jacket_process
cargo build --release

REM 复制exe到根目录
if exist "%target_exe%" (
    copy "%target_exe%" "%current_dir%"
) else (
    pause
)
