namespace Sokoban.Core
{
    public enum Direction
    {
        Up = 0,
        Down = 1,
        Left = 2,
        Right = 3
    }

    /// <summary>Integer grid coordinate. Y grows downwards (row index), matching the text level format.</summary>
    public struct Int2 : System.IEquatable<Int2>
    {
        public int x;
        public int y;

        public Int2(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static Int2 operator +(Int2 a, Int2 b) { return new Int2(a.x + b.x, a.y + b.y); }
        public static Int2 operator -(Int2 a, Int2 b) { return new Int2(a.x - b.x, a.y - b.y); }
        public static bool operator ==(Int2 a, Int2 b) { return a.x == b.x && a.y == b.y; }
        public static bool operator !=(Int2 a, Int2 b) { return !(a == b); }
        public bool Equals(Int2 other) { return this == other; }
        public override bool Equals(object obj) { return obj is Int2 && this == (Int2)obj; }
        public override int GetHashCode() { return (x * 73856093) ^ (y * 19349663); }
        public override string ToString() { return "(" + x + "," + y + ")"; }
    }

    public static class DirectionUtil
    {
        public static readonly Direction[] All = { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

        public static Int2 Delta(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return new Int2(0, -1);
                case Direction.Down: return new Int2(0, 1);
                case Direction.Left: return new Int2(-1, 0);
                default: return new Int2(1, 0);
            }
        }

        public static Direction Opposite(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return Direction.Down;
                case Direction.Down: return Direction.Up;
                case Direction.Left: return Direction.Right;
                default: return Direction.Left;
            }
        }

        /// <summary>LURD notation: lower case = walk, upper case = push.</summary>
        public static char ToChar(Direction d, bool push)
        {
            char c;
            switch (d)
            {
                case Direction.Up: c = 'u'; break;
                case Direction.Down: c = 'd'; break;
                case Direction.Left: c = 'l'; break;
                default: c = 'r'; break;
            }
            return push ? char.ToUpperInvariant(c) : c;
        }

        public static bool TryParse(char c, out Direction d)
        {
            switch (char.ToLowerInvariant(c))
            {
                case 'u': d = Direction.Up; return true;
                case 'd': d = Direction.Down; return true;
                case 'l': d = Direction.Left; return true;
                case 'r': d = Direction.Right; return true;
            }
            d = Direction.Up;
            return false;
        }
    }
}
