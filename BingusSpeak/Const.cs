using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BingusSpeak
{
    public class Const
    {
        public static int THREAD_COUNT = 16;
        public static readonly float GLOBAL_SCALE = 0.0129f;
        public static readonly int CELL_EXTERIOR_BOUNDS = 30;
        public static readonly float CELL_SIZE = 8192f * GLOBAL_SCALE;
        public static readonly int FIGHT_THRESHOLD = 81;
    }
}
