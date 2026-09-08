@ECHO OFF
ECHO ============================================================================
ECHO Vous allez executer SCRIPT MANAGER LITE avec la configuration AgendisConfig/VS
ECHO ============================================================================

CD /D "%~dp0Lite"
ScriptManager.exe /csName AgendisEntities /sqlPath "../../../SQL/" /envCode "" /csFile "../../../AgendisConfig/VS/Database.config"

PAUSE
