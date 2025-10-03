@echo off
REM Legion Toolkit Clean Script v6.0.0-elite-phase4
REM Removes all build artifacts, temporary files, and caches

echo ==========================================
echo Legion Toolkit Clean Script
echo Version: 6.0.0-elite-phase4
echo ==========================================
echo.

echo Cleaning IDE and cache directories...
rmdir /s /q .vs 2>nul
rmdir /s /q _ReSharper.Caches 2>nul

echo Cleaning build output directories...
rmdir /s /q build 2>nul
rmdir /s /q build_installer 2>nul
rmdir /s /q publish 2>nul
rmdir /s /q dist 2>nul

echo Cleaning module build artifacts...

REM LenovoLegionToolkit.CLI
rmdir /s /q LenovoLegionToolkit.CLI\bin 2>nul
rmdir /s /q LenovoLegionToolkit.CLI\obj 2>nul

REM LenovoLegionToolkit.CLI.Lib
rmdir /s /q LenovoLegionToolkit.CLI.Lib\bin 2>nul
rmdir /s /q LenovoLegionToolkit.CLI.Lib\obj 2>nul

REM LenovoLegionToolkit.Lib (Core library with Elite Optimizations)
rmdir /s /q LenovoLegionToolkit.Lib\bin 2>nul
rmdir /s /q LenovoLegionToolkit.Lib\obj 2>nul

REM LenovoLegionToolkit.Lib.Automation
rmdir /s /q LenovoLegionToolkit.Lib.Automation\bin 2>nul
rmdir /s /q LenovoLegionToolkit.Lib.Automation\obj 2>nul

REM LenovoLegionToolkit.Lib.Macro
rmdir /s /q LenovoLegionToolkit.Lib.Macro\bin 2>nul
rmdir /s /q LenovoLegionToolkit.Lib.Macro\obj 2>nul

REM LenovoLegionToolkit.WPF (Main WPF application)
rmdir /s /q LenovoLegionToolkit.WPF\bin 2>nul
rmdir /s /q LenovoLegionToolkit.WPF\obj 2>nul

REM LenovoLegionToolkit.SpectrumTester
rmdir /s /q LenovoLegionToolkit.SpectrumTester\bin 2>nul
rmdir /s /q LenovoLegionToolkit.SpectrumTester\obj 2>nul

echo Cleaning temporary files...
del /q build.log 2>nul
del /q *.tmp 2>nul
del /q *.cache 2>nul
del /q *.bak 2>nul
del /q *.old 2>nul
del /q *.log 2>nul

echo Cleaning NuGet package caches...
rmdir /s /q packages 2>nul
del /q *.lock.json 2>nul

echo Cleaning test results...
rmdir /s /q TestResults 2>nul

echo.
echo ==========================================
echo CLEANUP COMPLETE
echo ==========================================
echo.
echo All build artifacts and temporary files have been removed.
echo.
echo Modules Cleaned (7):
echo   1. LenovoLegionToolkit.CLI
echo   2. LenovoLegionToolkit.CLI.Lib
echo   3. LenovoLegionToolkit.Lib (Elite Optimizations Phase 1-4)
echo   4. LenovoLegionToolkit.Lib.Automation
echo   5. LenovoLegionToolkit.Lib.Macro
echo   6. LenovoLegionToolkit.WPF (Dashboard ^& Settings UI)
echo   7. LenovoLegionToolkit.SpectrumTester
echo.
echo Directories Cleaned:
echo   - bin/ obj/ (all modules)
echo   - build/ build_installer/
echo   - publish/ dist/
echo   - .vs/ _ReSharper.Caches/
echo   - packages/ TestResults/
echo.
echo Files Cleaned:
echo   - build.log
echo   - *.tmp *.cache *.bak *.old *.log
echo   - *.lock.json
echo.
echo Elite Optimizations v1.0.0 - Clean Build Environment Ready
echo Phase 1-3: Production Features | Phase 4: Beta Features
echo.
echo Next Steps:
echo   1. Run: build_gen9_enhanced.bat (Build complete application)
echo   2. Or: dotnet build (Quick build)
echo   3. Or: dotnet restore (Restore packages only)
echo.
pause
