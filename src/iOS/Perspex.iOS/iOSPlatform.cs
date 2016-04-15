﻿using System;
using System.Reflection;
using Perspex.Input;
using Perspex.Input.Platform;
using Perspex.iOS;
using Perspex.Platform;
using Perspex.Shared.PlatformSupport;
using Perspex.Skia;
using UIKit;

namespace Perspex
{
    public static class iOSApplicationExtensions
    {
        public static AppT UseiOS<AppT>(this AppT app) where AppT : Application
        {
            Perspex.iOS.iOSPlatform.Initialize();
            return app;
        }

        // I wish I could merge this with the SkiaPlatform itself. Might be possible
        // once we switch to SkiaSharp
        public static AppT UseSkiaViewHost<AppT>(this AppT app) where AppT : Application
        {
            var window = new UIWindow(UIScreen.MainScreen.Bounds);
            var controller = new PerspexViewController(window);
            window.RootViewController = controller;
            window.MakeKeyAndVisible();

            PerspexLocator.CurrentMutable
                .Bind<IWindowingPlatform>().ToConstant(new WindowingPlatform(controller.PerspexView));

            SkiaPlatform.Initialize();

            return app;
        }

        public static AppT UseAssetAssembly<AppT>(this AppT app, Assembly assembly) where AppT : Application
        {
            // Asset loading searches our own assembly?
            var loader = new AssetLoader(assembly);
            PerspexLocator.CurrentMutable.Bind<IAssetLoader>().ToConstant(loader);

            return app;
        }

        // This is somewhat generic, could probably put this elsewhere. But I don't think
        // it should part of the iOS App Delegate
        //
        class WindowingPlatform : IWindowingPlatform
        {
            private readonly IWindowImpl _window;

            public WindowingPlatform(IWindowImpl window)
            {
                _window = window;
            }

            public IWindowImpl CreateWindow()
            {
                return _window;
            }

            public IWindowImpl CreateEmbeddableWindow()
            {
                throw new NotImplementedException();
            }

            public IPopupImpl CreatePopup()
            {
                throw new NotImplementedException();
            }
        }

    }
}

namespace Perspex.iOS
{
    // TODO: Perhaps we should make this class handle all these interfaces directly, like we 
    // do for Win32 and Gtk
    //
    public class iOSPlatform //: IPlatformThreadingInterface, IPlatformSettings, IWindowingPlatform
    {
        internal static MouseDevice MouseDevice;
        internal static KeyboardDevice KeyboardDevice;

        public static void Initialize()
        {
            MouseDevice = new MouseDevice();
            KeyboardDevice = new KeyboardDevice();

            // refactored
            //SharedPlatform.Register(appType.Assembly);

            PerspexLocator.CurrentMutable
                .Bind<IPclPlatformWrapper>().ToSingleton<PclPlatformWrapper>()
                .Bind<IClipboard>().ToTransient<Clipboard>()
                //.Bind<ISystemDialogImpl>().ToTransient<SystemDialogImpl>()
                .Bind<IStandardCursorFactory>().ToTransient<CursorFactory>()
                .Bind<IKeyboardDevice>().ToConstant(KeyboardDevice)
                .Bind<IMouseDevice>().ToConstant(MouseDevice)
                .Bind<IPlatformSettings>().ToSingleton<PlatformSettings>()
                .Bind<IPlatformThreadingInterface>().ToConstant(PlatformThreadingInterface.Instance);
        }
    }
}
