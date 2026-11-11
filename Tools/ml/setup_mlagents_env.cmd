@echo off
rem Create the ML-Agents Python environment (Windows). See setup_mlagents_env.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup_mlagents_env.ps1" %*
exit /b %ERRORLEVEL%
