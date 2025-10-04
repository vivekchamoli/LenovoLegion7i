@echo off
REM Restore multi-agent files from temp_disabled folder

echo Restoring multi-agent files...

move temp_disabled\ResourceOrchestrator.cs LenovoLegionToolkit.Lib\AI\ 2>nul
move temp_disabled\SystemContext.cs LenovoLegionToolkit.Lib\AI\ 2>nul
move temp_disabled\SystemContextStore.cs LenovoLegionToolkit.Lib\AI\ 2>nul
move temp_disabled\WorkloadClassifier.cs LenovoLegionToolkit.Lib\AI\ 2>nul
move temp_disabled\ThermalAgent.cs LenovoLegionToolkit.Lib\AI\ 2>nul
move temp_disabled\PowerAgent.cs LenovoLegionToolkit.Lib\AI\ 2>nul
move temp_disabled\GPUAgent.cs LenovoLegionToolkit.Lib\AI\ 2>nul
move temp_disabled\DecisionArbitrationEngine.cs LenovoLegionToolkit.Lib\AI\ 2>nul
move temp_disabled\IOptimizationAgent.cs LenovoLegionToolkit.Lib\AI\ 2>nul
move temp_disabled\OrchestratorIntegration.cs LenovoLegionToolkit.Lib\AI\ 2>nul

rmdir temp_disabled 2>nul

echo Files restored to LenovoLegionToolkit.Lib\AI\
echo.
pause
