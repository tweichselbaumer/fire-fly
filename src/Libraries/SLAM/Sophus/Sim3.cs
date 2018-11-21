﻿using MathNet.Numerics.LinearAlgebra;

namespace FireFly.VI.SLAM.Sophus
{
    public class Sim3
    {
        private double _Scale = 1;
        private SE3 _SE3 = new SE3();

        public Sim3(double scale, double tx, double ty, double tz, double q1, double q2, double q3, double q4) : this(scale, Vector<double>.Build.DenseOfArray(new double[] { tx, ty, tz }), new Quaternion(q1, q2, q3, q4))
        {

        }

        public Sim3()
        {
        }

        public Sim3(double scale, Vector<double> translation, SO3 so3) : this(scale, new SE3(translation, so3))
        {
        }

        public Sim3(double scale, Vector<double> translation, Quaternion quaternion) : this(scale, new SE3(translation, new SO3(quaternion)))
        {
        }

        public Sim3(double scale, SE3 se3)
        {
            Scale = scale;
            _SE3 = se3;
        }

        public double Scale
        {
            get
            {
                return _Scale;
            }

            set
            {
                _Scale = value;
            }
        }

        public SE3 SE3
        {
            get
            {
                return _SE3;
            }

            set
            {
                _SE3 = value;
            }
        }

        public Matrix<double> Matrix
        {
            get
            {
                Matrix<double> result = SE3.Matrix;
                result[3, 3] = _Scale;
                return result;
            }
        }

        public Sim3 Inverse()
        {
            Sim3 result = new Sim3();

            result.SE3 = SE3.Inverse();
            result.Scale = 1 / Scale;
            result.SE3.Translation = result.Scale * result.SE3.Translation;

            return result;
        }
    }
}