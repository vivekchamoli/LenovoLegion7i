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

echo.
echo ==========================================
echo CLEANUP COMPLETE
echo ==========================================
echo.
echo All build artifacts and temporary files have been removed.
echo All 7 modules cleaned:
echo   - LenovoLegionToolkit.CLI
echo   - LenovoLegionToolkit.CLI.Lib
echo   - LenovoLegionToolkit.Lib (Elite Optimizations)
echo   - LenovoLegionToolkit.Lib.Automation
echo   - LenovoLegionToolkit.Lib.Macro
echo   - LenovoLegionToolkit.WPF
echo   - LenovoLegionToolkit.SpectrumTester
echo.
echo Ready for fresh build. Run: build_gen9_enhanced.bat
echo.
pause
