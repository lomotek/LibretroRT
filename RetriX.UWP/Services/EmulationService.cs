﻿using Acr.UserDialogs;
using LibRetriX;
using Plugin.FileSystem.Abstractions;
using Plugin.LocalNotifications.Abstractions;
using RetriX.Shared.Services;
using RetriX.Shared.StreamProviders;
using RetriX.Shared.ViewModels;
using RetriX.UWP.Pages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace RetriX.UWP.Services
{
    public class EmulationService : IEmulationService
    {
        public const string StateSavedToSlotMessageTitleKey = "StateSavedToSlotMessageTitleKey";
        public const string StateSavedToSlotMessageBodyKey = "StateSavedToSlotMessageBodyKey";

        private const string VFSRomPath = "ROM";
        private const string VFSSystemPath = "System";
        private const string VFSSavePath = "Save";

        private static readonly Type GamePlayerPageType = typeof(GamePlayerPage);

        private static readonly IReadOnlyDictionary<InjectedInputTypes, InputTypes> InjectedInputMapping = new Dictionary<InjectedInputTypes, InputTypes>
        {
            { InjectedInputTypes.DeviceIdJoypadA, InputTypes.DeviceIdJoypadA },
            { InjectedInputTypes.DeviceIdJoypadB, InputTypes.DeviceIdJoypadB },
            { InjectedInputTypes.DeviceIdJoypadDown, InputTypes.DeviceIdJoypadDown },
            { InjectedInputTypes.DeviceIdJoypadLeft, InputTypes.DeviceIdJoypadLeft },
            { InjectedInputTypes.DeviceIdJoypadRight, InputTypes.DeviceIdJoypadRight },
            { InjectedInputTypes.DeviceIdJoypadSelect, InputTypes.DeviceIdJoypadSelect },
            { InjectedInputTypes.DeviceIdJoypadStart, InputTypes.DeviceIdJoypadStart },
            { InjectedInputTypes.DeviceIdJoypadUp, InputTypes.DeviceIdJoypadUp },
            { InjectedInputTypes.DeviceIdJoypadX, InputTypes.DeviceIdJoypadX },
            { InjectedInputTypes.DeviceIdJoypadY, InputTypes.DeviceIdJoypadY },
        };

        private readonly IFileSystem FileSystem;
        private readonly ILocalizationService LocalizationService;
        private readonly IPlatformService PlatformService;
        private readonly ISaveStateService SaveStateService;
        private readonly ILocalNotifications NotificationService;
        private readonly IInputManager InputManager;

        private readonly Frame RootFrame = Window.Current.Content as Frame;

        private IStreamProvider StreamProvider;
        private ICoreRunner CoreRunner;

        private static readonly string[] archiveExtensions = { ".zip" };
        public IReadOnlyList<string> ArchiveExtensions => archiveExtensions;

        private readonly GameSystemVM[] systems;
        public IReadOnlyList<GameSystemVM> Systems => systems;

        public string GameID => CoreRunner?.GameID;

        public event GameStartedDelegate GameStarted;
        public event GameStoppedDelegate GameStopped;
        public event GameRuntimeExceptionOccurredDelegate GameRuntimeExceptionOccurred;

        public EmulationService(IFileSystem fileSystem, IUserDialogs dialogsService, ILocalizationService localizationService, IPlatformService platformService, ISaveStateService saveStateService, ILocalNotifications notificationService, ICryptographyService cryptographyService, IInputManager inputManager)
        {
            FileSystem = fileSystem;
            LocalizationService = localizationService;
            PlatformService = platformService;
            SaveStateService = saveStateService;
            NotificationService = notificationService;
            InputManager = inputManager;

            RootFrame.Navigated += OnNavigated;

            var CDImageExtensions = new HashSet<string> { ".bin", ".cue", ".iso", ".mds", ".mdf" };

            systems = new GameSystemVM[]
            {
                new GameSystemVM(LibRetriX.FCEUMM.Core.Instance, FileSystem, LocalizationService, "SystemNameNES", "ManufacturerNameNintendo", "\uf118"),
                new GameSystemVM(LibRetriX.Snes9X.Core.Instance, FileSystem, LocalizationService, "SystemNameSNES", "ManufacturerNameNintendo", "\uf119"),
                //new GameSystemVM(LibRetriX.ParallelN64.Core.Instance, FileSystem, LocalizationService, "SystemNameNintendo64", "ManufacturerNameNintendo", "\uf116"),
                new GameSystemVM(LibRetriX.Gambatte.Core.Instance, FileSystem, LocalizationService, "SystemNameGameBoy", "ManufacturerNameNintendo", "\uf11b"),
                new GameSystemVM(LibRetriX.VBAM.Core.Instance, FileSystem, LocalizationService, "SystemNameGameBoyAdvance", "ManufacturerNameNintendo", "\uf115"),
                new GameSystemVM(LibRetriX.MelonDS.Core.Instance, FileSystem, LocalizationService, "SystemNameDS", "ManufacturerNameNintendo", "\uf117"),
                new GameSystemVM(LibRetriX.GPGX.Core.Instance, FileSystem, LocalizationService, "SystemNameSG1000", "ManufacturerNameSega", "\uf102", true, new HashSet<string>{ ".sg" }),
                new GameSystemVM(LibRetriX.GPGX.Core.Instance, FileSystem, LocalizationService, "SystemNameMasterSystem", "ManufacturerNameSega", "\uf118", true, new HashSet<string>{ ".sms" }),
                new GameSystemVM(LibRetriX.GPGX.Core.Instance, FileSystem, LocalizationService, "SystemNameGameGear", "ManufacturerNameSega", "\uf129", true, new HashSet<string>{ ".gg" }),
                new GameSystemVM(LibRetriX.GPGX.Core.Instance, FileSystem, LocalizationService, "SystemNameMegaDrive", "ManufacturerNameSega", "\uf124", true, new HashSet<string>{ ".mds", ".md", ".smd", ".gen" }),
                new GameSystemVM(LibRetriX.GPGX.Core.Instance, FileSystem, LocalizationService, "SystemNameMegaCD", "ManufacturerNameSega", "\uf124", false, new HashSet<string>{ ".bin", ".cue", ".iso", ".chd" }, CDImageExtensions),
                //new GameSystemVM(LibRetriX.BeetleSaturn.Core.Instance, FileSystem, LocalizationService, "SystemNameSaturn", "ManufacturerNameSega", "\uf124", false, null, CDImageExtensions),
                new GameSystemVM(LibRetriX.BeetlePSX.Core.Instance, FileSystem, LocalizationService, "SystemNamePlayStation", "ManufacturerNameSony", "\uf128", false, null, CDImageExtensions),
                new GameSystemVM(LibRetriX.BeetlePCEFast.Core.Instance, FileSystem, LocalizationService, "SystemNamePCEngine", "ManufacturerNameNEC", "\uf124", true, new HashSet<string>{ ".pce" }),
                new GameSystemVM(LibRetriX.BeetlePCEFast.Core.Instance, FileSystem, LocalizationService, "SystemNamePCEngineCD", "ManufacturerNameNEC", "\uf124", false, new HashSet<string>{ ".cue", ".ccd", ".chd" }, CDImageExtensions),
                new GameSystemVM(LibRetriX.BeetlePCFX.Core.Instance, FileSystem, LocalizationService, "SystemNamePCFX", "ManufacturerNameNEC", "\uf124", false, null, CDImageExtensions),
                new GameSystemVM(LibRetriX.BeetleWswan.Core.Instance, FileSystem, LocalizationService, "SystemNameWonderSwan", "ManufacturerNameBandai", "\uf129"),
                new GameSystemVM(LibRetriX.FBAlpha.Core.Instance, FileSystem, LocalizationService, "SystemNameNeoGeo", "ManufacturerNameSNK", "\uf102", false),
                new GameSystemVM(LibRetriX.BeetleNGP.Core.Instance, FileSystem, LocalizationService, "SystemNameNeoGeoPocket", "ManufacturerNameSNK", "\uf129"),
                new GameSystemVM(LibRetriX.FBAlpha.Core.Instance, FileSystem, LocalizationService, "SystemNameArcade", "ManufacturerNameFBAlpha", "\uf102", true),
            };

            foreach (var i in systems.Select(d => d.Core).Distinct())
            {
                i.SystemRootPath = VFSSystemPath;
                i.SaveRootPath = VFSSavePath;
                i.OpenFileStream = OnCoreOpenFileStream;
                i.CloseFileStream = OnCoreCloseFileStream;
            }
        }

        public async Task<bool> StartGameAsync(GameSystemVM system, IFileInfo file, IDirectoryInfo rootFolder = null)
        {
            var core = system.Core;
            if (CoreRunner == null)
            {
                RootFrame.Navigate(GamePlayerPageType);
            }
            else
            {
                await CoreRunner.UnloadGameAsync();
            }

            StreamProvider?.Dispose();
            StreamProvider = null;
            string virtualMainFilePath = null;
            if (core.NativeArchiveSupport || !ArchiveExtensions.Contains(Path.GetExtension(file.Name)))
            {
                virtualMainFilePath = $"{VFSRomPath}{Path.DirectorySeparatorChar}{file.Name}";
                StreamProvider = new SingleFileStreamProvider(virtualMainFilePath, file);
                if (rootFolder != null)
                {
                    virtualMainFilePath = file.FullName.Substring(rootFolder.FullName.Length + 1);
                    virtualMainFilePath = $"{VFSRomPath}{Path.DirectorySeparatorChar}{virtualMainFilePath}";
                    StreamProvider = new FolderStreamProvider(VFSRomPath, rootFolder);
                }
            }
            else
            {
                var archiveProvider = new ArchiveStreamProvider(VFSRomPath, file);
                await archiveProvider.InitializeAsync();
                StreamProvider = archiveProvider;
                var entries = await StreamProvider.ListEntriesAsync();
                virtualMainFilePath = entries.FirstOrDefault(d => system.SupportedExtensions.Contains(Path.GetExtension(d)));
            }

            var systemFolder = await system.GetSystemDirectoryAsync();
            var systemProvider = new FolderStreamProvider(VFSSystemPath, systemFolder);
            var saveFolder = await system.GetSaveDirectoryAsync();
            var saveProvider = new FolderStreamProvider(VFSSavePath, saveFolder);
            StreamProvider = new CombinedStreamProvider(new HashSet<IStreamProvider>() { StreamProvider, systemProvider, saveProvider });

            //Navigation should cause the player page to load, which in turn should initialize the core runner
            while (CoreRunner == null)
            {
                await Task.Delay(100);
            }

            if (virtualMainFilePath == null)
            {
                return false;
            }

            var loadSuccessful = false;
            try
            {
                loadSuccessful = await CoreRunner.LoadGameAsync(core, virtualMainFilePath);
            }
            catch
            {
                await StopGameAsync();
                return false;
            }

            if (loadSuccessful)
            {
                GameStarted?.Invoke(this);
            }
            else
            {
                await StopGameAsync();
                return false;
            }

            return loadSuccessful;
        }

        public Task ResetGameAsync()
        {
            return CoreRunner == null ? Task.CompletedTask : CoreRunner.ResetGameAsync();
        }

        public async Task StopGameAsync()
        {
            if (CoreRunner != null)
            {
                await CoreRunner.UnloadGameAsync();
            }

            CleanupAndGoBack();
            GameStopped?.Invoke(this);
        }

        public Task PauseGameAsync()
        {
            return CoreRunner != null ? CoreRunner.PauseCoreExecutionAsync() : Task.CompletedTask;
        }

        public Task ResumeGameAsync()
        {
            return CoreRunner != null ? CoreRunner.ResumeCoreExecutionAsync() : Task.CompletedTask;
        }

        public async Task<bool> SaveGameStateAsync(uint slotID)
        {
            var success = false;
            if (CoreRunner == null)
            {
                return success;
            }

            SaveStateService.SetGameId(GameID);
            using (var stream = await SaveStateService.GetStreamForSlotAsync(slotID, FileAccess.ReadWrite))
            {
                if (stream == null)
                {
                    return success;
                }

                success = await CoreRunner.SaveGameStateAsync(stream);
                await stream.FlushAsync();
            }

            if (success)
            {
                var notificationTitle = LocalizationService.GetLocalizedString(StateSavedToSlotMessageTitleKey);
                var notificationBody = string.Format(LocalizationService.GetLocalizedString(StateSavedToSlotMessageBodyKey), slotID);
                NotificationService.Show(notificationTitle, notificationBody);
            }

            return success;
        }

        public async Task<bool> LoadGameStateAsync(uint slotID)
        {
            var success = false;
            if (CoreRunner == null)
            {
                return success;
            }

            SaveStateService.SetGameId(GameID);
            using (var stream = await SaveStateService.GetStreamForSlotAsync(slotID, FileAccess.Read))
            {
                if (stream == null)
                {
                    return false;
                }

                success = await CoreRunner.LoadGameStateAsync(stream);
            }

            return success;
        }

        public void InjectInputPlayer1(InjectedInputTypes inputType)
        {
            InputManager.InjectInputPlayer1(InjectedInputMapping[inputType]);
        }

        private void OnNavigated(object sender, NavigationEventArgs e)
        {
            var runnerPage = e.Content as ICoreRunnerPage;
            CoreRunner = runnerPage?.CoreRunner;

            if (CoreRunner != null)
            {
                CoreRunner.CoreRunExceptionOccurred -= OnCoreExceptionOccurred;
                CoreRunner.CoreRunExceptionOccurred += OnCoreExceptionOccurred;
            }
        }

        private void OnCoreExceptionOccurred(ICore core, Exception e)
        {
            var task = PlatformService.RunOnUIThreadAsync(() =>
            {
                CleanupAndGoBack();
                GameRuntimeExceptionOccurred?.Invoke(this, e);
            });
        }

        private Stream OnCoreOpenFileStream(string path, FileAccess fileAccess)
        {
            var stream = StreamProvider.OpenFileStreamAsync(path, fileAccess).Result;
            return stream;
        }

        private void OnCoreCloseFileStream(Stream stream)
        {
            StreamProvider.CloseStream(stream);
        }

        private string GetVirtualGamePath(IFileInfo file, IDirectoryInfo rootFolder)
        {
            var mainFileVirtualPath = $"{VFSRomPath}{Path.DirectorySeparatorChar}{file.Name}";
            if (rootFolder != null)
            {
                mainFileVirtualPath = file.FullName.Substring(rootFolder.FullName.Length + 1);
                mainFileVirtualPath = $"{VFSRomPath}{Path.DirectorySeparatorChar}{mainFileVirtualPath}";
            }

            return mainFileVirtualPath;
        }

        private void CleanupAndGoBack()
        {
            StreamProvider?.Dispose();
            StreamProvider = null;

            if (RootFrame.CurrentSourcePageType == GamePlayerPageType)
            {
                RootFrame.GoBack();
            }

            PlatformService.ChangeMousePointerVisibility(MousePointerVisibility.Visible);
        }
    }
}
