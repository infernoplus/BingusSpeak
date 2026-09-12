using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BingusSpeak
{
    public class Utility
    {
        public static System.Numerics.Vector3 ToRadians(System.Numerics.Vector3 rotation)
        {
            const float pi = (float)Math.PI;
            const float d2r = pi / 180.0f;
            return rotation * d2r;
        }

        public static System.Numerics.Vector3 ToDegrees(System.Numerics.Vector3 rotation)
        {
            const float pi = (float)Math.PI;
            const float r2d = 180.0f / pi;
            return rotation * r2d;
        }

        /* Rotation conversion from Morrowind worldspace to Elden Ring worldspace. right hand XYZ to left hand XZY (afaik, it's very hard to pinpoint this and lots of guesswork) */
        public static System.Numerics.Vector3 ConvertRotation(System.Numerics.Vector3 rotation)
        {
            /* The following unholy code converts morrowind (Z up) euler rotations into dark souls (Y up) euler rotations */
            /* Big thanks to katalash, dropoff, and the TESUnity dudes for helping me sort this out */

            /* Katalashes code from MapStudio */
            Vector3 MatrixToEulerXZY(Matrix4x4 m)
            {
                const float Pi = (float)Math.PI;
                const float Deg2Rad = Pi / 180.0f;
                Vector3 ret;
                ret.Z = MathF.Asin(-Math.Clamp(-m.M12, -1, 1));

                if (Math.Abs(m.M12) < 0.9999999)
                {
                    ret.X = MathF.Atan2(-m.M32, m.M22);
                    ret.Y = MathF.Atan2(-m.M13, m.M11);
                }
                else
                {
                    ret.X = MathF.Atan2(m.M23, m.M33);
                    ret.Y = 0;
                }
                ret.X = ret.X <= -180.0f * Deg2Rad ? ret.X + 360.0f * Deg2Rad : ret.X;
                ret.Y = ret.Y <= -180.0f * Deg2Rad ? ret.Y + 360.0f * Deg2Rad : ret.Y;
                ret.Z = ret.Z <= -180.0f * Deg2Rad ? ret.Z + 360.0f * Deg2Rad : ret.Z;
                return ret;
            }

            /* Adapted code from https://github.com/ColeDeanShepherd/TESUnity */
            Quaternion xRot = Quaternion.CreateFromAxisAngle(new Vector3(1.0f, 0.0f, 0.0f), rotation.X);
            Quaternion yRot = Quaternion.CreateFromAxisAngle(new Vector3(0.0f, 1.0f, 0.0f), rotation.Z);
            Quaternion zRot = Quaternion.CreateFromAxisAngle(new Vector3(0.0f, 0.0f, 1.0f), rotation.Y);
            Quaternion q = xRot * zRot * yRot;

            return MatrixToEulerXZY(Matrix4x4.CreateFromQuaternion(q));
        }
    }
}
