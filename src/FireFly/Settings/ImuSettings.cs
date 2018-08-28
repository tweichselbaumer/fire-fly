﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FireFly.Settings
{
    public class ImuSettings : AbstractSettings
    {
        private double _AccelerometerScale;
        private double _GyroscopeScale;
        private double _TemperatureScale;
        private double _TemperatureOffset;

        public double AccelerometerScale
        {
            get
            {
                return _AccelerometerScale;
            }

            set
            {
                _AccelerometerScale = value;
            }
        }

        public double GyroscopeScale
        {
            get
            {
                return _GyroscopeScale;
            }

            set
            {
                _GyroscopeScale = value;
            }
        }

        public double TemperatureScale
        {
            get
            {
                return _TemperatureScale;
            }

            set
            {
                _TemperatureScale = value;
            }
        }

        public double TemperatureOffset
        {
            get
            {
                return _TemperatureOffset;
            }

            set
            {
                _TemperatureOffset = value;
            }
        }

        public override void SetDefaults()
        {
            base.SetDefaults();
            _AccelerometerScale = 2048 / 9.80665;
            _GyroscopeScale = 16.4;
            _TemperatureOffset = 21;
            _TemperatureScale = 333.8;
        }
    }
}