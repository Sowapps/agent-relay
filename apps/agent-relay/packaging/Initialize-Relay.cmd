@echo off
"%~dp0app\AgentRelay.Windows.exe" initialize --data-dir "%~dp0var\apps\agent-relay\default" %*
