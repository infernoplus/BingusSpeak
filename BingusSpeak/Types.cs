using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BingusSpeak
{
    public class Types
    {
        public class Int2
        {
            public readonly int x, y;
            public Int2(int x, int y)
            {
                this.x = x; this.y = y;
            }

            public static bool operator ==(Int2 a, Int2 b)
            {
                return a.Equals(b);
            }
            public static bool operator !=(Int2 a, Int2 b) => !(a == b);

            public bool Equals(Int2 b)
            {
                return x == b.x && y == b.y;
            }
            public override bool Equals(object a) => Equals(a as Int2);

            public static Int2 operator +(Int2 a, Int2 b)
            {
                return a.Add(b);
            }

            public Int2 Add(Int2 b)
            {
                return new Int2(x + b.x, y + b.y);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = x.GetHashCode();
                    hashCode = hashCode * 397 ^ y.GetHashCode();
                    return hashCode;
                }
            }

            public int[] Array()
            {
                int[] r = { x, y };
                return r;
            }
        }
    }
}
