@echo off
python -B "%~dp0tools\updater\complete_update.py" %*
exit /b %ERRORLEVEL%
