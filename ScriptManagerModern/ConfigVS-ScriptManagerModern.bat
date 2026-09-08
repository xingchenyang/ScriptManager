@ECHO OFF
ECHO ============================================================================
ECHO Vous allez executer SCRIPT MANAGER MODERN avec la configuration AgendisConfig/VS
ECHO ============================================================================

CD /D "%~dp0Modern"
ScriptManager.exe /csName AgendisEntities /sqlPath "../../../SQL/" /envCode "" /csFile "../../../AgendisConfig/VS/Database.config"

PAUSE
