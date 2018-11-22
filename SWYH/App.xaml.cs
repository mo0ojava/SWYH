﻿/*
 *	 Stream What Your Hear
 *	 Assembly: SWYH
 *	 File: App.xaml.cs
 *	 Web site: http://www.streamwhatyouhear.com
 *	 Copyright (C) 2012-2017 - Sebastien Warin <http://sebastien.warin.fr> and others	
 *
 *   This file is part of Stream What Your Hear.
 *	 
 *	 Stream What Your Hear is free software: you can redistribute it and/or modify
 *	 it under the terms of the GNU General Public License as published by
 *	 the Free Software Foundation, either version 2 of the License, or
 *	 (at your option) any later version.
 *	 
 *	 Stream What Your Hear is distributed in the hope that it will be useful,
 *	 but WITHOUT ANY WARRANTY; without even the implied warranty of
 *	 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *	 GNU General Public License for more details.
 *	 
 *	 You should have received a copy of the GNU General Public License
 *	 along with Stream What Your Hear. If not, see <http://www.gnu.org/licenses/>.
 */

using NAudio.CoreAudioApi;

namespace SWYH
{
    using OpenSource.UPnP.AV.RENDERER.CP;
    using SWYH.Audio;
    using SWYH.UPnP;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using SharpCaster;
    using SharpCaster.Controllers;
    using SharpCaster.Extensions;
    using SharpCaster.Models;
    using SharpCaster.Models.ChromecastStatus;
    using SharpCaster.Models.MediaStatus;
    using SharpCaster.Services;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Collections.ObjectModel;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static App CurrentInstance { get { return Application.Current as App; } }
        public static bool NeedUpdate { get; private set; }

        private string[] autoStreamTo = new string[0];
        private AVRendererDiscovery rendererDiscovery = null;
        internal SwyhDevice swyhDevice = null;
        internal WasapiProvider wasapiProvider = null;
        private SettingsWindow settingsWindow = null;
        private AboutWindow aboutWindow = null;
        private HTTPLiveStreamWindow httpWindow = null;
        private RecordWindow recordWindow = null;
        private System.Windows.Forms.NotifyIcon notifyIcon = null;
        private System.Windows.Forms.ToolStripMenuItem streamToMenu = null;
        private System.Windows.Forms.ToolStripMenuItem streamToChromecastMenu = null;
        private System.Windows.Forms.ToolStripMenuItem searchingItem = null;
        private bool isStarted = false;

