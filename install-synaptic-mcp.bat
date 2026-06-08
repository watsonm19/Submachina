@echo off
REM ============================================================================
REM  Installs npm dependencies for the Synaptic AI Pro MCP Server.
REM
REM  The MCPServer/node_modules folder (13,000+ files) is git-ignored because
REM  it's bulky and not part of the Unity build. Run this once after cloning
REM  to restore the dependencies the MCP server needs to run.
REM ============================================================================

REM Move to the MCP server directory that contains package.json
pushd "%~dp0Assets\Synaptic AI Pro\MCPServer"

REM Install dependencies declared in package.json
echo Installing Synaptic AI Pro MCP Server dependencies...
call npm install

REM Restore the original working directory and report the result
popd
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo npm install FAILED. Make sure Node.js / npm is installed and on your PATH.
) else (
    echo.
    echo Done. Synaptic AI Pro MCP Server dependencies installed.
)
pause