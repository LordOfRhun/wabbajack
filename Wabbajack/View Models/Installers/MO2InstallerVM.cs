﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Wabbajack.Common;
using Wabbajack.Lib;

namespace Wabbajack
{
    public class MO2InstallerVM : ViewModel, ISubInstallerVM
    {
        private InstallerVM _installerVM;

        public IReactiveCommand BeginCommand { get; }

        [Reactive]
        public AInstaller ActiveInstallation { get; private set; }

        private readonly ObservableAsPropertyHelper<Mo2ModlistInstallationSettings> _CurrentSettings;
        public Mo2ModlistInstallationSettings CurrentSettings => _CurrentSettings.Value;

        public FilePickerVM Location { get; }

        public FilePickerVM DownloadLocation { get; }

        public MO2InstallerVM(InstallerVM installerVM)
        {
            _installerVM = installerVM;

            Location = new FilePickerVM()
            {
                ExistCheckOption = FilePickerVM.ExistCheckOptions.Off,
                PathType = FilePickerVM.PathTypeOptions.Folder,
                PromptTitle = "Select Installation Directory",
            };
            Location.AdditionalError = this.WhenAny(x => x.Location.TargetPath)
                .Select(x => Utils.IsDirectoryPathValid(x));
            DownloadLocation = new FilePickerVM()
            {
                ExistCheckOption = FilePickerVM.ExistCheckOptions.Off,
                PathType = FilePickerVM.PathTypeOptions.Folder,
                PromptTitle = "Select a location for MO2 downloads",
            };
            DownloadLocation.AdditionalError = this.WhenAny(x => x.DownloadLocation.TargetPath)
                .Select(x => Utils.IsDirectoryPathValid(x));

            BeginCommand = ReactiveCommand.CreateFromTask(
                canExecute: Observable.CombineLatest(
                        this.WhenAny(x => x.Location.InError),
                        this.WhenAny(x => x.DownloadLocation.InError),
                        installerVM.WhenAny(x => x.ModListLocation.InError),
                        resultSelector: (loc, modlist, download) =>
                        {
                            return !loc && !download && !modlist;
                        })
                    .ObserveOnGuiThread(),
                execute: async () =>
                {
                    AInstaller installer;

                    try
                    {
                        installer = new MO2Installer(
                            archive: installerVM.ModListLocation.TargetPath,
                            modList: installerVM.ModList.SourceModList,
                            outputFolder: Location.TargetPath,
                            downloadFolder: DownloadLocation.TargetPath);
                    }
                    catch (Exception ex)
                    {
                        while (ex.InnerException != null) ex = ex.InnerException;
                        Utils.Log(ex.StackTrace);
                        Utils.Log(ex.ToString());
                        Utils.Log($"{ex.Message} - Can't continue");
                        ActiveInstallation = null;
                        return;
                    }

                    await Task.Run(async () =>
                    {
                        IDisposable subscription = null;
                        try
                        {
                            var workTask = installer.Begin();
                            ActiveInstallation = installer;
                            await workTask;
                        }
                        catch (Exception ex)
                        {
                            while (ex.InnerException != null) ex = ex.InnerException;
                            Utils.Log(ex.StackTrace);
                            Utils.Log(ex.ToString());
                            Utils.Log($"{ex.Message} - Can't continue");
                        }
                        finally
                        {
                            // Dispose of CPU tracking systems
                            subscription?.Dispose();
                            ActiveInstallation = null;
                        }
                    });
                });

            // Have Installation location updates modify the downloads location if empty
            this.WhenAny(x => x.Location.TargetPath)
                .Skip(1) // Don't do it initially
                .Subscribe(installPath =>
                {
                    if (string.IsNullOrWhiteSpace(DownloadLocation.TargetPath))
                    {
                        DownloadLocation.TargetPath = Path.Combine(installPath, "downloads");
                    }
                })
                .DisposeWith(CompositeDisposable);

            // Load settings
            _CurrentSettings = installerVM.WhenAny(x => x.ModListLocation.TargetPath)
                .Select(path => path == null ? null : installerVM.MWVM.Settings.Installer.Mo2ModlistSettings.TryCreate(path))
                .ToProperty(this, nameof(CurrentSettings));
            this.WhenAny(x => x.CurrentSettings)
                .Pairwise()
                .Subscribe(settingsPair =>
                {
                    SaveSettings(settingsPair.Previous);
                    if (settingsPair.Current == null) return;
                    Location.TargetPath = settingsPair.Current.InstallationLocation;
                    DownloadLocation.TargetPath = settingsPair.Current.DownloadLocation;
                })
                .DisposeWith(CompositeDisposable);
            installerVM.MWVM.Settings.SaveSignal
                .Subscribe(_ => SaveSettings(CurrentSettings))
                .DisposeWith(CompositeDisposable);
        }

        public void Unload()
        {
            SaveSettings(this.CurrentSettings);
        }

        private void SaveSettings(Mo2ModlistInstallationSettings settings)
        {
            _installerVM.MWVM.Settings.Installer.LastInstalledListLocation = _installerVM.ModListLocation.TargetPath;
            if (settings == null) return;
            settings.InstallationLocation = Location.TargetPath;
            settings.DownloadLocation = DownloadLocation.TargetPath;
        }
    }
}