        private readonly ChromecastService chromecastService = ChromecastService.Current;
        private SharpCasterDemoController chromecastController;
        private ObservableCollection<Chromecast> chromecastDevices;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var startTest = 0;
            start:
            var existingProcess = Process.GetProcessesByName(Constants.SWYH_PROCESS_NAME);
            if (existingProcess != null && existingProcess.Length > 1)
            {
                if (e.Args != null && e.Args.Contains(Constants.RESTART_ARGUMENT_NAME) && (startTest++) < Constants.NUMBER_OF_RESTART_TEST)
                {
                    Thread.Sleep(100);
                    goto start;
                }
                MessageBox.Show("Stream What You Hear is already running !", "Stream What You Hear", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                this.Shutdown();
            }
            else
            {
                if (SWYH.Properties.Settings.Default.Debug)
                {
                    AppDomain.CurrentDomain.UnhandledException += (ss, ee) =>
                    {
                        var ex = (Exception)ee.ExceptionObject;
                        StringBuilder error = new StringBuilder();
                        error.AppendLine("Date: " + DateTime.Now.ToString());
                        error.AppendLine("Message: " + ex.Message);
                        error.AppendLine("Detail: " + ex.ToString());
                        error.AppendLine("------------------------------");
                        File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Constants.SWYH_CRASHLOG_FILENAME), error.ToString());
                        MessageBox.Show("An unhandled error has occured ! See the '" + Constants.SWYH_CRASHLOG_FILENAME + "' on your desktop for more information", "Stream What You Hear", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    };
                }
                if (new NAudio.CoreAudioApi.MMDeviceEnumerator().EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active).Count == 0)    //Check the available interface.
                {
                    MessageBox.Show("Unable to find your default audio interface !\n\nPlease check your audio settings in the Control Panel", "Stream What You Hear", MessageBoxButton.OK, MessageBoxImage.Error);
                    this.Shutdown();
                }
                else
                {
                    this.CheckAutomaticDeviceStreamed(e);
                    this.CheckNewVersion();
                    this.InitializeUI();
                    this.InitializeChromecast();
                    this.rendererDiscovery = new AVRendererDiscovery((new AVRendererDiscovery.DiscoveryHandler(RendererAddedSink)));
                    this.rendererDiscovery.OnRendererRemoved += new AVRendererDiscovery.DiscoveryHandler(new AVRendererDiscovery.DiscoveryHandler(RendererRemovedSink));
                    this.wasapiProvider = new WasapiProvider();
                    this.swyhDevice = new SwyhDevice();
                    this.swyhDevice.Start();
                    this.notifyIcon.ShowBalloonTip(2000, "Stream What You Hear is running", "Right-click on this icon to show the menu !", System.Windows.Forms.ToolTipIcon.Info);
                    this.isStarted = true;
                }
            }
        }

        private void InitializeUI()
        {
            FileVersionInfo fileVersion = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            this.settingsWindow = new SettingsWindow();
            this.aboutWindow = new AboutWindow();
            this.httpWindow = new HTTPLiveStreamWindow();
            this.recordWindow = new RecordWindow();
            this.notifyIcon = new System.Windows.Forms.NotifyIcon();
            this.notifyIcon = new System.Windows.Forms.NotifyIcon()
            {
                Icon = SWYH.Properties.Resources.swyh128_v2,
                Text = string.Format("Stream What You Hear {0}.{1}{2}", fileVersion.ProductMajorPart, fileVersion.ProductMinorPart, (fileVersion.ProductPrivatePart % 2) == 0 ? "" : " (BETA)"),
                Visible = true
            };
            this.notifyIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            this.streamToMenu = new System.Windows.Forms.ToolStripMenuItem("Stream to UPnP/DLNA", null);
            this.streamToChromecastMenu = new System.Windows.Forms.ToolStripMenuItem("Stream to Chromecast", null);
            this.searchingItem = new System.Windows.Forms.ToolStripMenuItem("Searching ...", null) { Name = "searchingItem", ForeColor = System.Drawing.Color.Gray };
            this.streamToMenu.DropDownItems.Add("My device is not listed", null, (s, e2) => MessageBox.Show("Perhaps your device is not connected or is not recognized as an UPnP Media Renderer !\nIf your device is an UPnP/DLNA player, try to start the stream of SWYH manually on the device.\nFor more information, go to http://www.streamwhatyouhear.com !", "Stream What You Hear", MessageBoxButton.OK, MessageBoxImage.Information));
            this.streamToMenu.DropDownItems.Add("-");
            this.streamToMenu.DropDownItems.Add(searchingItem);
            this.notifyIcon.ContextMenuStrip.Items.Add(streamToMenu);
            this.notifyIcon.ContextMenuStrip.Items.Add(streamToChromecastMenu);
            var toolsMenu = new System.Windows.Forms.ToolStripMenuItem("Tools", null);
            toolsMenu.DropDownItems.Add("HTTP Live Streaming", null, (s, e2) => this.httpWindow.Show());
            toolsMenu.DropDownItems.Add("Record What You Hear", null, (s, e2) => this.recordWindow.Show());
            this.notifyIcon.ContextMenuStrip.Items.Add(toolsMenu);
            this.notifyIcon.ContextMenuStrip.Items.Add("Settings", null, (s, e2) => this.settingsWindow.Show());
            this.notifyIcon.ContextMenuStrip.Items.Add("Website", null, (s, e2) => Process.Start(Constants.SWYH_WEBSITE_URL));
            this.notifyIcon.ContextMenuStrip.Items.Add("About", null, (s, e2) => this.aboutWindow.Show());
            this.notifyIcon.ContextMenuStrip.Items.Add("-");
            this.notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e2) => this.Shutdown());
        }

        private void CheckAutomaticDeviceStreamed(StartupEventArgs startupEvent)
        {
            if (startupEvent.Args.Any(a => a.StartsWith(Constants.STREAM_TO_ARGUMENT_NAME, StringComparison.InvariantCultureIgnoreCase)))
            {
                autoStreamTo = startupEvent.Args.First(a => a.StartsWith(Constants.STREAM_TO_ARGUMENT_NAME, StringComparison.InvariantCultureIgnoreCase)).Substring(Constants.STREAM_TO_ARGUMENT_NAME.Length).Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        private void CheckNewVersion()
        {
            FileVersionInfo fileVersion = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            Task.Factory.StartNew(() =>
            {
                // Check update !
                WebClient wc = new WebClient();
                try
                {
                    string lastVersionStr = wc.DownloadString(Constants.UPDATE_VERSION_URL);
                    if (!string.IsNullOrEmpty(lastVersionStr))
                    {
                        Version lastVersion = new Version(lastVersionStr);
                        Version currentVersion = new Version(fileVersion.FileVersion);
                        App.NeedUpdate = (lastVersion > currentVersion);
                        if (App.NeedUpdate)
                        {
                            var response = MessageBox.Show("A new version of Stream What You Hear is available !\n\nDo you want to download it now?", "Stream What You Hear", MessageBoxButton.YesNo, MessageBoxImage.Information);
                            if (response == MessageBoxResult.Yes)
                            {
                                Process.Start(Constants.DOWNLOAD_SWYH_URL);
                            }
                        }
                    }
                }
                catch { }
            });
        }

        private void CloseStreamingConnections()
        {
            foreach (System.Windows.Forms.ToolStripItem tsi in this.streamToMenu.DropDownItems)
            {
                var item = tsi as System.Windows.Forms.ToolStripMenuItem;
                if (item != null && item.Checked)
                {
                    item.Checked = false;
                    var renderer = item.Tag as AVRenderer;
                    if (renderer != null && renderer.Connections.Count > 0)
                    {
                        var connectionAV = renderer.Connections[0] as AVConnection;
                        connectionAV.Stop();
                        ToggleMuteWindowsAudioDevices(false);
                    }
                }
            }
        }

        private void RendererAddedSink(AVRendererDiscovery sender, AVRenderer renderer)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!this.streamToMenu.DropDownItems.ContainsKey(renderer.UniqueDeviceName))
                    {
                        var menuItem = new System.Windows.Forms.ToolStripMenuItem
                            (renderer.FriendlyName, (renderer.device.favicon != null) ? renderer.device.favicon.ToBitmap() : null, streamMenu_RendererSelected)
                            {
                                Name = renderer.UniqueDeviceName,
                                Tag = renderer
                            };
                        this.streamToMenu.DropDownItems.Add(menuItem);
                        if (renderer != null && renderer.Connections.Count > 0)
                        {
                            var connectionAV = renderer.Connections[0] as AVConnection;
                            connectionAV.OnPlayStateChanged += new AVConnection.PlayStateChangedHandler(connectionAV_OnPlayStateChanged);
                            if (autoStreamTo.Any(r => r.Equals(renderer.FriendlyName, StringComparison.InvariantCultureIgnoreCase)))
                            {
                                streamMenu_RendererSelected(menuItem, EventArgs.Empty);
                            }
                        }
                    }
                    if (this.streamToMenu.DropDownItems.ContainsKey(this.searchingItem.Name) && this.streamToMenu.DropDownItems.Count >= 2)
                    {
                        this.streamToMenu.DropDownItems.RemoveByKey(this.searchingItem.Name);
                    }
                }));
        }

        private void RendererRemovedSink(AVRendererDiscovery sender, AVRenderer renderer)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (this.streamToMenu.DropDownItems.ContainsKey(renderer.UniqueDeviceName))
                {
                    this.streamToMenu.DropDownItems.RemoveByKey(renderer.UniqueDeviceName);
                    if (renderer != null && renderer.Connections.Count > 0)
                    {
                        var connectionAV = renderer.Connections[0] as AVConnection;
                        connectionAV.OnPlayStateChanged -= new AVConnection.PlayStateChangedHandler(connectionAV_OnPlayStateChanged);
                    }
                }
                if (!this.streamToMenu.DropDownItems.ContainsKey(this.searchingItem.Name) && this.streamToMenu.DropDownItems.Count == 2)
                {
                    this.streamToMenu.DropDownItems.Add(searchingItem);
                }
            }));
        }

        private void streamMenu_RendererSelected(object sender, EventArgs e)
        {
            var rendererItem = (System.Windows.Forms.ToolStripMenuItem)sender;
            var renderer = rendererItem.Tag as AVRenderer;
            if (renderer != null && renderer.Connections.Count > 0)
            {
                var connectionAV = renderer.Connections[0] as AVConnection;
                if (rendererItem.Checked)
                {
                    rendererItem.Checked = false;
                    connectionAV.Stop();
                }
                else
                {
                    rendererItem.Checked = true;
                    var media = this.swyhDevice.ContentDirectory.GetWasapiMediaItem();
                    if (media != null)
                    {
                        connectionAV.Stop();
                        renderer.CreateConnection(media, DateTime.Now.Ticks);
                        connectionAV.Play();
                    }
                }
            }
        }

        public static bool ToggleMuteWindowsAudioDevices(bool muted = false)
        {
            //Instantiate an Enumerator to find audio devices
            NAudio.CoreAudioApi.MMDeviceEnumerator MMDE = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            //Get all the devices, no matter what condition or status
            NAudio.CoreAudioApi.MMDeviceCollection DevCol = MMDE.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.All, NAudio.CoreAudioApi.DeviceState.All);
            //Loop through all devices
            foreach (NAudio.CoreAudioApi.MMDevice dev in DevCol)
            {
                try
                {
                    //Show us the human understandable name of the device
                    //System.Diagnostics.Debug.Print(dev.FriendlyName);
                    if (dev.State == DeviceState.Active)
                        //[Un]Mute it
                        dev.AudioEndpointVolume.Mute = muted;
                }
                catch (Exception ex)
                {
                    //Do something with exception when an audio endpoint could not be muted
                    return false;
                }
            }
            return true;
        }

        private void connectionAV_OnPlayStateChanged(AVConnection sender, AVConnection.PlayState NewState)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (this.streamToMenu.DropDownItems.ContainsKey(sender.Parent.UniqueDeviceName) &&
                        sender.MediaResource != null && this.swyhDevice.ContentDirectory.GetWasapiUris(AudioSettings.GetStreamFormat()).Contains(sender.MediaResource.ContentUri))
                    {
                        var item = this.streamToMenu.DropDownItems[sender.Parent.UniqueDeviceName] as System.Windows.Forms.ToolStripMenuItem;
                        {
                            if (NewState == AVConnection.PlayState.STOPPED)
                            {
                                ToggleMuteWindowsAudioDevices(false);
                                item.Checked = false;
                            }
                            else if (NewState == AVConnection.PlayState.PLAYING)
                            {
                                ToggleMuteWindowsAudioDevices(true);
                                item.Checked = true;
                            }
                        }
                    }
                }));
        }

        private async Task InitializeChromecast()
        {
            this.chromecastDevices = await this.chromecastService.StartLocatingDevices();
            this.UpdateChromecastDevicesList();
        }

        private void UpdateChromecastDevicesList()
        {
            this.streamToChromecastMenu.DropDownItems.Clear();
            if (this.chromecastDevices.Count != 0)
            {
                foreach (var device in this.chromecastDevices)
                {
                    var menuItem = new System.Windows.Forms.ToolStripMenuItem(device.FriendlyName, null, streamToChromecastMenu_DeviceSelected)
                    {
                        Tag = device
                    };
                    this.streamToChromecastMenu.DropDownItems.Add(menuItem);
                }
            }
        }

        public async void streamToChromecastMenu_DeviceSelected(object sender, EventArgs e)
        {
            var menuItem = (System.Windows.Forms.ToolStripMenuItem)sender;
            var device = menuItem.Tag as Chromecast;
            if (device != null)
            {
                if (menuItem.Checked)
                {
                    menuItem.Checked = false;
                    chromecastService.ChromeCastClient.ConnectedChanged -= ChromeCastClient_Connected;
                    chromecastService.ChromeCastClient.ApplicationStarted -= Client_ApplicationStarted;
                    await chromecastController.Stop();
                    await chromecastController.StopApplication();
                    //TODO: Disconnect from the chromecast. Dispose the socket connection
                    //reference: https://github.com/tapanila/SharpCaster/blob/master/SharpCaster/ChromeCastClient.cs Line:191
                }
                else
                {
                    menuItem.Checked = true;
                    chromecastService.ChromeCastClient.ConnectedChanged += ChromeCastClient_Connected;
                    chromecastService.ChromeCastClient.ApplicationStarted += Client_ApplicationStarted;
                    chromecastService.ConnectToChromecast(device);
                }
            }
        }

        private async void Client_ApplicationStarted(object sender, ChromecastApplication e)
        {
            while (chromecastController == null)
            {
                await Task.Delay(500);
            }
            await chromecastController.LoadMedia(SWYH.App.CurrentInstance.swyhDevice.ContentDirectory.GetWasapiUris(Audio.AudioFormats.Format.Mp3).FirstOrDefault(), null, null, "BUFFERED", 0D, null);
        }

        private async void ChromeCastClient_Connected(object sender, EventArgs e)
        {
            if (chromecastController == null)
            {
                chromecastController = await chromecastService.ChromeCastClient.LaunchSharpCaster();
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            if (this.isStarted)
            {
                this.CloseStreamingConnections();
                this.swyhDevice.Stop();
                this.wasapiProvider.Dispose();
                if (this.notifyIcon != null)
                {
                    this.notifyIcon.Dispose();
                }
            }
            ToggleMuteWindowsAudioDevices(false);
        }
    }
}
