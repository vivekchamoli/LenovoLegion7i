@echo off
REM Disable Advanced Multi-Agent System Features
REM Removes environment variables to deactivate optimizations

echo ==========================================
echo Advanced Multi-Agent System - Feature Deactivation
echo Version: 6.2.0
echo ==========================================
echo.

echo Disabling Advanced Multi-Agent System features...
echo.

REM Disable Advanced Resource Orchestrator
setx LLT_FEATURE_ADVANCEDRESOURCEORCHESTRATOR "false"
echo [OK] Advanced Resource Orchestrator: DISABLED

REM Disable Thermal Agent
setx LLT_FEATURE_THERMALAGENT "false"
echo [OK] Thermal Agent: DISABLED

REM Disable Power Agent
setx LLT_FEATURE_POWERAGENT "false"
echo [OK] Power Agent: DISABLED

REM Disable GPU Agent
setx LLT_FEATURE_GPUAGENT "false"
echo [OK] GPU Agent: DISABLED

REM Disable Battery Agent
setx LLT_FEATURE_BATTERYAGENT "false"
echo [OK] Battery Agent: DISABLED

REM Disable ML/AI Controller
setx LLT_FEATURE_MLAICONTROLLER "false"
echo [OK] ML/AI Controller: DISABLED

REM Disable Adaptive Fan Curves
setx LLT_FEATURE_ADAPTIVEFANCURVES "false"
echo [OK] Adaptive Fan Curves: DISABLED

REM Disable Object Pooling
setx LLT_FEATURE_OBJECTPOOLING "false"
echo [OK] Object Pooling: DISABLED

REM Disable Reactive Sensors
setx LLT_FEATURE_REACTIVESENSORS "false"
echo [OK] Reactive Sensors: DISABLED

echo.
echo ==========================================
echo ADVANCED FEATURES DEACTIVATED
echo ==========================================
echo.

echo All Advanced Multi-Agent System features are now DISABLED.
echo.
echo IMPORTANT: Restart Lenovo Legion Toolkit to apply changes!
echo.
echo To re-enable features, run: enable_features.bat
echo.
pause
