@echo off
REM Temporary disable multi-agent files to allow build
REM This allows the main application to build while we fix integration issues

echo Temporarily moving multi-agent files...

mkdir temp_disabled 2>nul

move LenovoLegionToolkit.Lib\AI\ResourceOrchestrator.cs temp_disabled\ 2>nul
move LenovoLegionToolkit.Lib\AI\SystemContext.cs temp_disabled\ 2>nul
move LenovoLegionToolkit.Lib\AI\SystemContextStore.cs temp_disabled\ 2>nul
move LenovoLegionToolkit.Lib\AI\WorkloadClassifier.cs temp_disabled\ 2>nul
move LenovoLegionToolkit.Lib\AI\ThermalAgent.cs temp_disabled\ 2>nul
move LenovoLegionToolkit.Lib\AI\PowerAgent.cs temp_disabled\ 2>nul
move LenovoLegionToolkit.Lib\AI\GPUAgent.cs temp_disabled\ 2>nul
move LenovoLegionToolkit.Lib\AI\DecisionArbitrationEngine.cs temp_disabled\ 2>nul
move LenovoLegionToolkit.Lib\AI\IOptimizationAgent.cs temp_disabled\ 2>nul
move LenovoLegionToolkit.Lib\AI\OrchestratorIntegration.cs temp_disabled\ 2>nul

echo Files moved to temp_disabled\
echo You can now build the solution.
echo.
echo To restore: run temp_restore_multi_agent.bat
pause
