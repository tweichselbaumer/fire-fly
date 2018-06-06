﻿using Emgu.CV;
using FireFly.Models;
using FireFly.Utilities;
using LinkUp.Node;
using System;
using System.Windows;

namespace FireFly.ViewModels
{
    public class CameraViewModel : AbstractViewModel
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.Register("Enabled", typeof(bool), typeof(CameraViewModel), new FrameworkPropertyMetadata(false, new PropertyChangedCallback(OnPropertyChanged)));

        public static readonly DependencyProperty FPSProperty =
            DependencyProperty.Register("FPS", typeof(int), typeof(CameraViewModel), new PropertyMetadata(0));

        public static readonly DependencyProperty ImageProperty =
                    DependencyProperty.Register("Image", typeof(CvImageContainer), typeof(CameraViewModel), new PropertyMetadata(null));

        public static readonly DependencyProperty QualityProperty =
            DependencyProperty.Register("Quality", typeof(int), typeof(CameraViewModel), new FrameworkPropertyMetadata(0, new PropertyChangedCallback(OnPropertyChanged)));

        private LinkUpEventLabel _EventLabel;

        private FPSCounter _FPSCounter = new FPSCounter();

        private LinkUpPropertyLabel<byte> _QualityLabel;

        private System.Timers.Timer _Timer;

        public CameraViewModel(MainViewModel parent) : base(parent)
        {
            _Timer = new System.Timers.Timer(1000);
            _Timer.Elapsed += _Timer_Elapsed;
            _Timer.Start();
        }

        public bool Enabled
        {
            get { return (bool)GetValue(EnabledProperty); }
            set { SetValue(EnabledProperty, value); }
        }

        public int FPS
        {
            get { return (int)GetValue(FPSProperty); }
            set { SetValue(FPSProperty, value); }
        }

        public CvImageContainer Image
        {
            get { return (CvImageContainer)GetValue(ImageProperty); }
            set { SetValue(ImageProperty, value); }
        }

        public int Quality
        {
            get { return (int)GetValue(QualityProperty); }
            set { SetValue(QualityProperty, value); }
        }

        internal override void SettingsUpdated()
        {
            Quality = Parent.SettingContainer.Settings.StreamingSettings.Quality;
            Enabled = Parent.SettingContainer.Settings.StreamingSettings.Enabled;
        }

        internal override void UpdateLinkUpBindings()
        {
            if (Parent.Node != null)
            {
                _EventLabel = Parent.Node.GetLabelByName<LinkUpEventLabel>("firefly/test/label_event");
                if (Enabled)
                    _EventLabel.Subscribe();
                else
                    _EventLabel.Unsubscribe();
                _EventLabel.Fired += EventLabel_Fired;

                _QualityLabel = Parent.Node.GetLabelByName<LinkUpPropertyLabel<byte>>("firefly/test/jpeg_quality");
            }
        }

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            CameraViewModel cvm = (d as CameraViewModel);
            bool changed = false;
            switch (e.Property.Name)
            {
                case "Quality":
                    changed = cvm.Parent.SettingContainer.Settings.StreamingSettings.Quality != cvm.Quality;
                    cvm.Parent.SettingContainer.Settings.StreamingSettings.Quality = cvm.Quality;
                    try
                    {
                        if (changed)
                            cvm._QualityLabel.Value = (byte)cvm.Quality;
                    }
                    catch (Exception) { }
                    break;

                case "Enabled":
                    changed = cvm.Parent.SettingContainer.Settings.StreamingSettings.Enabled != cvm.Enabled;
                    cvm.Parent.SettingContainer.Settings.StreamingSettings.Enabled = cvm.Enabled;
                    try
                    {
                        if (changed)
                        {
                            if (cvm.Enabled)
                                cvm._EventLabel.Subscribe();
                            else
                                cvm._EventLabel.Unsubscribe();
                        }
                    }
                    catch (Exception) { }
                    break;

                default:
                    break;
            }
            if (changed)
            {
                cvm.Parent.SettingsUpdated(false);
            }
        }

        private void _Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Parent.SyncContext.Post(o =>
            {
                FPS = (int)_FPSCounter.FramesPerSecond;
            }, null);
        }

        private void EventLabel_Fired(LinkUpEventLabel label, byte[] data)
        {
            Mat mat = new Mat();
            CvInvoke.Imdecode(data, Emgu.CV.CvEnum.ImreadModes.Grayscale, mat);
            _FPSCounter.CountFrame();
            Parent.SyncContext.Post(o =>
            {
                Image = new CvImageContainer();
                Image.CvImage = mat;
            }
            , null);
        }
    }
}