@echo off
rem NN training tools (Windows): build, record, train. See Tools\ml\nntools.py or "Tools\ml\nntools.cmd --help".
rem Uses the ML-Agents virtual environment (Tools\ml\setup_mlagents_env.cmd); set VENV=... to use another one.
setlocal
set "PROJECT=%~dp0..\.."
if "%VENV%"=="" set "VENV=%PROJECT%\.venv-mlagents"
if not exist "%VENV%\Scripts\python.exe" (
  echo ML-Agents environment not found at %VENV%. Run Tools\ml\setup_mlagents_env.cmd first. 1>&2
  exit /b 1
)
"%VENV%\Scripts\python.exe" "%PROJECT%\Tools\ml\nntools.py" %*
exit /b %ERRORLEVEL%
