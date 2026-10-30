@echo off
REM Prima Three Acre Farms Stock Reconciler - local development start-up
cd /d "%~dp0"
echo.
echo   Prima Three Acre Farms Stock Reconciler
echo   Open in browser:  http://localhost:5012
echo   Sign in: officer / Officer@2026   admin / Admin@2026   auditor / Auditor@2026
echo.
dotnet run --project src\PrimaFarm.StockReconciler.Web --launch-profile http
