﻿using FireFly.VI.SLAM.Sophus;

namespace FireFly.VI.SLAM
{
    public class Frame
    {
        private uint _Id;
        private Sim3 _T_cam_world = new Sim3();

        public Frame(uint id, double tx, double ty, double tz, double q1, double q2, double q3, double q4, double s)
        {
            Id = id;
            T_cam_world = new Sim3(s, tx, ty, tz, q1, q2, q3, q4);
        }

        public uint Id
        {
            get
            {
                return _Id;
            }

            set
            {
                _Id = value;
            }
        }

        public Sim3 T_cam_world
        {
            get
            {
                return _T_cam_world;
            }

            set
            {
                _T_cam_world = value;
            }
        }
    }
}