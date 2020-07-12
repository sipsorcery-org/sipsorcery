namespace VP8L {
  using System;
  using System.Drawing;

  /// Quadruple of alpha, red, green and blue values.
  public struct Argb {
    public Byte A;
    public Byte R;
    public Byte G;
    public Byte B;
    public Argb(Byte a, Byte r, Byte g, Byte b) {
      this.A = a; this.R = r; this.G = g; this.B = b;
    }
    public Argb(Byte r, Byte g, Byte b) {
      this.A = 0xff; this.R = r; this.G = g; this.B = b;
    }
    public Argb(Color color) {
      this.A = color.A; this.R = color.R; this.G = color.G; this.B = color.B;
    }

    public uint ToUInt() {
      return ((uint)this.A << 24) | ((uint)this.R << 16) 
        | ((uint)this.G << 8) | (uint)this.B;
    }
    public override int GetHashCode() {
      return (int)this.ToUInt();
    }
    public override string ToString() {
      return String.Format("{0:X8}", this.ToUInt());
    }

    static public bool operator==(Argb a1, Argb a2) {
      return a1.ToUInt() == a2.ToUInt();
    }
    static public bool operator!=(Argb a1, Argb a2) {
      return a1.ToUInt() != a2.ToUInt();
    }
    public override bool Equals(object o) {
      if(o is Argb) {
        return this == (Argb)o;
      } else {
        return false;
      }
    }

    public static Argb operator+(Argb a1, Argb a2) {
      return new Argb((byte)(a1.A + a2.A), (byte)(a1.R + a2.R),
          (byte)(a1.G + a2.G), (byte)(a1.B + a2.B));
    }
    public static Argb operator-(Argb a1, Argb a2) {
      return new Argb((byte)(a1.A - a2.A), (byte)(a1.R - a2.R),
          (byte)(a1.G - a2.G), (byte)(a1.B - a2.B));
    }

    internal static uint Hash(Argb a1, Argb a2, Argb a3) {
      uint h1 = Hash(a1);
      uint h2 = Hash(a2);
      uint h3 = Hash(a3);
      uint x1 = h1 ^ (h2 * 32369) ^ ~(h3 * 39217);
      uint x2 = ~(h1 * 40483) ^ h2 ^ (h3 * 42943);
      uint x3 = (x1 << 3) ^ (x2 >> 7);
      return x3;
    }
    internal static uint Hash(Argb argb) {
      uint x1 = argb.ToUInt();
      uint x2 = (x1 * 47491) ^ ~(x1 * 41227);
      uint x3 = x2 * (x2 << 3) ^ ~(x2 >> 5) ^ (x2 >> 13);
      uint x4 = (x3 * 20389) ^ ~(x3 * 28111);
      return x4;
    }
  }
}
