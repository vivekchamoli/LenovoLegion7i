using Autofac;
using LenovoLegionToolkit.Lib.AI;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.FanCurve;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Features.FlipToStart;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Features.Hybrid.Notify;
using LenovoLegionToolkit.Lib.Features.InstantBoot;
using LenovoLegionToolkit.Lib.Features.OverDrive;
using LenovoLegionToolkit.Lib.Features.PanelLogo;
using LenovoLegionToolkit.Lib.Features.WhiteKeyboardBacklight;
using LenovoLegionToolkit.Lib.Integrations;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.PackageDownloader;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib;

public class IoCModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.Register<HttpClientFactory>();

        builder.Register<FnKeysDisabler>();
        builder.Register<LegionZoneDisabler>();
        builder.Register<VantageDisabler>();

        builder.Register<ApplicationSettings>();
        builder.Register<BalanceModeSettings>();
        builder.Register<GodModeSettings>();
        builder.Register<GPUOverclockSettings>();
        builder.Register<IntegrationsSettings>();
        builder.Register<PackageDownloaderSettings>();
        builder.Register<RGBKeyboardSettings>();
        builder.Register<SpectrumKeyboardSettings>();
        builder.Register<SunriseSunsetSettings>();
        builder.Register<UpdateCheckSettings>();

        builder.Register<AlwaysOnUSBFeature>();
        builder.Register<BatteryFeature>();
        builder.Register<BatteryNightChargeFeature>();
        builder.Register<DpiScaleFeature>();
        builder.Register<FlipToStartFeature>();
        builder.Register<FlipToStartCapabilityFeature>(true);
        builder.Register<FlipToStartUEFIFeature>(true);
        builder.Register<FnLockFeature>();
        builder.Register<GSyncFeature>();
        builder.Register<HDRFeature>();
        builder.Register<HybridModeFeature>();
        builder.Register<IGPUModeFeature>();
        builder.Register<IGPUModeCapabilityFeature>(true);
        builder.Register<IGPUModeFeatureFlagsFeature>(true);
        builder.Register<IGPUModeGamezoneFeature>(true);
        builder.Register<InstantBootFeature>();
        builder.Register<InstantBootFeatureFlagsFeature>(true);
        builder.Register<InstantBootCapabilityFeature>(true);
        builder.Register<MicrophoneFeature>();
        builder.Register<OneLevelWhiteKeyboardBacklightFeature>();
        builder.Register<OverDriveFeature>();
        builder.Register<OverDriveGameZoneFeature>(true);
        builder.Register<OverDriveCapabilityFeature>(true);
        builder.Register<PanelLogoBacklightFeature>();
        builder.Register<PanelLogoSpectrumBacklightFeature>(true);
        builder.Register<PanelLogoLenovoLightingBacklightFeature>(true);
        builder.Register<PortsBacklightFeature>();
        builder.Register<PowerModeFeature>();
        builder.Register<RefreshRateFeature>();
        builder.Register<ResolutionFeature>();
        builder.Register<SpeakerFeature>();
        builder.Register<TouchpadLockFeature>();
        builder.Register<WhiteKeyboardBacklightFeature>();
        builder.Register<WhiteKeyboardDriverBacklightFeature>(true);
        builder.Register<WhiteKeyboardLenovoLightingBacklightFeature>(true);
        builder.Register<WinKeyFeature>();

        builder.Register<DGPUNotify>();
        builder.Register<DGPUCapabilityNotify>(true);
        builder.Register<DGPUFeatureFlagsNotify>(true);
        builder.Register<DGPUGamezoneNotify>(true);

        builder.Register<DisplayBrightnessListener>().AutoActivateListener();
        builder.Register<DisplayConfigurationListener>().AutoActivateListener();
        builder.Register<DriverKeyListener>().AutoActivateListener();
        builder.Register<LightingChangeListener>().AutoActivateListener();
        builder.Register<NativeWindowsMessageListener>().AutoActivateListener();
        builder.Register<PowerModeListener>().AutoActivateListener();
        builder.Register<PowerStateListener>().AutoActivateListener();
        builder.Register<RGBKeyboardBacklightListener>().AutoActivateListener();
        builder.Register<SessionLockUnlockListener>().AutoActivateListener();
        builder.Register<SpecialKeyListener>().AutoActivateListener();
        builder.Register<SystemThemeListener>().AutoActivateListener();
        builder.Register<ThermalModeListener>().AutoActivateListener();
        builder.Register<WinKeyListener>().AutoActivateListener();

        builder.Register<GameAutoListener>();
        builder.Register<InstanceStartedEventAutoAutoListener>();
        builder.Register<InstanceStoppedEventAutoAutoListener>();
        builder.Register<ProcessAutoListener>();
        builder.Register<TimeAutoListener>();
        builder.Register<UserInactivityAutoListener>();
        builder.Register<WiFiAutoListener>();

        builder.Register<AIController>();
        builder.Register<DisplayBrightnessController>();
        builder.Register<GodModeController>();
        builder.Register<GodModeControllerV1>(true);
        builder.Register<GodModeControllerV2>(true);
        builder.Register<GodModeControllerV3>(true);

        // Gen 9 specific components
        builder.Register<Gen9ECController>();
        builder.Register<GPUController>();
        builder.Register<GPUOverclockController>();
        builder.Register<RGBKeyboardBacklightController>();
        builder.Register<SensorsController>();
        builder.Register<SensorsControllerV1>(true);
        builder.Register<SensorsControllerV2>(true);
        builder.Register<SensorsControllerV3>(true);
        builder.Register<SmartFnLockController>();
        builder.Register<SpectrumKeyboardBacklightController>();
        builder.Register<WindowsPowerModeController>();
        builder.Register<WindowsPowerPlanController>();

        // Phase 4: Advanced Optimization Controllers
        builder.Register<AdaptiveFanCurveController>();
        builder.Register<ManualFanController>();
        builder.Register<PowerUsagePredictor>();
        builder.Register<ReactiveSensorsController>(true);

        // Elite AI/ML Thermal Management System - Multi-Agent Architecture v6.2.2
        // Core services (singletons for state preservation)
        builder.RegisterType<AI.DataPersistenceService>().SingleInstance();
        builder.RegisterType<AI.SafetyValidator>().SingleInstance();
        builder.RegisterType<AI.ActionExecutor>().SingleInstance();
        builder.RegisterType<AI.WorkloadClassifier>().SingleInstance();
        builder.RegisterType<AI.SystemContextStore>().SingleInstance();
        builder.RegisterType<AI.BatteryLifeEstimator>().SingleInstance();
        builder.RegisterType<AI.UserBehaviorAnalyzer>().SingleInstance();
        builder.RegisterType<AI.UserPreferenceTracker>().SingleInstance();
        builder.RegisterType<AI.AgentCoordinator>().SingleInstance();
        builder.RegisterType<AI.ThermalOptimizer>().SingleInstance();
        builder.RegisterType<AI.AcousticOptimizer>().SingleInstance();
        builder.RegisterType<AI.UserOverrideManager>().SingleInstance();
        builder.RegisterType<AI.DecisionArbitrationEngine>().SingleInstance();

        // Multi-agent system (register with interface for array injection)
        builder.RegisterType<AI.ThermalAgent>().As<AI.IOptimizationAgent>().SingleInstance();
        builder.RegisterType<AI.PowerAgent>().As<AI.IOptimizationAgent>().SingleInstance();
        builder.RegisterType<AI.GPUAgent>().As<AI.IOptimizationAgent>().SingleInstance();
        builder.RegisterType<AI.BatteryAgent>().As<AI.IOptimizationAgent>().SingleInstance();
        builder.RegisterType<AI.HybridModeAgent>().As<AI.IOptimizationAgent>().SingleInstance();
        builder.RegisterType<AI.DisplayAgent>().As<AI.IOptimizationAgent>().SingleInstance();
        builder.RegisterType<AI.KeyboardLightAgent>().As<AI.IOptimizationAgent>().SingleInstance();

        // Action handlers (register with interface for array injection)
        builder.RegisterType<AI.CPUPowerLimitHandler>().As<AI.IActionHandler>().SingleInstance();
        builder.RegisterType<AI.GPUControlHandler>().As<AI.IActionHandler>().SingleInstance();
        builder.RegisterType<AI.FanControlHandler>().As<AI.IActionHandler>().SingleInstance();
        builder.RegisterType<AI.PowerModeHandler>().As<AI.IActionHandler>().SingleInstance();
        builder.RegisterType<AI.BatteryControlHandler>().As<AI.IActionHandler>().SingleInstance();
        builder.RegisterType<AI.HybridModeHandler>().As<AI.IActionHandler>().SingleInstance();
        builder.RegisterType<AI.DisplayControlHandler>().As<AI.IActionHandler>().SingleInstance();
        builder.RegisterType<AI.KeyboardBacklightHandler>().As<AI.IActionHandler>().SingleInstance();
        builder.RegisterType<AI.CoordinationHandler>().As<AI.IActionHandler>().SingleInstance();
        builder.RegisterType<AI.EliteProfileHandler>().As<AI.IActionHandler>().SingleInstance();

        // Elite hardware control (advanced power management)
        // These components work without drivers (ProcessPriority, WindowsPower)
        // Optional components gracefully degrade if drivers unavailable (MSR, NVAPI, PCIe)
        builder.RegisterType<System.EliteFeaturesManager>().SingleInstance();

        // Orchestrator and integration
        builder.RegisterType<AI.ResourceOrchestrator>().SingleInstance();
        builder.RegisterType<AI.OrchestratorLifecycleManager>().SingleInstance();

        builder.Register<UpdateChecker>();
        builder.Register<WarrantyChecker>();

        builder.Register<PackageDownloaderFactory>();
        builder.Register<PCSupportPackageDownloader>();
        builder.Register<VantagePackageDownloader>();

        builder.Register<HWiNFOIntegration>();

        // Centralized state and timing services (v6.3.1+)
        builder.Register<BatteryStateService>();
        builder.Register<SystemTickService>();
        builder.Register<GPUTransitionManager>(); // Phase 1: GPU transition management
        builder.Register<DisplayTopologyService>(); // Phase 1: Display topology awareness
        builder.Register<ProcessLaunchMonitor>(); // Phase 2: Predictive GPU switching
        builder.Register<MultiStepPlanner>(); // Phase 3: Multi-step conflict avoidance
        builder.Register<CPUCoreManager>(); // Phase 4: CPU per-core management
        builder.Register<MemoryPowerManager>(); // Phase 4: Memory power management
        builder.Register<System.PCIePowerManager>(); // Phase 4: PCIe/NVMe power management
        builder.Register<WorkModePreset>(); // Productivity Mode: One-click work optimization

        builder.Register<SunriseSunset>();

        builder.Register<BatteryDischargeRateMonitorService>();
    }
}
