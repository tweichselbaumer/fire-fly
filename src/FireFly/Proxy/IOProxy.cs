﻿using FireFly.Data.Storage;
using FireFly.Settings;
using LinkUp.Node;
using LinkUp.Raw;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FireFly.Proxy
{
    public enum IOProxyMode
    {
        Live,
        Offline
    }

    public class IOProxy : IDisposable
    {
        private LinkUpPropertyLabel<Double> _AccelerometerScaleLabel;
        private LinkUpEventLabel _CameraEventLabel;
        private LinkUpEventLabel _CameraImuEventLabel;
        private LinkUpPropertyLabel<Int16> _ExposureLabel;
        private LinkUpFunctionLabel _GetRemoteChessboardCorner;
        private LinkUpPropertyLabel<Double> _GyroscopeScaleLabel;
        private LinkUpEventLabel _ImuEventLabel;
        private LinkUpNode _Node;
        private IOProxyMode _ProxyMode = IOProxyMode.Live;
        private LinkUpPropertyLabel<Boolean> _RecordRemoteLabel;
        private LinkUpFunctionLabel _ReplayDataSend;
        private SettingContainer _SettingContainer;
        private List<Tuple<IProxyEventSubscriber, ProxyEventType>> _Subscriptions = new List<Tuple<IProxyEventSubscriber, ProxyEventType>>();
        private List<Task> _Tasks = new List<Task>();
        private LinkUpPropertyLabel<Double> _TemperatureOffsetLabel;
        private LinkUpPropertyLabel<Double> _TemperatureScaleLabel;
        private LinkUpFunctionLabel _UpdateSettings;

        private long lastTimestamp = 0;

        public IOProxy(SettingContainer settingContainer)
        {
            _SettingContainer = settingContainer;
        }

        public LinkUpConnectivityState ConnectivityState
        {
            get
            {
                LinkUpConnectivityState linkUpConnectivityState = LinkUpConnectivityState.Disconnected;
                if (Node != null)
                {
                    if (Node.SubNodes != null && Node.SubNodes.Count > 0)
                    {
                        linkUpConnectivityState = Node.SubNodes[0].Connector.ConnectivityState;
                    }
                }
                return linkUpConnectivityState;
            }
        }

        public LinkUpNode Node
        {
            get
            {
                return _Node;
            }

            set
            {
                _Node = value;
            }
        }

        public IOProxyMode ProxyMode
        {
            get
            {
                return _ProxyMode;
            }

            private set
            {
                _ProxyMode = value;

                Task.Run(() =>
                {
                    lock (_Subscriptions)
                    {
                        UpdateSubscription();
                    }
                });
            }
        }

        public void Connector_ConnectivityChanged(LinkUpConnector connector, LinkUpConnectivityState connectivity)
        {
            if (connectivity == LinkUpConnectivityState.Connected)
            {
                Update();
            }
        }

        public void Dispose()
        {
            //TODO: wait for tasks
        }

        public byte[] GetRemoteChessboardCorner(byte[] input)
        {
            if (_GetRemoteChessboardCorner != null)
            {
                return _GetRemoteChessboardCorner.Call(input);
            }
            return null;
        }

        public void ReplayOffline(DataReader reader, Action<TimeSpan> updateTime, Action onClose, Func<bool> isPaused, Func<bool> isStopped)
        {
            _Tasks.Add(Task.Factory.StartNew(() =>
            {
                ProxyMode = IOProxyMode.Offline;
                Stopwatch watch = new Stopwatch();
                watch.Start();
                int nextTimeUpdate = 1000;
                long startTime = -1;
                int currentTime = 0;
                while (reader.HasNext())
                {
                    while (isPaused())
                    {
                        watch.Stop();
                        Thread.Sleep(500);
                        watch.Start();
                    }
                    if (isStopped())
                    {
                        break;
                    }

                    Tuple<long, List<Tuple<ReaderMode, object>>> res = reader.Next();
                    if (startTime == -1)
                        startTime = res.Item1;

                    lock (_Subscriptions)
                    {
                        ImuEventData imuEventData = null;
                        CameraEventData cameraEventData = null;

                        int rawSize = 0;
                        byte[] rawImage = null;
                        byte[] rawImu = null;
                        double exposureTime = 0.0;

                        foreach (Tuple<ReaderMode, object> val in res.Item2)
                        {
                            if (val.Item1 == ReaderMode.Imu0)
                            {
                                imuEventData = ImuEventData.Parse(res.Item1, (Tuple<double, double, double, double, double, double>)val.Item2, res.Item2.Any(c => c.Item1 == ReaderMode.Camera0));
                                rawSize += imuEventData.RawSize;
                                rawImu = imuEventData.GetRaw(_SettingContainer.Settings.ImuSettings.GyroscopeScale, _SettingContainer.Settings.ImuSettings.AccelerometerScale, _SettingContainer.Settings.ImuSettings.TemperatureScale, _SettingContainer.Settings.ImuSettings.TemperatureOffset);
                            }
                            if (val.Item1 == ReaderMode.Camera0)
                            {
                                cameraEventData = CameraEventData.Parse(((Tuple<double, byte[]>)val.Item2).Item2, 0, false, ((Tuple<double, byte[]>)val.Item2).Item1);
                                rawSize += cameraEventData.RawSize;
                                rawImage = ((Tuple<double, byte[]>)val.Item2).Item2;
                                exposureTime = ((Tuple<double, byte[]>)val.Item2).Item1;
                            }
                        }

                        if (rawSize > 0)
                        {
                            byte[] data = new byte[rawSize];
                            Array.Copy(rawImu, data, rawImu.Length);
                            if (rawImage != null)
                            {
                                Array.Copy(BitConverter.GetBytes(exposureTime), 0, data, imuEventData.RawSize, sizeof(double));
                                Array.Copy(rawImage, 0, data, imuEventData.RawSize + sizeof(double), rawImage.Length);
                            }
                            _ReplayDataSend.AsyncCall(data);
                        }

                        foreach (Tuple<IProxyEventSubscriber, ProxyEventType> t in _Subscriptions.Where(c => c.Item2 == ProxyEventType.CameraImuEvent))
                        {
                            if (cameraEventData != null)
                                t.Item1.Fired(this, new List<AbstractProxyEventData>() { cameraEventData, imuEventData });
                            else
                            {
                                if (imuEventData != null)
                                    t.Item1.Fired(this, new List<AbstractProxyEventData>() { imuEventData });
                            }
                        }

                        foreach (Tuple<IProxyEventSubscriber, ProxyEventType> t in _Subscriptions.Where(c => c.Item2 == ProxyEventType.CameraEvent))
                        {
                            if (cameraEventData != null)
                                t.Item1.Fired(this, new List<AbstractProxyEventData>() { cameraEventData });
                        }

                        foreach (Tuple<IProxyEventSubscriber, ProxyEventType> t in _Subscriptions.Where(c => c.Item2 == ProxyEventType.ImuEvent))
                        {
                            if (imuEventData != null)
                                t.Item1.Fired(this, new List<AbstractProxyEventData>() { imuEventData });
                        }
                    }
                    currentTime += reader.DeltaTimeMs;
                    int sleep = (int)(currentTime - watch.ElapsedMilliseconds);
                    if (sleep > reader.DeltaTimeMs)
                        Thread.Sleep(sleep);

                    if (res.Item1 / 1000 > nextTimeUpdate)
                    {
                        nextTimeUpdate += 1000;
                        updateTime(reader.Length - TimeSpan.FromMilliseconds((res.Item1 - startTime) / (1000 * 1000)));
                    }
                }
                reader.Close();
                ProxyMode = IOProxyMode.Live;
                onClose();
            }, TaskCreationOptions.LongRunning));
        }

        public void SetExposure(Int16 exposure)
        {
            if (_ExposureLabel != null)
            {
                _ExposureLabel.Value = exposure;
            }
        }

        public void Subscribe(IProxyEventSubscriber subscriber, ProxyEventType eventType)
        {
            lock (_Subscriptions)
            {
                if (!_Subscriptions.Any(c => c.Item1 == subscriber && c.Item2 == eventType))
                {
                    _Subscriptions.Add(new Tuple<IProxyEventSubscriber, ProxyEventType>(subscriber, eventType));
                }
                UpdateSubscription();
            }
        }

        public void Unsubscribe(IProxyEventSubscriber subscriber, ProxyEventType eventType)
        {
            lock (_Subscriptions)
            {
                if (_Subscriptions.Any(c => c.Item1 == subscriber && c.Item2 == eventType))
                {
                    _Subscriptions.Remove(_Subscriptions.FirstOrDefault(c => c.Item1 == subscriber && c.Item2 == eventType));
                }
                UpdateSubscription();
            }
        }

        public void UpdateLinkUpBindings()
        {
            if (Node != null)
            {
                _CameraEventLabel = Node.GetLabelByName<LinkUpEventLabel>("firefly/computer_vision/camera_event");
                _ImuEventLabel = Node.GetLabelByName<LinkUpEventLabel>("firefly/computer_vision/imu_event");
                _CameraImuEventLabel = Node.GetLabelByName<LinkUpEventLabel>("firefly/computer_vision/camera_imu_event");

                _ExposureLabel = Node.GetLabelByName<LinkUpPropertyLabel<Int16>>("firefly/computer_vision/camera_exposure");

                _ReplayDataSend = Node.GetLabelByName<LinkUpFunctionLabel>("firefly/computer_vision/replay_data");

                _GetRemoteChessboardCorner = Node.GetLabelByName<LinkUpFunctionLabel>("firefly/computer_vision/get_chessboard_corner");

                _UpdateSettings = Node.GetLabelByName<LinkUpFunctionLabel>("firefly/computer_vision/update_settings");

                _AccelerometerScaleLabel = Node.GetLabelByName<LinkUpPropertyLabel<Double>>("firefly/computer_vision/acc_scale");
                _GyroscopeScaleLabel = Node.GetLabelByName<LinkUpPropertyLabel<Double>>("firefly/computer_vision/gyro_scale");
                _TemperatureScaleLabel = Node.GetLabelByName<LinkUpPropertyLabel<Double>>("firefly/computer_vision/temp_scale");
                _TemperatureOffsetLabel = Node.GetLabelByName<LinkUpPropertyLabel<Double>>("firefly/computer_vision/temp_offset");

                _RecordRemoteLabel = Node.GetLabelByName<LinkUpPropertyLabel<Boolean>>("firefly/computer_vision/record_remote");

                _CameraEventLabel.Fired += _CameraEventLabel_Fired;
                _ImuEventLabel.Fired += _CameraEventLabel_Fired;
                _CameraImuEventLabel.Fired += _CameraEventLabel_Fired;

                Update();
            }
        }

        public void UpdateSettings()
        {
            if (_AccelerometerScaleLabel != null)
            {
                try
                {
                    if (ConnectivityState == LinkUpConnectivityState.Connected)
                        _AccelerometerScaleLabel.Value = _SettingContainer.Settings.ImuSettings.AccelerometerScale;
                }
                catch (Exception) { }
            }
            if (_GyroscopeScaleLabel != null)
            {
                try
                {
                    if (ConnectivityState == LinkUpConnectivityState.Connected)
                        _GyroscopeScaleLabel.Value = _SettingContainer.Settings.ImuSettings.GyroscopeScale;
                }
                catch (Exception) { }
            }
            if (_TemperatureScaleLabel != null)
            {
                try
                {
                    if (ConnectivityState == LinkUpConnectivityState.Connected)
                        _TemperatureScaleLabel.Value = _SettingContainer.Settings.ImuSettings.TemperatureScale;
                }
                catch (Exception) { }
            }
            if (_TemperatureOffsetLabel != null)
            {
                try
                {
                    if (ConnectivityState == LinkUpConnectivityState.Connected)
                        _TemperatureOffsetLabel.Value = _SettingContainer.Settings.ImuSettings.TemperatureOffset;
                }
                catch (Exception) { }
            }

            if (_RecordRemoteLabel != null)
            {
                try
                {
                    if (ConnectivityState == LinkUpConnectivityState.Connected)
                        _RecordRemoteLabel.Value = _SettingContainer.Settings.ImuSettings.RecordRemote;
                }
                catch (Exception) { }
            }

            if (_UpdateSettings != null)
            {
                try
                {
                    if (ConnectivityState == LinkUpConnectivityState.Connected)
                        _UpdateSettings.AsyncCall(new byte[] { });
                }
                catch (Exception) { }
            }
        }

        private void _CameraEventLabel_Fired(LinkUpEventLabel label, byte[] data)
        {
            lock (_Subscriptions)
            {
                if (label == _CameraEventLabel)
                {
                    CameraEventData eventData = CameraEventData.Parse(data, 0, true);
                    foreach (Tuple<IProxyEventSubscriber, ProxyEventType> t in _Subscriptions.Where(c => c.Item2 == ProxyEventType.CameraEvent))
                    {
                        t.Item1.Fired(this, new List<AbstractProxyEventData>() { eventData });
                    }
                }
                else if (label == _ImuEventLabel)
                {
                    ImuEventData eventData = ImuEventData.Parse(data, 0, _SettingContainer.Settings.ImuSettings.GyroscopeScale, _SettingContainer.Settings.ImuSettings.AccelerometerScale, _SettingContainer.Settings.ImuSettings.TemperatureScale, _SettingContainer.Settings.ImuSettings.TemperatureOffset);
                    foreach (Tuple<IProxyEventSubscriber, ProxyEventType> t in _Subscriptions.Where(c => c.Item2 == ProxyEventType.ImuEvent))
                    {
                        t.Item1.Fired(this, new List<AbstractProxyEventData>() { eventData });
                    }
                }
                else if (label == _CameraImuEventLabel)
                {
                    ImuEventData imuEventData = ImuEventData.Parse(data, 0, _SettingContainer.Settings.ImuSettings.GyroscopeScale, _SettingContainer.Settings.ImuSettings.AccelerometerScale, _SettingContainer.Settings.ImuSettings.TemperatureScale, _SettingContainer.Settings.ImuSettings.TemperatureOffset);
                    CameraEventData cameraEventData = null;

                    if (imuEventData.HasCameraImage)
                        cameraEventData = CameraEventData.Parse(data, 23, true);

                    foreach (Tuple<IProxyEventSubscriber, ProxyEventType> t in _Subscriptions.Where(c => c.Item2 == ProxyEventType.CameraImuEvent))
                    {
                        if (cameraEventData != null)
                            t.Item1.Fired(this, new List<AbstractProxyEventData>() { cameraEventData, imuEventData });
                        else
                            t.Item1.Fired(this, new List<AbstractProxyEventData>() { imuEventData });
                    }

                    foreach (Tuple<IProxyEventSubscriber, ProxyEventType> t in _Subscriptions.Where(c => c.Item2 == ProxyEventType.CameraEvent))
                    {
                        if (cameraEventData != null)
                            t.Item1.Fired(this, new List<AbstractProxyEventData>() { cameraEventData });
                    }

                    foreach (Tuple<IProxyEventSubscriber, ProxyEventType> t in _Subscriptions.Where(c => c.Item2 == ProxyEventType.ImuEvent))
                    {
                        t.Item1.Fired(this, new List<AbstractProxyEventData>() { imuEventData });
                    }

                    lastTimestamp = imuEventData.TimeNanoSeconds;
                }
            }
        }

        private void Update()
        {
            try
            {
                lock (_Subscriptions)
                {
                    UpdateSubscription();
                }
                UpdateSettings();
            }
            catch (Exception ex)
            {
            }
        }

        private void UpdateSubscription()
        {
            if (ProxyMode == IOProxyMode.Live)
            {
                if (_CameraEventLabel != null && _ImuEventLabel != null && _CameraImuEventLabel != null)
                {
                    if (_Subscriptions.Any(c => c.Item2 == ProxyEventType.CameraEvent) && _Subscriptions.Any(c => c.Item2 == ProxyEventType.ImuEvent) || _Subscriptions.Any(c => c.Item2 == ProxyEventType.CameraImuEvent))
                    {
                        try
                        {
                            if (ConnectivityState == LinkUpConnectivityState.Connected)
                                _CameraImuEventLabel.Subscribe();
                        }
                        catch (Exception) { }
                        try
                        {
                            if (ConnectivityState == LinkUpConnectivityState.Connected)
                                _ImuEventLabel.Unsubscribe();
                        }
                        catch (Exception) { }
                        try
                        {
                            if (ConnectivityState == LinkUpConnectivityState.Connected)
                                _CameraEventLabel.Unsubscribe();
                        }
                        catch (Exception) { }
                    }
                    else
                    {
                        try
                        {
                            if (ConnectivityState == LinkUpConnectivityState.Connected)
                                _CameraImuEventLabel.Unsubscribe();
                        }
                        catch (Exception) { }
                        if (_Subscriptions.Any(c => c.Item2 == ProxyEventType.CameraEvent))
                        {
                            try
                            {
                                if (ConnectivityState == LinkUpConnectivityState.Connected)
                                    _CameraEventLabel.Subscribe();
                            }
                            catch (Exception) { }
                        }
                        else
                        {
                            try
                            {
                                if (ConnectivityState == LinkUpConnectivityState.Connected)
                                    _CameraEventLabel.Unsubscribe();
                            }
                            catch (Exception) { }
                        }
                        if (_Subscriptions.Any(c => c.Item2 == ProxyEventType.ImuEvent))
                        {
                            try
                            {
                                if (ConnectivityState == LinkUpConnectivityState.Connected)
                                    _ImuEventLabel.Subscribe();
                            }
                            catch (Exception) { }
                        }
                        else
                        {
                            try
                            {
                                if (ConnectivityState == LinkUpConnectivityState.Connected)
                                    _ImuEventLabel.Unsubscribe();
                            }
                            catch (Exception) { }
                        }
                    }
                }
            }
            else if (ProxyMode == IOProxyMode.Offline)
            {
                try
                {
                    if (ConnectivityState == LinkUpConnectivityState.Connected)
                        _CameraEventLabel.Unsubscribe();
                }
                catch (Exception) { }
                try
                {
                    if (ConnectivityState == LinkUpConnectivityState.Connected)
                        _ImuEventLabel.Unsubscribe();
                }
                catch (Exception) { }
                try
                {
                    if (ConnectivityState == LinkUpConnectivityState.Connected)
                        _CameraImuEventLabel.Unsubscribe();
                }
                catch (Exception) { }
            }
        }
    }
}