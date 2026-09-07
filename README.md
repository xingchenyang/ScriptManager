# scriptmanager
script manager is a tool that allows differential script execution on a sql server database

USAGE: ScriptManager.exe /csName connectionStringName [/sqlPath pathToSqlDirectory] [/envCode forcedEnvCode] [/csFile pathToConfFile] [/disableScriptDiff 1] [/version versionString]
ex: ScriptManager.exe /csName "MyCsName" /sqlPath "../../SQL/" /envCode "RCT" /csFile "../../Database.Config" /version "3.1.0#85"
PARAMETERS:
 - sqlPath: path of scripts folder (default is ./SQL)
 - envCode: environment code: scripts whose file name contains =envCode= will be executed (default empty)
 - csFile: path to connection strings file (defaut ./Config/Database.config)
 - disableScriptDiff: when set to 1, run all the scripts even those already passed previously (default 0)
 - version: label of version, will update row in system table SYS.EXTENDED_PROPERTIES whose name is 'VERSION' (not updated if not provided)
 - csName: name of connection string to be used (mandatory)

TODO 
 - write a better readme
 - make some sample

 
 # scriptrunner
 scriptrunner is a binary that simply run script on a database
 
