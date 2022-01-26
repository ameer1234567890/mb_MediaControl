using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using static System.IO.WindowsRuntimeStreamExtensions;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Gma.System.MouseKeyHook;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private readonly PluginInfo about = new PluginInfo();
        private SystemMediaTransportControls systemMediaControls;
        private SystemMediaTransportControlsDisplayUpdater displayUpdater;
        private MusicDisplayProperties musicProperties;
        private InMemoryRandomAccessStream artworkStream;

        private IKeyboardMouseEvents globalHook;
        private int mediaKeysInvalidateBeforeMs = 2000; // media control buttons won't trigger for 2000 ms after media button is pressed
        private DateTime lastPlayPauseKeyPress;
        private DateTime lastStopKeyPress;
        private DateTime lastPreviousTrackKeyPress;
        private DateTime lastNextTrackKeyPress;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            SubscribeGlobalHooks();
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "Media Control";
            about.Description = "Enables MusicBee to interact with the Windows 10 Media Control overlay.";
            about.Author = "Ameer Dawood";
            about.TargetApplication = "";   //  the name of a Plugin Storage device or panel header for a dockable panel
            about.Type = PluginType.General;
            about.VersionMajor = 1;  // your plugin version
            about.VersionMinor = 0;
            about.Revision = 2;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            about.ConfigurationPanelHeight = 0;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            return false;
        }
       
        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
            UnsubscribeGlobalHooks();
            SetArtworkThumbnail(null);
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }

        private void MediaControl_PlayPauseButtonPress()
        {
            if (DateTime.Now.Subtract(lastPlayPauseKeyPress).TotalMilliseconds > mediaKeysInvalidateBeforeMs)
                mbApiInterface.Player_PlayPause();
        }

        private void MediaControl_StopButtonPress()
        {
            if (DateTime.Now.Subtract(lastStopKeyPress).TotalMilliseconds > mediaKeysInvalidateBeforeMs)
                mbApiInterface.Player_Stop();
        }

        private void MediaControl_PreviousTrackButtonPress()
        {
            if (DateTime.Now.Subtract(lastPreviousTrackKeyPress).TotalMilliseconds > mediaKeysInvalidateBeforeMs)
                mbApiInterface.Player_PlayPreviousTrack();
        }

        private void MediaControl_NextTrackButtonPress()
        {
            if (DateTime.Now.Subtract(lastNextTrackKeyPress).TotalMilliseconds > mediaKeysInvalidateBeforeMs)
                mbApiInterface.Player_PlayNextTrack();
        }

        // receive event notifications from MusicBee
        // you need to set about.ReceiveNotificationFlags = PlayerEvents to receive all notifications, and not just the startup event
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            switch (type)
            {
                case NotificationType.PluginStartup:
                    systemMediaControls = BackgroundMediaPlayer.Current.SystemMediaTransportControls;
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Closed;
                    systemMediaControls.IsEnabled = false;
                    systemMediaControls.IsPlayEnabled = true;
                    systemMediaControls.IsPauseEnabled = true;
                    systemMediaControls.IsStopEnabled = true;
                    systemMediaControls.IsPreviousEnabled = true;
                    systemMediaControls.IsNextEnabled = true;
                    systemMediaControls.IsRewindEnabled = false;
                    systemMediaControls.IsFastForwardEnabled = false;
                    systemMediaControls.ButtonPressed += systemMediaControls_ButtonPressed;
                    systemMediaControls.PlaybackPositionChangeRequested += systemMediaControls_PlaybackPositionChangeRequested;
                    systemMediaControls.PlaybackRateChangeRequested += systemMediaControls_PlaybackRateChangeRequested;
                    systemMediaControls.ShuffleEnabledChangeRequested += systemMediaControls_ShuffleEnabledChangeRequested;
                    systemMediaControls.AutoRepeatModeChangeRequested += systemMediaControls_AutoRepeatModeChangeRequested;
                    displayUpdater = systemMediaControls.DisplayUpdater;
                    displayUpdater.Type = MediaPlaybackType.Music;
                    musicProperties = displayUpdater.MusicProperties;
                    SetDisplayValues();
                    break;
                case NotificationType.PlayStateChanged:
                    SetPlayerState();
                    break;
                case NotificationType.TrackChanged:
                    SetDisplayValues();
                    break;
            }
        }

        private void systemMediaControls_ButtonPressed(SystemMediaTransportControls smtc, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Stop:
                    MediaControl_StopButtonPress();
                    break;
                case SystemMediaTransportControlsButton.Play:
                case SystemMediaTransportControlsButton.Pause:
                    MediaControl_PlayPauseButtonPress();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    MediaControl_NextTrackButtonPress();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    MediaControl_PreviousTrackButtonPress();
                    break;
                case SystemMediaTransportControlsButton.Rewind:
                    break;
                case SystemMediaTransportControlsButton.FastForward:
                    break;
                case SystemMediaTransportControlsButton.ChannelUp:
                    mbApiInterface.Player_SetVolume(mbApiInterface.Player_GetVolume() + 0.05F);
                    break;
                case SystemMediaTransportControlsButton.ChannelDown:
                    mbApiInterface.Player_SetVolume(mbApiInterface.Player_GetVolume() - 0.05F);
                    break;
            }
        }

        private void systemMediaControls_PlaybackPositionChangeRequested(SystemMediaTransportControls smtc, PlaybackPositionChangeRequestedEventArgs args)
        {
            mbApiInterface.Player_SetPosition(args.RequestedPlaybackPosition.Milliseconds);
        }

        private void systemMediaControls_PlaybackRateChangeRequested(SystemMediaTransportControls smtc, PlaybackRateChangeRequestedEventArgs args)
        {
        }

        private void systemMediaControls_AutoRepeatModeChangeRequested(SystemMediaTransportControls smtc, AutoRepeatModeChangeRequestedEventArgs args)
        {
            switch (args.RequestedAutoRepeatMode)
            {
                case MediaPlaybackAutoRepeatMode.Track:
                    mbApiInterface.Player_SetRepeat(RepeatMode.One);
                    break;
                case MediaPlaybackAutoRepeatMode.List:
                    mbApiInterface.Player_SetRepeat(RepeatMode.All);
                    break;
                case MediaPlaybackAutoRepeatMode.None:
                    mbApiInterface.Player_SetRepeat(RepeatMode.None);
                    break;
            }
        }

        private void systemMediaControls_ShuffleEnabledChangeRequested(SystemMediaTransportControls smtc, ShuffleEnabledChangeRequestedEventArgs args)
        {
            mbApiInterface.Player_SetShuffle(args.RequestedShuffleEnabled);
        }

        private void SetDisplayValues()
        {
            displayUpdater.ClearAll();
            displayUpdater.Type = MediaPlaybackType.Music;
            SetArtworkThumbnail(null);
            string url = mbApiInterface.NowPlaying_GetFileUrl();
            if (url != null)
            {
                musicProperties.AlbumArtist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.AlbumArtist);
                musicProperties.AlbumTitle = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);
                uint value;
                if (UInt32.TryParse(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackCount), out value))
                    musicProperties.AlbumTrackCount = value;
                musicProperties.Artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
                musicProperties.Title = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
                if (string.IsNullOrEmpty(musicProperties.Title))
                    musicProperties.Title = url.Substring(url.LastIndexOfAny(new char[] { '/', '\\' }) + 1);
                if (UInt32.TryParse(mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackNo), out value))
                    musicProperties.TrackNumber = value;
                //musicProperties.Genres = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Genres).Split(new string[] {"; "}, StringSplitOptions.RemoveEmptyEntries);
                PictureLocations pictureLocations;
                string pictureUrl;
                byte[] imageData;
                mbApiInterface.Library_GetArtworkEx(url, 0, true, out pictureLocations, out pictureUrl, out imageData);
                SetArtworkThumbnail(imageData);
            }
            displayUpdater.Update();
        }

        private void SetPlayerState()
        {

            switch (mbApiInterface.Player_GetPlayState())
            {
                case PlayState.Playing:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    systemMediaControls.IsEnabled = true;
                    break;
                case PlayState.Paused:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                    break;
                case PlayState.Stopped:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    systemMediaControls.IsEnabled = false;
                    break;
            }
        }

        private async void SetArtworkThumbnail(byte[] data)
        {
            if (artworkStream != null)
                artworkStream.Dispose();
            if (data == null)
            {
                artworkStream = null;
                displayUpdater.Thumbnail = null;
            }
            else
            {
                new MemoryStream(data).AsInputStream();

                artworkStream = new InMemoryRandomAccessStream();
                await artworkStream.WriteAsync(data.AsBuffer());
                displayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromStream(artworkStream);
            }
        }

        private void SubscribeGlobalHooks()
        {
            globalHook = Hook.GlobalEvents();
            globalHook.KeyPress += GlobalHook_KeyPress;
        }

        private void UnsubscribeGlobalHooks()
        {
            globalHook.KeyPress -= GlobalHook_KeyPress;
            globalHook.Dispose();
        }

        private void GlobalHook_KeyPress(object sender, KeyPressEventArgs e)
        {
            switch((Keys)e.KeyChar)
            {
                case Keys.MediaPlayPause:
                    lastPlayPauseKeyPress = DateTime.Now;
                    break;
                case Keys.MediaStop:
                    lastStopKeyPress = DateTime.Now;
                    break;
                case Keys.MediaPreviousTrack:
                    lastPreviousTrackKeyPress = DateTime.Now;
                    break;
                case Keys.MediaNextTrack:
                    lastNextTrackKeyPress = DateTime.Now;
                    break;
                default:
                    break;
            }
        }

    }

}