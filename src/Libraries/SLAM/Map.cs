﻿using MathNet.Numerics.LinearAlgebra;
using System.Collections.Generic;
using System.Linq;

namespace FireFly.VI.SLAM
{
    public enum TrajectoryType
    {
        PreOptimazation,
        Optimazation
    }

    public class Map
    {
        private List<Frame> _Frames = new List<Frame>();
        private List<KeyFrame> _KeyFrames = new List<KeyFrame>();

        public List<Vector<double>> GetTrajectory(TrajectoryType trajectoryType = TrajectoryType.PreOptimazation)
        {
            switch (trajectoryType)
            {
                case TrajectoryType.PreOptimazation:
                    lock (_Frames)
                    {
                        return _Frames.Where(d => d != null).Select(c => c.T_cam_world.Inverse().SE3.Translation).ToList();
                    }
                case TrajectoryType.Optimazation:
                    lock (_Frames)
                    {
                        return _KeyFrames.Where(d => d != null).Select(c => c.Frame.T_cam_world.Inverse().SE3.Translation).ToList();
                    }
                default:
                    return new List<Vector<double>>();
            }
        }

        public void AddNewFrame(Frame frame)
        {
            lock (_Frames)
            {
                while (_Frames.Count < frame.Id)
                {
                    _Frames.Add(null);
                }
                _Frames[(int)frame.Id] = frame;
            }
        }

        public void AddNewKeyFrame(KeyFrame keyFrame)
        {
            lock (_KeyFrames)
            {
                while (_KeyFrames.Count < keyFrame.Id)
                {
                    _KeyFrames.Add(null);
                }
                _KeyFrames[(int)keyFrame.Id] = keyFrame;
            }
        }
    }
}