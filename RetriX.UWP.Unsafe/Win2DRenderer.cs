﻿using LibRetriX;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.UI;

namespace RetriX.UWP
{
    public sealed class Win2DRenderer : IRenderer, ICoreRunner, IDisposable
    {
        public event CoreRunExceptionOccurredDelegate CoreRunExceptionOccurred;

        private readonly CoreCoordinator Coordinator;

        public string GameID { get; private set; }
        public bool CoreIsExecuting { get; private set; }

        public ulong SerializationSize
        {
            get
            {
                lock (Coordinator)
                {
                    var core = Coordinator.Core;
                    return core != null ? core.SerializationSize : 0;
                }
            }
        }

        private CanvasAnimatedControl RenderPanel;
        private bool RenderPanelInitialized = false;

        private readonly RenderTargetManager RenderTargetManager = new RenderTargetManager();

        public Win2DRenderer(CanvasAnimatedControl renderPanel, IAudioPlayer audioPlayer, IInputManager inputManager)
        {
            Coordinator = new CoreCoordinator
            {
                Renderer = this,
                AudioPlayer = audioPlayer,
                InputManager = inputManager
            };

            CoreIsExecuting = false;

            RenderPanel = renderPanel;
            RenderPanel.ClearColor = Color.FromArgb(0xff, 0, 0, 0);
            RenderPanel.Update -= RenderPanelUpdate;
            RenderPanel.Update += RenderPanelUpdate;
            RenderPanel.CreateResources -= RenderPanelCreateResources;
            RenderPanel.CreateResources += RenderPanelCreateResources;
            RenderPanel.Draw -= RenderPanelDraw;
            RenderPanel.Draw += RenderPanelDraw;
            RenderPanel.Unloaded -= RenderPanelUnloaded;
            RenderPanel.Unloaded += RenderPanelUnloaded;
        }

        public void Dispose()
        {
            lock (Coordinator)
            {
                Coordinator.Core?.UnloadGame();
                Coordinator.Dispose();
                RenderTargetManager.Dispose();
            }
        }

        public Task<bool> LoadGameAsync(ICore core, string mainGameFilePath)
        {
            return Task.Run(async () =>
            {
                while (!RenderPanelInitialized)
                {
                    //Ensure core doesn't try rendering before Win2D is ready.
                    //Some games load faster than the Win2D canvas is initialized
                    await Task.Delay(100);
                }

                await UnloadGameAsync();

                lock (Coordinator)
                {
                    Coordinator.Core = core;
                    if (core.LoadGame(mainGameFilePath) == false)
                    {
                        return false;
                    }

                    GameID = mainGameFilePath;
                    RenderTargetManager.CurrentCorePixelFormat = core.PixelFormat;
                    CoreIsExecuting = true;
                    return true;
                }
            });
        }

        public Task UnloadGameAsync()
        {
            return Task.Run(() =>
            {
                lock (Coordinator)
                {
                    GameID = null;
                    CoreIsExecuting = false;
                    Coordinator.Core?.UnloadGame();
                    Coordinator.AudioPlayer?.Stop();
                }
            });
        }

        public Task ResetGameAsync()
        {
            return Task.Run(() =>
            {
                lock (Coordinator)
                {
                    Coordinator.AudioPlayer?.Stop();
                    Coordinator.Core?.Reset();
                }
            });
        }

        public Task PauseCoreExecutionAsync()
        {
            return Task.Run(() =>
            {
                lock (Coordinator)
                {
                    Coordinator.AudioPlayer?.Stop();
                    CoreIsExecuting = false;
                }
            });
        }

        public Task ResumeCoreExecutionAsync()
        {
            return Task.Run(() =>
            {
                lock (Coordinator)
                {
                    CoreIsExecuting = true;
                }
            });
        }

        public Task<bool> SaveGameStateAsync(Stream outputStream)
        {
            return Task.Run(() =>
            {
                lock (Coordinator)
                {
                    var core = Coordinator.Core;
                    if (core == null)
                        return false;

                    return core.SaveState(outputStream);
                }
            });
        }

        public Task<bool> LoadGameStateAsync(Stream inputStream)
        {
            return Task.Run(() =>
            {
                lock (Coordinator)
                {
                    var core = Coordinator.Core;
                    if (core == null)
                        return false;

                    return core.LoadState(inputStream);
                }
            });
        }

        private void RenderPanelUnloaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            RenderPanel.Update -= RenderPanelUpdate;
            RenderPanel.Draw -= RenderPanelDraw;
            RenderPanel.Unloaded -= RenderPanelUnloaded;
            RenderPanel = null;
        }

        private void RenderPanelCreateResources(CanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
            RenderPanelInitialized = true;
        }

        private void RenderPanelUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
        {
            lock (Coordinator)
            {
                if (CoreIsExecuting && !Coordinator.AudioPlayerRequestsFrameDelay)
                {
                    try
                    {
                        Coordinator.Core?.RunFrame();
                    }
                    catch (Exception e)
                    {
                        GameID = null;
                        CoreIsExecuting = false;
                        Coordinator.AudioPlayer?.Stop();
                        CoreRunExceptionOccurred(Coordinator.Core, e);
                    }
                }
            }
        }

        private void RenderPanelDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        {
            RenderTargetManager.Render(args.DrawingSession, sender.Size);
        }

        public void RenderVideoFrame(IntPtr data, uint width, uint height, ulong pitch)
        {
            RenderTargetManager.UpdateFromCoreOutput(RenderPanel.Device, data, width, height, pitch);
        }

        public void GeometryChanged(GameGeometry geometry)
        {
            RenderTargetManager.UpdateRenderTargetSize(RenderPanel.Device, geometry);
        }

        public void PixelFormatChanged(PixelFormats format)
        {
            RenderTargetManager.CurrentCorePixelFormat = format;
        }

        public void TimingsChanged(SystemTimings timings)
        {
            var targetTimeTicks = (long)(TimeSpan.TicksPerSecond / timings.FPS);
            RenderPanel.TargetElapsedTime = TimeSpan.FromTicks(targetTimeTicks);
        }

        public void RotationChanged(Rotations rotation)
        {
            RenderTargetManager.CorrentRotation = rotation;
        }
    }
}
